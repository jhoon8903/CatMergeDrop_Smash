#!/usr/bin/env python3
"""Read-only full-game economy validation for Cat Merge Drop.

This script deliberately models persistence outside Unity.  It reads the snapshot
created by ``battle_model.load_snapshot`` and only calls its deterministic combat
``simulate`` function.  It never writes game assets or save data.

The report contains two scenarios: ``actual`` starts at the authored initial gold
(currently 30); ``what_if_start_gold_60`` is a diagnostic only, equal to three
current first-summon costs.  It is not a balancing recommendation.
"""
from __future__ import annotations

import argparse
import json
import random
import statistics
from collections import Counter
from dataclasses import dataclass, field
from pathlib import Path
from typing import Any

from battle_model import load_snapshot, simulate


POLICIES = ("auto_merge_spread", "auto_merge_focus", "preserve_count_focus")
SCENARIOS = ("actual", "what_if_start_gold_60")
TRACE_STAGE_LIMIT = 12
TRACE_STOP_LIMIT = 8


@dataclass
class OwnedCat:
    ident: int
    level: int


@dataclass
class Economy:
    gold: int
    historical_max: int = 1
    owned: list[OwnedCat] = field(default_factory=list)
    next_ident: int = 1
    summons: int = 0
    merges: int = 0
    gold_tickets_opened: int = 0
    item_tickets_opened: int = 0
    capacity_overflow: int = 0

    def add(self, level: int) -> None:
        if level < 1:
            raise ValueError(f"invalid cat level {level}")
        self.owned.append(OwnedCat(self.next_ident, level))
        self.next_ident += 1

    def merge(self, left: OwnedCat, right: OwnedCat) -> None:
        if left.ident == right.ident or left.level != right.level:
            raise ValueError("only two distinct equal-level cats may merge")
        self.owned.remove(left)
        self.owned.remove(right)
        self.add(left.level + 1)
        self.historical_max = max(self.historical_max, left.level + 1)
        self.merges += 1


def _balance(snapshot: dict[str, Any]) -> dict[str, Any]:
    required = {"summon_gold_per_max", "minimum_gap", "maximum_gap", "weights", "clear_summon_count"}
    balance = snapshot["balance"]
    missing = required.difference(balance)
    if missing:
        raise KeyError(f"snapshot balance missing {sorted(missing)}")
    return balance


def summon_range(economy: Economy, balance: dict[str, Any]) -> tuple[int, int]:
    highest = max(1, economy.historical_max)
    minimum = max(1, highest - int(balance["minimum_gap"]))
    maximum = max(minimum, highest - int(balance["maximum_gap"]))
    return minimum, maximum


def summon_cost(economy: Economy, balance: dict[str, Any]) -> int:
    _, maximum = summon_range(economy, balance)
    return int(balance["summon_gold_per_max"]) * maximum


def weighted_summon_level(economy: Economy, balance: dict[str, Any], rng: random.Random) -> int:
    minimum, maximum = summon_range(economy, balance)
    weights = [max(0, int(value)) for value in balance["weights"]]
    active = [weights[min(offset, len(weights) - 1)] if weights else 1
              for offset in range(maximum - minimum + 1)]
    return rng.choices(range(minimum, maximum + 1), weights=active, k=1)[0]


def stage_dimensions(snapshot: dict[str, Any], stage_index: int) -> tuple[int, int]:
    stage = snapshot["stages"][stage_index]
    # ``rows`` describes the brick grid.  The merge formation is always the
    # separately authored three-row board, exposed in the snapshot as board_rows.
    return int(stage["columns"]), int(stage["board_rows"])


def _select_for_policy(economy: Economy, policy: str, slots: int) -> list[OwnedCat]:
    ordered = sorted(economy.owned, key=lambda cat: (-cat.level, cat.ident))
    if policy == "auto_merge_focus" or policy == "preserve_count_focus":
        return ordered[:min(3, slots)]
    return ordered[:slots]


