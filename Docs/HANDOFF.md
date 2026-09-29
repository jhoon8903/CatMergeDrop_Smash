# Cat Merge Drop — Handoff

## Goal and current state

Unity 6.3 / URP 2D mobile merge-and-drop game. The project has a 4×3 merge board, world-space SpriteRenderer tile blocks, a goal line, and ECS drop state. The board's cat GameObject is moved during Drop and restored afterward. The user requires gameplay objects to use SpriteRenderer and world transforms; Canvas is UI-only, and gameplay objects should be authored assets rather than runtime-created GameObjects.

Current focus: generalize `ViewAuthoring` so cat and brick world views share typed rendering/lifecycle behavior and bake into Entities. Cat and brick Bakers and presenter queries have been added, but the active GameScene is still a regular scene with no SubScene. Therefore actual scene baking and runtime binding through those baked entities are **not verified**. The presenter also currently validates serialized brick/cat arrays before it attempts to bind baked views, so it cannot yet run with those arrays omitted.

## Completed work and evidence

- The board model supports moving, merging equal levels, and swapping unequal occupied slots. Board data is persisted through the existing Local save setup.
- ECS Drop state, cat fall/collision, tile damage/break events, rewards, failure reset, and the scene presenter were implemented. The existing tile views and cat visuals are reused; the cat is not recreated for Drop.
- Camera framing zooms out for Drop and moves smoothly to the lowest broken tile height. Tiles were tightened to a 1.22 world width by 0.90 height on 1.20 horizontal and 0.88 vertical pitch. Cat bounce height is capped to 0.45 world units above the impacted tile.
- `ViewAuthoring<TState>` now provides a typed render entry point and shared configured/visibility lifecycle. `ViewBaker<T>` creates the dynamic entity and adds the Authoring MonoBehaviour as a companion component. `DropBrickViewBaker` adds row/column data; `DropCatViewBaker` adds slot data. `DropStageController` queries and resolves these baked entities while retaining serialized references as a fallback.
- **Passed:** `dotnet build Assembly-CSharp-Editor.csproj --no-restore -v:q -clp:ErrorsOnly` after the script organization refactor (0 errors, 7 warnings). Unity Editor also reimported and compiled the moved scripts without errors; runtime baking remains unverified.
- **Tests:** a recent Unity EditMode job reported initialization timeout. The resulting `TestResults.xml` showed 21/22 tests passed; the sole failure was an unhandled Apps-in-Toss package manager command log in `BreakEvents_AreRemovedBeforeTheNextSimulationFrame`, not a gameplay assertion. The newest typed View refactor has not been tested in Unity.
- **Earlier manual/app verification:** a prior Play Mode check covered Drop, cats moving from board cells, camera movement, and restoration after failure. The latest authored-view baking path was not part of that check. No SubScene has been added to GameScene.
- The approved simple cat sprite is `Assets/Projects/Art/Build/CatBase/cat_base_idle_v1.png`. The project directory is not a Git repository. Existing user save data is local; preserve it when doing any future live Drop checks.

## Script organization update

- Scripts are grouped under `Board/`, `Drop/`, `Drop/Ecs/`, `Views/`, and `World/`. The former `InPlay` namespace is now `Drop.Ecs`; enum types are in `Drop.Ecs.Enums`. Unity AssetDatabase retained asset GUIDs during the moves; no `.meta` file was edited directly.
- `MergeBoardController`, `DropStageController`, and `DropCameraController` are renamed for their actual coordinating roles. The reward save logic is now in `Drop/DropRewardPersistence.cs`; run state and retry timing remain in the controller.
- `DropStageFactory.cs` and `DamageAndBreakSystem.cs` now match their declared type names.

## Decisions and constraints

