#!/usr/bin/env python3
"""Read-only fixed-step model of the Drop ECS battle.

Usage:
  python3 Tools/BalanceValidation/battle_model.py --root . --stage 1 \\
      --cats-json '[{"slot":0,"column":0,"row":0,"level":1}]'

The loader intentionally reads the authored Unity assets and rejects incomplete
assets.  It does not write project files or emulate pools/views.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import re
import struct
from pathlib import Path
from typing import Any


_ASSET = Path("Assets/Projects/Assets/Configuration")
_SOURCE_FILES = (
    "Assets/Projects/Assets/Configuration/CatConfig.asset",
    "Assets/Projects/Assets/Configuration/BrickConfig.asset",
    "Assets/Projects/Assets/Configuration/StageBalanceConfig.asset",
    "Assets/Projects/Scripts/GamePlay/Drop/DropStageFactory.cs",
    "Assets/Projects/Scripts/GamePlay/Drop/Ecs/Systems/FallSystem.cs",
    "Assets/Projects/Scripts/GamePlay/Drop/Ecs/Systems/GateCollisionSystem.cs",
    "Assets/Projects/Scripts/GamePlay/Drop/Ecs/Systems/DamageAndBreakSystem.cs",
    "Assets/Projects/Scripts/GamePlay/Drop/Ecs/Systems/RunResultSystem.cs",
    "Assets/Projects/Scripts/GamePlay/World/WorldPlayfieldLayout.cs",
    "Assets/Projects/Scripts/GamePlay/Configs/CatConfig.cs",
    "Assets/Projects/Scripts/GamePlay/Configs/BrickConfig.cs",
    "Assets/Projects/Scripts/GamePlay/Configs/StageBalanceConfig.cs",
    "Assets/Projects/Scripts/Data/PlayerSaveData.cs",
    "Assets/Projects/Scripts/GamePlay/Drop/DropRewardPersistence.cs",
)


def _fail(message: str) -> None:
    raise ValueError("battle_model: " + message)


def _read(root: Path, relative: str) -> str:
    path = root / relative
    if not path.is_file():
        _fail("required source is missing: " + relative)
    return path.read_text(encoding="utf-8")


def _scalar(text: str, name: str, cast: type = int) -> Any:
    match = re.search(r"(?m)^  " + re.escape(name) + r": ([^\r\n]+)$", text)
    if not match:
        _fail("missing authored field " + name)
    raw = match.group(1).strip()
    try:
        return cast(raw)
    except ValueError as exc:
        raise ValueError("battle_model: invalid " + name + ": " + raw) from exc


def _array_records(text: str, key: str, fields: tuple[str, ...]) -> list[dict[str, int]]:
    header = re.search(r"(?m)^  " + re.escape(key) + r":\s*$", text)
    if not header:
        _fail("missing authored array " + key)
    rest = text[header.end():]
    end = re.search(r"(?m)^  [A-Za-z_][A-Za-z0-9_]*:", rest)
    section = rest[:end.start()] if end else rest
    records: list[dict[str, int]] = []
    current: dict[str, int] | None = None
    for line in section.splitlines():
        match = re.match(r"\s*- (\w+): ([^\s]+)\s*$", line)
        if match:
            if current is not None:
                records.append(current)
            current = {match.group(1): int(match.group(2))}
            continue
        match = re.match(r"\s+(\w+): ([^\s]+)\s*$", line)
        if match and current is not None:
            current[match.group(1)] = int(match.group(2))
    if current is not None:
        records.append(current)
    if not records or any(any(field not in record for field in fields) for record in records):
        _fail("malformed authored array " + key)
    return records


def _csharp_int(text: str, pattern: str, label: str) -> int:
    matches = re.findall(pattern, text)
    if len(matches) != 1:
        _fail("cannot uniquely read PlayerSaveData " + label)
    try:
        return int(matches[0])
    except ValueError as exc:
        raise ValueError("battle_model: invalid PlayerSaveData " + label) from exc


def _hashes(root: Path, generated_relative: str) -> dict[str, str]:
    paths = list(_SOURCE_FILES) + [generated_relative]
    result: dict[str, str] = {}
    for relative in paths:
        data = _read(root, relative).encode("utf-8")
        result[relative] = hashlib.sha256(data).hexdigest()
    return result


def _resolve_stage_asset(root: Path, balance_text: str) -> str:
    section = re.search(r"(?ms)^  stageDefinitions:\s*$([\s\S]*?)(?=^  \w+:|\Z)", balance_text)
    refs = re.findall(r"guid: ([0-9a-f]{32})", section.group(1) if section else "")
    if not refs:
        _fail("StageBalanceConfig has no stage definition references")
    guid = refs[0]
    matches: list[Path] = []
    for meta in (root / "Assets").rglob("*.asset.meta"):
        if re.search(r"(?m)^guid: " + re.escape(guid) + r"\s*$", meta.read_text(encoding="utf-8")):
            matches.append(meta.with_suffix(""))
    if len(matches) != 1:
        _fail("cannot uniquely resolve stage definition asset GUID " + guid)
    try:
        return matches[0].relative_to(root).as_posix()
    except ValueError as exc:
        raise ValueError("battle_model: resolved stage asset outside root") from exc


def _parse_stages(text: str) -> dict[int, dict[str, Any]]:
    stages: dict[int, dict[str, Any]] = {}
    for document in re.split(r"(?m)^--- !u!114[^\n]*\n", text):
        if not re.search(r"(?m)^  stageIndex:", document):
            continue
        index = _scalar(document, "stageIndex")
        columns = _scalar(document, "boardColumns")
        rows = _scalar(document, "brickRows")
        if index < 1 or columns < 1 or rows < 1:
            _fail("invalid StageDefinition dimensions")
        cells = _array_records(document, "cells", ("Present", "Kind", "Level"))
        if len(cells) != columns * rows:
            _fail("Stage %d has %d cells; expected %d" % (index, len(cells), columns * rows))
        parsed = []
        for cell in cells:
            kind_value = cell["Kind"]
            if kind_value not in (0, 1, 2) or cell["Present"] not in (0, 1) or cell["Level"] < 0:
                _fail("Stage %d contains an unsupported cell" % index)
            if cell["Present"] and cell["Level"] < 1:
                _fail("Stage %d has a present cell without level" % index)
            parsed.append({"present": bool(cell["Present"]),
                           "kind": ("normal", "gold", "item")[kind_value],
                           "level": cell["Level"]})
        if index in stages:
            _fail("duplicate stage index %d" % index)
        stages[index] = {"columns": columns, "rows": rows, "board_rows": 3, "cells": parsed}
    if not stages:
        _fail("stage asset contained no StageDefinition documents")
    return stages


def load_snapshot(root: str | Path) -> dict[str, Any]:
    """Load exact authored combat/economy input from a project root."""
    project = Path(root).resolve()
    cat_text = _read(project, (_ASSET / "CatConfig.asset").as_posix())
    brick_text = _read(project, (_ASSET / "BrickConfig.asset").as_posix())
    balance_relative = (_ASSET / "StageBalanceConfig.asset").as_posix()
    balance_text = _read(project, balance_relative)
    save_text = _read(project, "Assets/Projects/Scripts/Data/PlayerSaveData.cs")
    stages_relative = _resolve_stage_asset(project, balance_text)
    stages = _parse_stages(_read(project, stages_relative))

    cat_stats = _array_records(cat_text, "combatStatsByLevel", ("impactDamage", "bounceCount"))
    cats = [{"damage": record["impactDamage"], "bounces": record["bounceCount"]}
            for record in cat_stats]
    if not cats or any(row["damage"] < 1 or row["bounces"] < 0 for row in cats):
        _fail("CatConfig combat stats are invalid")
    brick_stats = _array_records(brick_text, "levelConfigs", ("maxHealth", "goldReward"))
    bricks = [{"health": row["maxHealth"], "gold": row["goldReward"]} for row in brick_stats]
    if not bricks or any(row["health"] < 1 or row["gold"] < 0 for row in bricks):
        _fail("BrickConfig level stats are invalid")

    weights_match = re.search(r"(?m)^  spawnWeightsFromLowestLevel: ([^\r\n]+)$", balance_text)
    if not weights_match:
        _fail("missing authored spawn weights")
    encoded_weights = weights_match.group(1).strip()
    if re.fullmatch(r"[0-9a-fA-F]+", encoded_weights) and len(encoded_weights) % 8 == 0:
        weights = [struct.unpack("<i", bytes.fromhex(encoded_weights)[offset:offset + 4])[0]
                   for offset in range(0, len(encoded_weights) // 2, 4)]
    else:
        # Unity also writes YAML sequences for this field in some serialization modes.
        weights = [int(v) for v in re.findall(r"-?\d+", encoded_weights)]
    if not weights or any(weight < 0 for weight in weights):
        _fail("invalid authored spawn weights")
    base_height = _scalar(cat_text, "baseBounceHeight", float)
    strength = _scalar(cat_text, "bounceStrength", float)
    duration = _scalar(cat_text, "bounceDuration", float)
    fall_speed = _scalar(cat_text, "fallSpeed", float)
    acceleration = _scalar(cat_text, "fallAcceleration", float)
    if min(base_height, strength, duration, fall_speed, acceleration) <= 0:
        _fail("CatConfig has non-positive physics")
    physics = {"fall_speed": fall_speed, "fall_acceleration": acceleration,
               "bounce_height": base_height * strength,
               "bounce_duration": max(duration, 4.0 * base_height * strength / (18.0 * fall_speed)),
               "drop_start_y": -4.2, "drop_travel": 18.0, "first_tile_progress": .26,
               "tile_row_pitch": 1.2, "tile_height": 1.28,
               "cat_visual_size": 1.08, "cat_visual_offset_y": .06}
    snapshot = {
        "stages": stages, "cats": cats, "bricks": bricks,
        "balance": {"summon_gold_per_max": _scalar(balance_text, "summonGoldPerMaximumSpawnLevel"),
                    "minimum_gap": _scalar(balance_text, "minimumSummonGap"),
                    "maximum_gap": _scalar(balance_text, "maximumSummonGap"),
                    "weights": weights, "clear_summon_count": _scalar(balance_text, "clearSummonCount")},
        "initial_gold": _csharp_int(save_text, r"private int gold = (-?\d+);", "gold default"),
        "reserve_capacity": _csharp_int(save_text,
            r"public const int DefaultReserveCapacity = (-?\d+);", "DefaultReserveCapacity"),
        "physics": physics,
        "source_hashes": _hashes(project, stages_relative),
    }
    return snapshot


def _contact(physics: dict[str, float], row: int) -> float:
    return (physics["first_tile_progress"] + row * physics["tile_row_pitch"] / physics["drop_travel"]
            - (physics["tile_height"] * .5 + physics["cat_visual_size"] * .5
               - physics["cat_visual_offset_y"]) / physics["drop_travel"])


def simulate(snapshot: dict[str, Any], stage_index: int, cats: list[dict[str, int]],
             dt: float = .05, max_steps: int = 480, initial_health: list[int] | None = None) -> dict[str, Any]:
    """Run Fall -> GateCollision -> DamageAndBreak -> RowAdvance -> RunResult.

    `normal_gold` is always raw per-run earned normal-brick gold.  Ticket fields
    are raw break counts; the current cumulative loop banks them after each Drop.
    Optional initial_health resumes prior damage; returned brick_health preserves it.
    """
    if not isinstance(stage_index, int) or stage_index not in snapshot.get("stages", {}):
        _fail("stage index is not in snapshot")
    if not isinstance(dt, (int, float)) or dt <= 0 or not isinstance(max_steps, int) or max_steps < 1:
        _fail("dt must be positive and max_steps must be positive")
    stage = snapshot["stages"][stage_index]
    physics = snapshot.get("physics")
    level_stats = snapshot.get("cats")
    brick_stats = snapshot.get("bricks")
    if not isinstance(physics, dict) or not isinstance(level_stats, list) or not isinstance(brick_stats, list):
        _fail("unsupported snapshot shape")
    columns, rows, board_rows = stage["columns"], stage["rows"], stage["board_rows"]
    if board_rows != 3:
        _fail("model only supports the authored three board rows")
    if initial_health is not None and len(initial_health) != columns * rows:
        _fail("initial_health shape differs from stage")
    bricks: dict[tuple[int, int], dict[str, Any]] = {}
    def remaining_health():
        return [bricks.get(divmod(i, columns), {}).get("health", 0) for i in range(columns * rows)]
    last_row = -1
    for index, cell in enumerate(stage["cells"]):
        if not cell["present"]:
            continue
        if cell["level"] > len(brick_stats):
            _fail("brick level is outside authored BrickConfig")
        row, column = divmod(index, columns)
        kind = cell["kind"]
        health = 1 if kind in ("gold", "item") else brick_stats[cell["level"] - 1]["health"]
        last_row = max(last_row, row)
        if initial_health is not None:
            if not 0 <= initial_health[index] <= health:
                _fail("initial_health outside authored health")
            health = initial_health[index]
        if health == 0:
            continue
        bricks[(row, column)] = {"health": health, "kind": kind,
                                 "gold": brick_stats[cell["level"] - 1]["gold"] if kind == "normal" else 0}
        last_row = max(last_row, row)
    goal = (physics["first_tile_progress"] + max(0, last_row) * physics["tile_row_pitch"] / physics["drop_travel"]
            + (physics["tile_height"] * .5 + .25) / physics["drop_travel"])
    runtime: list[dict[str, Any]] = []
    for source in cats:
        if not isinstance(source, dict):
            _fail("cat must be a dict")
        required = ("slot", "column", "row", "level")
        if any(key not in source or not isinstance(source[key], int) for key in required):
            _fail("cat needs integer slot, column, row, level")
        column, row, level = source["column"], source["row"], source["level"]
        if not (0 <= column < columns and 0 <= row < board_rows and level >= 1):
            _fail("cat is outside the authored board or combat table")
        initial_y = (physics["drop_start_y"] - ((board_rows - 1) * .5 - row) * physics["tile_row_pitch"]) / physics["drop_travel"]
        # CatConfig uses explicit entries when present, then the documented legacy
        # curves for higher levels.  The oracle includes level 25 against a 24-row
        # authored table, so retaining this branch is required source behavior.
        stats = (level_stats[level - 1] if level <= len(level_stats)
                 else {"damage": level, "bounces": level * 2})
        runtime.append({"column": column, "y": initial_y, "previous_y": initial_y, "velocity": 0.0,
                        "bounces": stats["bounces"], "damage": stats["damage"], "bouncing": False,
                        "bounce_elapsed": physics["bounce_duration"], "spent": False})
    if not runtime:
        _fail("drop request has no cats")
    phase = "falling"
    normal_gold = gold_tickets = item_tickets = 0
    for step in range(1, max_steps + 1):
        # FallSystem: exact trapezoidal fall and exact authored parabola for rebounds.
        for cat in runtime:
            cat["previous_y"] = cat["y"]
            if cat["bouncing"]:
                cat["bounce_elapsed"] = min(cat["bounce_elapsed"] + dt, physics["bounce_duration"])
                t = cat["bounce_elapsed"] / physics["bounce_duration"]
                h = physics["bounce_height"] / physics["drop_travel"]
                cat["y"] = cat["bounce_origin"] - h * 4.0 * t * (1.0 - t)
                cat["velocity"] = h * 4.0 * (2.0 * t - 1.0) / physics["bounce_duration"]
                if t >= 1.0:
                    cat["bouncing"] = False
            else:
                next_velocity = min(physics["fall_speed"], cat["velocity"] + physics["fall_acceleration"] * dt)
                cat["y"] += (cat["velocity"] + next_velocity) * .5 * dt
                cat["velocity"] = next_velocity
        # GateCollisionSystem processes cats in creation/input order.
        for cat in runtime:
            if cat["velocity"] <= 0.0:
                continue
            for row in range(rows):
                contact = _contact(physics, row)
                if cat["y"] < contact:
                    break
                brick = bricks.get((row, cat["column"]))
                if brick is None:
                    continue
                if cat["previous_y"] > contact + .001:
                    continue
                brick["health"] -= cat["damage"]
                can_bounce = cat["bounces"] > 0
                if can_bounce:
                    cat["bounces"] -= 1
                if brick["health"] > 0:
                    cat["y"] = contact - .001
                    if can_bounce:
                        cat["bounce_origin"] = cat["y"]
                        cat["bounce_elapsed"] = 0.0
                        cat["bouncing"] = True
                        cat["velocity"] = -4.0 * (physics["bounce_height"] / physics["drop_travel"]) / physics["bounce_duration"]
                    else:
                        cat["spent"] = True
                        cat["bouncing"] = False
                        cat["velocity"] = 0.0
                    break
                # Lethal impact: the brick remains until DamageAndBreak, so later
                # cats cannot hit it this frame. This cat keeps checking lower rows.
                del bricks[(row, cat["column"])]
                if brick["kind"] == "normal":
                    normal_gold = min(2_147_483_647, normal_gold + brick["gold"])
                elif brick["kind"] == "gold":
                    gold_tickets = min(2_147_483_647, gold_tickets + 1)
                else:
                    item_tickets = min(2_147_483_647, item_tickets + 1)
        # DamageAndBreak removes spent cats before RunResult.
        # EntityManager destruction uses swap-back storage. DamageAndBreak walks a
        # snapshot of entity identities, so preserve the resulting chunk order for
        # the next frame's GateCollision query rather than stable-filtering cats.
        damage_query = list(runtime)
        for cat in damage_query:
            if not cat["spent"]:
                continue
            location = next(index for index, candidate in enumerate(runtime) if candidate is cat)
            runtime[location] = runtime[-1]
            runtime.pop()
        if any(cat["y"] >= goal for cat in runtime):
            phase = "won"
        elif not runtime:
            phase = "lost"
        if phase != "falling":
            return {"won": phase == "won", "phase": "Won" if phase == "won" else "Lost", "normal_gold": normal_gold,
                    "gold_tickets": gold_tickets, "item_tickets": item_tickets,
                    "steps": step, "seconds": step * float(dt), "brick_health": remaining_health()}
    return {"won": False, "phase": "Timeout", "normal_gold": normal_gold,
            "gold_tickets": gold_tickets, "item_tickets": item_tickets,
            "steps": max_steps, "seconds": max_steps * float(dt), "brick_health": remaining_health()}


def _main() -> None:
    parser = argparse.ArgumentParser(description="Run the read-only Drop ECS battle model")
    parser.add_argument("--root", required=True, help="Unity project root")
    parser.add_argument("--stage", type=int, required=True)
    parser.add_argument("--cats-json", required=True, help="JSON list of {slot,column,row,level}")
    parser.add_argument("--dt", type=float, default=.05)
    parser.add_argument("--max-steps", type=int, default=480)
    args = parser.parse_args()
    cats = json.loads(args.cats_json)
    if not isinstance(cats, list):
        _fail("--cats-json must decode to a list")
    print(json.dumps(simulate(load_snapshot(args.root), args.stage, cats, args.dt, args.max_steps), sort_keys=True))


if __name__ == "__main__":
    _main()