def _merge_candidates(economy: Economy, policy: str, slots: int) -> list[OwnedCat]:
    # Focus controls the submitted drop formation, not the board-management pass.
    # Auto merge can first form as many cats as the physical board holds, merge them,
    # then reserve all but the strongest three for the focused drop.
    return sorted(economy.owned, key=lambda cat: (-cat.level, cat.ident))[:slots]


def _can_merge(policy: str, cats: list[OwnedCat], level: int, capacity_pressure: bool) -> bool:
    if policy != "preserve_count_focus":
        return True
    counts = Counter(cat.level for cat in cats)
    highest = max(counts, default=0)
    # This policy deliberately retains pairs.  It only raises the top available
    # level with a three-cat buffer, unless the declared board+reserve space fills.
    return capacity_pressure or (level == highest and counts[level] >= 3)


def has_merge_change(economy: Economy, policy: str, slots: int, reserve_capacity: int) -> bool:
    candidates = _merge_candidates(economy, policy, slots)
    counts = Counter(cat.level for cat in candidates)
    pressure = len(economy.owned) >= slots + reserve_capacity
    return any(count >= 2 and _can_merge(policy, candidates, level, pressure)
               for level, count in counts.items())


def merge_mass(cats: list[OwnedCat]) -> int:
    """Two cats at level N merge to one at N+1, preserving this mass."""
    return sum(1 << cat.level for cat in cats)


def merge_preparation(economy: Economy, policy: str, slots: int, reserve_capacity: int) -> int:
    """Perform policy-permitted merges among currently placeable cats.

    ``AddOwnedCat`` in PlayerSaveData does not enforce reserve capacity.  We retain
    every real reward and only use board+reserve as a pressure signal, recording any
    excess instead of deleting or inventing cats.
    """
    merged = 0
    while True:
        candidates = _merge_candidates(economy, policy, slots)
        counts = Counter(cat.level for cat in candidates)
        capacity_pressure = len(economy.owned) >= slots + reserve_capacity
        pairs = [level for level, count in counts.items()
                 if count >= 2 and _can_merge(policy, candidates, level, capacity_pressure)]
        if not pairs:
            break
        level = max(pairs)  # deterministic: raise the strongest eligible pair first
        pair = [cat for cat in candidates if cat.level == level][:2]
        before_mass = merge_mass(economy.owned)
        economy.merge(pair[0], pair[1])
        if merge_mass(economy.owned) != before_mass:
            raise AssertionError("merge mass conservation violated")
        merged += 1
    economy.capacity_overflow = max(economy.capacity_overflow, len(economy.owned) - slots - reserve_capacity)
    return merged


def prepare_cats(economy: Economy, policy: str, columns: int, rows: int) -> dict[str, int]:
    slots = columns * rows
    chosen = _select_for_policy(economy, policy, slots)
    cats: dict[str, dict[str, int]] = {}
    if policy == "auto_merge_spread":
        # Fill top-to-bottom, rotating columns so this policy has no lane knowledge.
        for index, cat in enumerate(chosen):
            column = index % columns
            row = index // columns
            cats[str(cat.ident)] = {"slot": row * columns + column, "column": column, "row": row, "level": cat.level}
    else:
        # Focus always uses one column.  The runner rotates that column after failures.
        # Callers replace these positions with their current rotation before combat.
        for row, cat in enumerate(chosen):
            cats[str(cat.ident)] = {"slot": row * columns, "column": 0, "row": row, "level": cat.level}
    return cats


def rotate_focus(cats: dict[str, dict[str, int]], columns: int, lane: int) -> dict[str, dict[str, int]]:
    if columns < 1:
        raise ValueError("stage must have at least one board column")
    rotated: dict[str, dict[str, int]] = {}
    for ident, cat in cats.items():
        copy = dict(cat)
        copy["column"] = lane % columns
        copy["slot"] = copy["row"] * columns + copy["column"]
        rotated[ident] = copy
    return rotated