- Keep the 4×3 board cat objects as the actual Drop visuals; do not instantiate new cat GameObjects at runtime.
- Gameplay uses world-space SpriteRenderer/TextMesh transforms. Canvas remains limited to UI.
- Use `game-asset-pipeline` when generating or editing game raster assets. Current work is code/scene architecture, not image generation.
- `ViewAuthoring<TState>` gives compile-time typed render data, while the concrete Baker adds each view's stable identifier data. A baked MonoBehaviour companion is intended to bridge ECS presentation back to the authored GameObject.
- Local save mode remains configured. Do not use live Firestore or overwrite user save data during validation.

## Unfinished work and blockers

- Put the authored brick and cat view hierarchies in a SubScene and verify that Entities baking creates the expected 12 brick view entities and 12 cat view entities. Confirm companion references and GameObject transforms work at runtime.
- Refactor `DropStageController.Initialize`/`ValidateReferences` so baked views can be the primary source and serialized arrays are only an optional fallback; currently validation returns early if those arrays are unset.
- Confirm whether brick/cat slot metadata should share one common baked view identity component rather than separate `BrickViewData` and `CatViewData` structs.
- Re-run Unity EditMode tests after the SubScene/view wiring. Investigate the unrelated Apps-in-Toss test log only if it blocks a clean run.

## Next concrete step

Refactor presenter initialization to collect the 12 brick and 12 cat views from baked Entities before requiring serialized view arrays, then author the relevant hierarchy into a SubScene and inspect the baked entity count and companion components in Unity.

## Key paths

- `Assets/Projects/Scripts/GamePlay/Views/ViewAuthoring.cs` — shared typed view base and Baker helper.
- `Assets/Projects/Scripts/GamePlay/Views/DropBrickView.cs`, `DropCatView.cs` — concrete views and Bakers.
- `Assets/Projects/Scripts/GamePlay/Drop/DropStageController.cs` — runtime ECS-to-view binding and rendering.
- `Assets/Projects/Scripts/GamePlay/Drop/Ecs/Components/`, `Assets/Projects/Scripts/GamePlay/Drop/Ecs/Systems/` — ECS state and drop systems.
- `Assets/Projects/Scripts/GamePlay/World/WorldPlayfieldLayout.cs` — world layout, tile spacing, and bounce height.
- `Assets/Projects/Scenes/BootScene/GameScene.unity`, `Assets/Projects/Prefabs/GamePlay/` — authored gameplay scene and prefabs.
- `Assets/Projects/Editor/WorldGameSceneAuthoring.cs` — editor utility that rebuilds the authored GameScene and prefabs.
- `Docs/CatMergeDrop_DevelopmentPlan.md` — broader gameplay plan.

```context-manifest
{
  "schema_version": 1,
  "saved_at": "2026-09-26T14:09:59Z",
  "project_root": ".",
  "handoff_path": "Docs/HANDOFF.md",
  "required_files": [],
  "optional_files": [
    "Assets/Projects/Scripts/GamePlay/Views/ViewAuthoring.cs",
    "Assets/Projects/Scripts/GamePlay/Views/DropBrickView.cs",
    "Assets/Projects/Scripts/GamePlay/Views/DropCatView.cs",
    "Assets/Projects/Scripts/GamePlay/Drop/DropStageController.cs",
    "Assets/Projects/Scripts/GamePlay/Drop/Ecs/Components/Brick.cs",
    "Assets/Projects/Scripts/GamePlay/Drop/Ecs/Components/Cat.cs",
    "Assets/Projects/Scripts/GamePlay/Drop/Ecs/Systems/GateCollisionSystem.cs",
    "Assets/Projects/Scripts/GamePlay/Drop/Ecs/Systems/FallSystem.cs",
    "Assets/Projects/Scripts/GamePlay/World/WorldPlayfieldLayout.cs",
    "Assets/Projects/Scenes/BootScene/GameScene.unity",
    "Assets/Projects/Editor/WorldGameSceneAuthoring.cs",
    "Docs/CatMergeDrop_DevelopmentPlan.md"
  ],
  "validation": {
    "repository_inspected": true,
    "handoff_read": true,
    "tests_run": true,
    "build_run": true,
    "app_run": true,
    "manual_verification": true
  }
}
```
