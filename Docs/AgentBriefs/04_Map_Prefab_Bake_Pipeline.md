# Task Brief 04 — Map prefab bake pipeline (JSON stays the source of truth)

## Goal

Add an editor bake step that turns each map JSON into a ready-to-instantiate prefab, and make the runtime prefer the baked prefab over building from JSON. This removes per-block validation, per-block `Instantiate`, wall mesh combining, and hierarchy walks from the run-start path, and makes the load async-capable. The JSON build path stays intact as the fallback and as the designer iteration loop.

Start this brief **after** Brief 03's P0 items have been measured. If the measured build spike is already acceptable on the target device, this brief can be deferred.

## Repository

`/Volumes/Supercent/FierceTigerGameJam/FierceTigerGameJam`

Relevant code:

- `Assets/GameJam/Scripts/Gameplay/Wall/KnockdownLayoutMapAuthoring.cs` — `BuildMap()`, `ClearMap()`, `SubscribeWalls`, `SetupStructureCenter`, `PlacedBlockCount`, `InstantiateBlock` (already uses `PrefabUtility.InstantiatePrefab` when `!Application.isPlaying`)
- `Assets/GameJam/Scripts/Gameplay/Wall/MapSelection.cs`, `MapConfig.cs`, `MapSelector.cs`, `MapListView.cs` — `MapInfo` (has `Id`, `MapJson`)
- `Assets/GameJam/Scripts/Gameplay/Flow/GameFlowController.cs` — `ConfirmAmmoPick()` calls `mapBuilder.BuildMap()` then `runController.BeginRun()`
- `Assets/GameJam/Scripts/Gameplay/Flow/LevelProgressTracker.cs` — `BeginRun()` reads `mapBuilder.PlacedBlockCount`
- `Assets/GameJam/Editor/BlockPrefabBuilder.cs` — `SaveChunkMeshes` / `TryOverwriteChunkMeshes` show how meshes are stored as sub-assets of one asset file; reuse the pattern
- `Assets/Editor/Scripts/KnockdownLayoutMapAuthoringEditor.cs` — inspector with *Build Map* / *Clear Map* buttons

## Why the cost is not the JSON

`JsonUtility.FromJson` on a 500-block map is a few milliseconds. What costs is everything `BuildMap` does afterwards, per block: validation and cell reservation, `Instantiate`, `AddComponent` ×3 in `WallBlockPhysicsSetup` (until Brief 03 P0-2 lands), and per wall `Mesh.CombineMeshes` / `BuildPanelMesh`, plus three `GetComponentsInChildren` walks. A baked prefab does all of that once in the editor; the runtime does a single `Instantiate` (or `InstantiateAsync`) of the root.

| Step | JSON at runtime | Baked prefab |
|---|---|---|
| Validate + reserve cells | per block | 0 |
| `Instantiate` | N calls | 1 call |
| `AddComponent` physics | 3N (0 after Brief 03 P0-2) | 0 |
| Wall mesh combine | per wall, every build | 0 (mesh is a sub-asset) |
| Hierarchy walks | 3–4 | 0–1 |
| Async load | no | yes (`Resources.LoadAsync` / Addressables + `Object.InstantiateAsync`) |
| Asset size | tens of KB | hundreds of KB to a few MB per map (combined wall meshes) |
| Block tuning changes | picked up automatically | picked up automatically for blocks (nested prefab instances); **baked wall meshes must be re-baked** |
| Designer loop | edit JSON → Play | edit JSON → Bake → Play |

## Design

### Assets

- `Assets/GameJam/Maps/Baked/<mapId>.prefab` — the baked `GeneratedLayoutBlocks` root.
- `Assets/GameJam/Maps/Baked/<mapId>_WallMeshes.asset` — all wall meshes for that map as sub-assets of one asset (pattern: `BlockPrefabBuilder.SaveChunkMeshes`). Prefab `MeshFilter`s reference these sub-assets, never runtime-created meshes.
- `MapInfo` gains a `GameObject bakedPrefab` field (direct reference for now; leave a comment where an `AssetReference` would go if Addressables are adopted).

### Runtime component on the baked root

```csharp
public sealed class BakedMapInfo : MonoBehaviour
{
    [SerializeField] private string mapId;
    [SerializeField] private int placedBlockCount;      // what LevelProgressTracker needs
    [SerializeField] private Vector3 structureCenterLocal; // what SetupStructureCenter needs
    [SerializeField] private string sourceJsonHash;     // to detect a stale bake
    [SerializeField] private string blockDatabaseHash;  // idem
    // read-only properties
}
```

### Bake (editor)

`Tools > Smashdown > Bake Map Prefabs` (and a `Bake This Map` button in `KnockdownLayoutMapAuthoringEditor`):