def buy_summons(economy: Economy, balance: dict[str, Any], rng: random.Random, slots: int) -> int:
    """Buy only while there is room to place a newly summoned cat this preparation."""
    bought = 0
    # The game summon button requires an empty board slot.  Policies may reserve cats,
    # but cannot use that to create unlimited purchases, so the physical board caps a
    # preparation's summons.
    room = max(0, slots - min(slots, len(economy.owned)))
    while bought < room:
        cost = summon_cost(economy, balance)
        if cost <= 0 or economy.gold < cost:
            break
        economy.gold -= cost
        economy.add(weighted_summon_level(economy, balance, rng))
        economy.summons += 1
        bought += 1
    if economy.gold < 0:
        raise AssertionError("gold conservation violated: negative gold")
    return bought


def open_success_tickets(economy: Economy, balance: dict[str, Any], result: dict[str, Any], rng: random.Random,
                         slots: int, reserve_capacity: int) -> None:
    gold_count = int(result.get("gold_tickets", 0))
    item_count = int(result.get("item_tickets", 0))
    if gold_count < 0 or item_count < 0:
        raise ValueError("combat returned negative ticket count")
    economy.gold += gold_count * 3 * summon_cost(economy, balance)
    economy.gold_tickets_opened += gold_count
    _, maximum = summon_range(economy, balance)
    for _ in range(item_count):
        economy.add(rng.randint(1, maximum))
    economy.item_tickets_opened += item_count
    economy.capacity_overflow = max(economy.capacity_overflow, len(economy.owned) - slots - reserve_capacity)


def quantiles(values: list[float]) -> dict[str, float | None]:
    if not values:
        return {"p50": None, "p90": None, "p99": None}
    values = sorted(values)
    def pick(percentile: float) -> float:
        return values[min(len(values) - 1, round((len(values) - 1) * percentile))]
    return {"p50": pick(.5), "p90": pick(.9), "p99": pick(.99)}


def run_seed(snapshot: dict[str, Any], policy: str, seed: int, start_gold: int, end_stage: int,
             max_attempts_per_stage: int, max_runs: int) -> dict[str, Any]:
    balance = _balance(snapshot)
    reserve_capacity = int(snapshot["reserve_capacity"])
    rng = random.Random(seed)
    economy = Economy(gold=start_gold)
    stage = 1
    attempts = wins = fails = farming = runs = 0
    lane = 0
    per_stage_attempts: dict[str, int] = {}
    combat_seconds: list[float] = []
    trace: list[dict[str, Any]] = []
    stops: list[dict[str, Any]] = []
    status = "completed"
    stagnant_lanes: set[int] = set()
    traced_stages: set[int] = set()

    while stage <= end_stage:
        if runs >= max_runs:
            status = "censored_max_runs"
            stops.append({"stage": stage, "reason": "max_runs", "value": max_runs})
            break
        columns, rows = stage_dimensions(snapshot, stage)
        slots = columns * rows
        merge_preparation(economy, policy, slots, reserve_capacity)
        buy_summons(economy, balance, rng, slots)
        merge_preparation(economy, policy, slots, reserve_capacity)
        cats = prepare_cats(economy, policy, columns, rows)
        if policy != "auto_merge_spread":
            cats = rotate_focus(cats, columns, lane)

        cost = summon_cost(economy, balance)
        no_progress_action = not cats and economy.gold < cost
        if no_progress_action:
            status = "policy_blocked_no_resources"
            stops.append({"stage": stage, "reason": "no_submitted_cats_or_affordable_summon", "gold": economy.gold, "cost": cost})
            break
        stage_attempts = per_stage_attempts.get(str(stage), 0)
        if stage_attempts >= max_attempts_per_stage:
            status = "censored_max_attempts_per_stage"
            stops.append({"stage": stage, "reason": "max_attempts_per_stage", "value": max_attempts_per_stage})
            break

        before_ids = set(cats)
        before_mass = merge_mass(economy.owned)
        # battle_model accepts a list of snapshot records.  The ID-keyed mapping
        # stays local so successful runs can consume exactly those owned cats.
        result = simulate(snapshot, stage, list(cats.values()), dt=.05, max_steps=480)
        required = {"won", "phase", "normal_gold", "gold_tickets", "item_tickets", "steps", "seconds"}
        missing = required.difference(result)
        if missing:
            raise KeyError(f"combat result missing {sorted(missing)}")
        earned_gold = int(result["normal_gold"])
        if earned_gold < 0:
            raise ValueError("combat returned negative normal_gold")
        attempts += 1
        runs += 1
        per_stage_attempts[str(stage)] = stage_attempts + 1
        combat_seconds.append(float(result["seconds"]))
        # A fixed-step timeout is a model censor, not a lost run: no earned gold,
        # ticket, cat, or lane settlement is applied from its partial simulation.
        if result["phase"] == "Timeout":
            status = "censored_combat_timeout"
            stops.append({"stage": stage, "reason": "combat_timeout", "steps": result["steps"]})
            if stage <= TRACE_STAGE_LIMIT and stage not in traced_stages:
                traced_stages.add(stage)
                trace.append({"stage": stage, "outcome": "timeout", "phase": result["phase"],
                              "cats": len(cats), "gold": economy.gold,
                              "historical_max": economy.historical_max, "seconds": result["seconds"]})
            break
        economy.gold += earned_gold
        if result["won"]:
            winners = [cat for cat in economy.owned if str(cat.ident) in before_ids]
            economy.owned = [cat for cat in economy.owned if str(cat.ident) not in before_ids]
            if len(winners) != len(before_ids):
                raise AssertionError("submitted-cat ownership changed before a success could consume it")
            submitted_mass = merge_mass(winners)
            if merge_mass(economy.owned) != before_mass - submitted_mass:
                raise AssertionError("success must consume exactly the submitted cats")
            economy.gold += summon_cost(economy, balance) * int(balance["clear_summon_count"])
            open_success_tickets(economy, balance, result, rng, slots, reserve_capacity)
            wins += 1
            outcome = "win"
            stage += 1
            lane = 0
            stagnant_lanes.clear()
        else:
            after_mass = merge_mass(economy.owned)
            if after_mass != before_mass:
                raise AssertionError("failed combat must preserve all cat ownership/mass")
            fails += 1
            if earned_gold > 0:
                farming += 1
            outcome = "fail"
            lane_key = lane if policy != "auto_merge_spread" else 0
            no_change = (earned_gold == 0
                         and economy.gold < summon_cost(economy, balance)
                         and not has_merge_change(economy, policy, slots, reserve_capacity))
            if no_change:
                stagnant_lanes.add(lane_key)
                lanes_required = columns if policy != "auto_merge_spread" else 1
                if len(stagnant_lanes) >= lanes_required:
                    status = "policy_blocked_exhausted_lanes_no_resources"
                    stops.append({"stage": stage, "reason": "all_policy_lanes_failed_without_income_or_action",
                                  "lanes": lanes_required, "gold": economy.gold, "cost": summon_cost(economy, balance)})
            else:
                stagnant_lanes.clear()
            if policy != "auto_merge_spread":
                lane = (lane + 1) % columns
        if economy.gold < 0:
            raise AssertionError("gold conservation violated: negative gold")
        attempted_stage = stage - (1 if outcome == "win" else 0)
        if attempted_stage <= TRACE_STAGE_LIMIT and attempted_stage not in traced_stages:
            traced_stages.add(attempted_stage)
            trace.append({"stage": attempted_stage, "outcome": outcome,
                          "phase": result["phase"], "cats": len(cats), "gold": economy.gold,
                          "historical_max": economy.historical_max, "seconds": result["seconds"]})
        if status.startswith("policy_blocked_"):
            break

    return {"seed": seed, "policy": policy, "completed_stage": stage - 1, "status": status,
            "attempts": attempts, "wins": wins, "fails": fails, "farming_attempts": farming,
            "summons": economy.summons, "merges": economy.merges, "gold": economy.gold,
            "historical_max": economy.historical_max, "owned_cats": len(economy.owned),
            "max_declared_capacity_overflow": economy.capacity_overflow,
            "per_stage_attempts": per_stage_attempts, "combat_seconds": quantiles(combat_seconds),
            "combat_seconds_total": sum(combat_seconds), "trace": trace, "stops": stops[:TRACE_STOP_LIMIT]}