1. For each `MapInfo` in the `MapConfig`/`MapSelection` list (or the one selected): temporarily point the scene's `KnockdownLayoutMapAuthoring` at that map, call `BuildMap()` in edit mode. `InstantiateBlock` already uses `PrefabUtility.InstantiatePrefab`, so blocks inside the bake stay **nested prefab instances** and keep following the block prefabs.
2. Collect every wall `MeshFilter.sharedMesh` created by the build (the `builtMeshes` list from Brief 03 P0-3) and save them into `<mapId>_WallMeshes.asset`; repoint the `MeshFilter`s at the saved sub-assets.
3. Add `BakedMapInfo` to the root with `placedBlockCount = PlacedBlockCount`, `structureCenterLocal` from `ResolveStructureCenterLocalPosition`, hashes of the JSON text and of the `BlockDatabase` asset (GUID + file hash is enough).
4. `PrefabUtility.SaveAsPrefabAsset(root, path)`, then `ClearMap()` and restore the authoring component's previous map.
5. Assign the prefab to `MapInfo.bakedPrefab`, mark the config asset dirty, save.
6. Log a per-map summary: blocks, walls (panel vs welded), mesh count, asset sizes.

Guardrails:

- Refuse to bake if `BuildMap` logged any error for that map (block outside grid, unknown type, overlapping cell) — a bake must not silently drop blocks.
- Bake uses the authoring component's current `WallGroupingMode` (Brief 01); the mode is recorded in `BakedMapInfo` so a stale-mode bake is detectable.
- `IPreprocessBuildWithReport`: before an Android build, re-bake any map whose stored hashes do not match the current JSON / BlockDatabase, or fail the build with a clear message (choose fail-fast; auto-bake in a build hook can hide mistakes).

### Runtime

`KnockdownLayoutMapAuthoring.BuildMap()`:

```
if (selected MapInfo has bakedPrefab && !forceJsonBuild)
    → InstantiateBaked()
else
    → existing JSON build
```

`InstantiateBaked()`:

1. `ClearMap()` as today.
2. `Instantiate(bakedPrefab, parent)`; rename to `GeneratedBlocksRootName` so `LevelProgressTracker.ResolveGeneratedRoot` and `ClearMap` keep working unchanged.
3. `PlacedBlockCount = bakedInfo.placedBlockCount`.
4. `physicsSetup.PrepareBlocks(generatedRoot)` still runs (after Brief 03 P0-2 it only applies authoring values, cheap) — needed because `KnockdownBlock` reads `KnockdownBlockAuthoring` at runtime.
5. `SubscribeWalls(generatedRoot)` unchanged; `BreakableWall.Initialize` data is serialized in the prefab (the `cells` list is private and populated at build time — **make sure it is serialized**: today `cells` is a `private readonly List<Cell>` with no `[SerializeField]`, and `Cell` holds a `GameObject Prefab` reference. Mark `Cell` `[Serializable]`, the list `[SerializeField]`, and `physicsSetup` must be re-injected at runtime since scene references cannot be saved into a prefab asset — add `BreakableWall.SetPhysicsSetup(WallBlockPhysicsSetup)` called from `SubscribeWalls`).
6. `SetupStructureCenter` uses `bakedInfo.structureCenterLocal` instead of the renderer walk.
7. In development builds, compare the stored hashes with the live JSON/BlockDatabase and log a warning if stale.

Async variant: `BuildMapAsync(Action onComplete)` using `Object.InstantiateAsync(bakedPrefab, parent)` (Unity 6 API); `GameFlowController.ConfirmAmmoPick` awaits it before `runController.BeginRun()`. Start the load when the ammo-pick screen opens (`EnterAmmoPick`) and keep the result inactive until Start Run so the load is hidden behind UI.

`forceJsonBuild` is a serialized bool on the authoring component (default false) plus an editor toggle, so designers can iterate on JSON without re-baking.

## Constraints

- Do not change the JSON schema or `KnockdownMapDefinition`.
- Everything under `Assets/GameJam/Maps/Baked/` is generated; add a `.gitattributes` LFS rule if mesh assets exceed a few MB, and a `README.md` in that folder saying "generated by Tools > Smashdown > Bake Map Prefabs — do not edit".
- The JSON build path must keep producing an identical structure to the baked one (same block names, `GridPosition`, `LogicalSize`, wall manifests). Add an editor test that builds a map both ways and compares child names + `KnockdownBlockAuthoring` values.
- Editor-only code under `#if UNITY_EDITOR` / `Assets/GameJam/Editor`.

## Acceptance criteria

1. `Tools > Smashdown > Bake Map Prefabs` bakes every map in the config; re-running with no changes rewrites nothing (hash check) and reports "up to date".
2. Baked prefab for `test_03.json` loads at run start with **one** `Instantiate` call, no `CombineMeshes`, no runtime-created `Mesh` objects (Memory Profiler: mesh count stable across five retries).
3. `LevelProgressTracker.TotalBlocks` and the clear percentage are identical between JSON build and baked build of the same map.
4. Walls in a baked map still break up correctly (`BreakableWall.BreakUp` spawns the right blocks at the right poses) — proves the manifest serialization.
5. Changing a value in `brick_1x1.prefab` (e.g. mass) is reflected in a baked map without re-baking; changing `Brick_Wall.fbx` or the wall panel material requires a re-bake and the stale-hash warning fires.
6. Android build with a stale bake fails with a message naming the map.
7. Build-spike capture (Brief 03 Step 0 method) for the baked path is recorded in `ProfilerCaptures/README.md` next to the JSON-path numbers.

## Out of scope

- Addressables adoption (leave the hook).
- Wall grouping rules (Brief 01), debris (Brief 02), runtime hotspots (Brief 03).