def summarize(runs: list[dict[str, Any]]) -> dict[str, Any]:
    completed = [run["completed_stage"] for run in runs]
    attempts = [run["attempts"] for run in runs]
    statuses = Counter(run["status"] for run in runs)
    return {"seed_count": len(runs),
            "completed_stage": quantiles([run["completed_stage"] for run in runs]),
            "completed_stage_min": min(completed), "completed_stage_max": max(completed),
            "attempts": quantiles(attempts),
            "attempts_mean": statistics.mean(attempts), "attempts_median": statistics.median(attempts),
            "combat_seconds": quantiles([run["combat_seconds_total"] for run in runs]),
            "total_wins": sum(run["wins"] for run in runs),
            "total_fails": sum(run["fails"] for run in runs),
            "positive_income_failures": sum(run["farming_attempts"] for run in runs),
            "censored_seed_count": sum(count for status, count in statuses.items() if status.startswith("censored_")),
            "statuses": dict(sorted(statuses.items()))}


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", type=Path, default=Path(__file__).resolve().parents[2])
    parser.add_argument("--output", type=Path, default=Path("Library/BalanceValidation/economy-report.json"))
    parser.add_argument("--seeds", type=int, default=20, help="number of deterministic seeds (starting at --seed-start)")
    parser.add_argument("--seed-start", type=int, default=1)
    parser.add_argument("--end-stage", type=int, default=1000)
    parser.add_argument("--max-attempts-per-stage", type=int, default=100)
    parser.add_argument("--max-runs-per-seed", type=int, default=20000)
    parser.add_argument("--dry-run", action="store_true", help="validate snapshot/configuration without simulating")
    args = parser.parse_args()
    if args.seeds < 1 or args.end_stage < 1 or args.max_attempts_per_stage < 1 or args.max_runs_per_seed < 1:
        parser.error("seed count, end stage, and caps must be positive")

    root = args.root.resolve()
    snapshot = load_snapshot(root)
    balance = _balance(snapshot)
    report: dict[str, Any] = {"kind": "economy_validation", "read_only": True,
        "root": str(root), "source_hashes": snapshot.get("source_hashes", {}),
        "limits": {"seeds": args.seeds, "seed_start": args.seed_start, "end_stage": args.end_stage,
                   "max_attempts_per_stage": args.max_attempts_per_stage, "max_runs_per_seed": args.max_runs_per_seed},
        "authored": {"initial_gold": snapshot["initial_gold"], "reserve_capacity": snapshot["reserve_capacity"],
                     "balance": balance},
        "notes": ["actual uses authored initial_gold; what_if_start_gold_60 is a diagnostic only.",
                  "No reserve upgrade, sale, oracle lane selection, or fabricated resources are modeled.",
                  "PlayerSaveData.AddOwnedCat does not enforce reserve capacity; overflow is retained and reported."],
        "policies": list(POLICIES)}
    if args.dry_run:
        report["dry_run"] = {"status": "validated_no_simulation", "stage_count": len(snapshot["stages"]),
                             "fixture_cats": {"1": {"slot": 0, "column": 0, "row": 0, "level": 1}}}
    else:
        report["results"] = {}
        starts = {"actual": int(snapshot["initial_gold"]), "what_if_start_gold_60": summon_cost(Economy(gold=0), balance) * 3}
        for scenario, start_gold in starts.items():
            report["results"][scenario] = {}
            for policy in POLICIES:
                runs = [run_seed(snapshot, policy, seed, start_gold, args.end_stage,
                                 args.max_attempts_per_stage, args.max_runs_per_seed)
                        for seed in range(args.seed_start, args.seed_start + args.seeds)]
                report["results"][scenario][policy] = {"summary": summarize(runs), "per_seed": runs}
    output = args.output if args.output.is_absolute() else root / args.output
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(json.dumps(report, indent=2, sort_keys=True) + "\n", encoding="utf-8")
    print(f"wrote {output}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
