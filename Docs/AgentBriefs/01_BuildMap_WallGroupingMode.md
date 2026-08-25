# Task Brief 01 — BuildMap: merge walls only for blocks with a `wall` property

## Goal

Change `KnockdownLayoutMapAuthoring.BuildMap()` so that blocks are merged into a wall **only when the map JSON explicitly assigns them a wall** (`"wall": { "wall_id": "..." }`). Blocks without a wall id are always built as single blocks. The existing automatic rectangle detection stays available as an opt-in mode for older maps, but is no longer the default.

## Repository

- Unity project: `/Volumes/Supercent/FierceTigerGameJam/FierceTigerGameJam` (Unity 6000.3.15f1, URP, Android target)
- Runtime scripts: `Assets/GameJam/Scripts/Gameplay/Wall/`
- Editor scripts: `Assets/Editor/Scripts/`
- Map JSON files: `Assets/GameJam/Maps/*.json`

## Files to touch

| File | Change |
|---|---|
| `Assets/GameJam/Scripts/Gameplay/Wall/KnockdownLayoutMapAuthoring.cs` | Replace `groupBlocksIntoWalls` bool with a `WallGroupingMode` enum; gate `CollectDetectedWalls` on it |
| `Assets/Editor/Scripts/KnockdownLayoutMapAuthoringEditor.cs` | Draw the new enum; make the map summary / layer preview reflect the selected mode |
| `Assets/GameJam/Scripts/Gameplay/Wall/KnockdownMapDefinition.cs` | No format change required; only doc comment on `KnockdownMapBlock.wall` |
| (optional) `Assets/GameJam/Scripts/Gameplay/Wall/WallGrouping.cs` | Unchanged — still used by `NamedAndDetected` mode |

Do **not** modify the mesh-building code (`TryBuildWall`, `BuildPanelMesh`, `BuildWeldedMesh`), `BreakableWall`, `LevelProgressTracker`, or the JSON schema version.

## Current behaviour (read before changing)

`BuildMap()` → `TryPlaceBlock()` per block → `BuildPlacedBlocks(placed, generatedRoot)`:

1. If `groupBlocksIntoWalls == false` → every block is spawned loose, return 0.
2. `CollectNamedWalls(placed, walls, loose, unassigned)` — groups by `PlacedBlock.WallId` (which comes from `KnockdownMapBlock.WallId => string.IsNullOrEmpty(wall?.wall_id) ? null : wall.wall_id`). A named wall with fewer than 2 members is demoted to loose blocks with a warning. Blocks with no wall id go to `unassigned`.
3. `CollectDetectedWalls(unassigned, walls, loose)` — **this is the behaviour to gate.** It groups single-cell blocks by `(level, type)`, runs `WallGrouping.Find` (greedy maximal rectangles) and keeps rectangles with `Area >= minimumWallCells` (default 3).
4. `TryBuildWall` for every wall; failures fall back to loose blocks; loose blocks are spawned with `SpawnPlacedBlock`.

### Important JSON detail

Maps are parsed with `JsonUtility`. For a `[Serializable]` class field such as `KnockdownMapWallRef wall`, Unity **always creates an instance**, even when the `"wall"` key is absent from the JSON. Therefore "the block has a wall property" cannot be detected by `wall != null`. The only reliable signal is **`wall.wall_id` being non-empty**, which is exactly what `KnockdownMapBlock.WallId` already returns. Use `WallId != null` as the rule; do not add null checks on `wall`.

## Required changes

### 1. `KnockdownLayoutMapAuthoring.cs`

Add the enum and field (keep the old bool serialized but hidden so existing scenes/prefabs migrate cleanly):

```csharp
public enum WallGroupingMode
{
    /// <summary>Never merge. Every block is its own body.</summary>
    None,

    /// <summary>
    /// Merge only blocks that name a wall in the JSON ("wall": { "wall_id": ... }).
    /// Blocks without a wall id are single blocks. Default.
    /// </summary>
    NamedOnly,

    /// <summary>
    /// NamedOnly, plus the legacy fallback: same-type single-cell blocks in one layer are
    /// grouped into rectangles automatically when no wall id was given.
    /// </summary>
    NamedAndDetected,
}

[Header("Wall Grouping")]
[Tooltip("NamedOnly: a block is merged into a wall only when the map gives it a wall_id. "
       + "NamedAndDetected adds the old automatic rectangle grouping for blocks without one.")]
[SerializeField] private WallGroupingMode wallGrouping = WallGroupingMode.NamedOnly;

[Tooltip("Smallest automatically detected run worth merging. Only used by NamedAndDetected.")]
[SerializeField] private int minimumWallCells = 3;

// Legacy field kept for migration. Read once in OnValidate and never written again.
[SerializeField, HideInInspector] private bool groupBlocksIntoWalls = true;
[SerializeField, HideInInspector] private bool wallGroupingMigrated;
```

Migration (in `OnValidate()` — the class currently has none, add it):

```csharp
private void OnValidate()
{
    if (!wallGroupingMigrated)
    {
        // Scenes saved before the enum existed: a disabled bool meant "never group".
        // An enabled bool is mapped to NamedOnly, which is the new intended default.
        wallGrouping = groupBlocksIntoWalls ? WallGroupingMode.NamedOnly : WallGroupingMode.None;
        wallGroupingMigrated = true;
    }

    minimumWallCells = Mathf.Max(2, minimumWallCells);
}
```

Rewrite `BuildPlacedBlocks`:

```csharp
private int BuildPlacedBlocks(List<PlacedBlock> placed, Transform generatedRoot)
{
    if (wallGrouping == WallGroupingMode.None)
    {
        for (int i = 0; i < placed.Count; i++)
        {
            SpawnPlacedBlock(placed[i], generatedRoot);
        }

        return 0;
    }

    List<WallBuild> walls = new List<WallBuild>();
    List<PlacedBlock> loose = new List<PlacedBlock>();
    List<PlacedBlock> unassigned = new List<PlacedBlock>();

    CollectNamedWalls(placed, walls, loose, unassigned);

    if (wallGrouping == WallGroupingMode.NamedAndDetected)
    {
        CollectDetectedWalls(unassigned, walls, loose);
    }
    else
    {
        // NamedOnly: a block the map did not put in a wall is a block.
        loose.AddRange(unassigned);
    }

    for (int i = 0; i < walls.Count; i++)
    {
        if (!TryBuildWall(walls[i], generatedRoot))
        {
            loose.AddRange(walls[i].Blocks);
            walls.RemoveAt(i--);
        }
    }

    for (int i = 0; i < loose.Count; i++)
    {
        SpawnPlacedBlock(loose[i], generatedRoot);
    }

    return walls.Count;
}
```

Expose the mode for the editor: `public WallGroupingMode WallGrouping => wallGrouping;` and `public int MinimumWallCells => minimumWallCells;`.

Extend the build log in `BuildMap()` to say how many walls used a panel mesh vs a welded mesh (return that from `TryBuildWall` via an `out bool usedPanel` or a small counter) — this is what tells you whether a converter-exported wall is actually cheap.

### 2. Named-wall connectivity warning (small, recommended)

`CollectNamedWalls` accepts any set of blocks sharing an id, even if they are not adjacent. Add a check in `CreateWallBuild` or right after it: flood-fill the members over 6-neighbour adjacency on `GridPosition`/`Footprint`. If more than one connected component is found, log a warning naming the wall id and **split it into one `WallBuild` per component** (suffix `_partN`). A single `BoxCollider` around two disconnected clusters is a gameplay bug, not just a cosmetic one.

### 3. `KnockdownLayoutMapAuthoringEditor.cs`

The inspector currently reads `groupBlocksIntoWalls` (bool) and `minimumWallCells` at lines ~32–53 and passes them into `DrawMapSummary`, `DrawLayerPreview`, `CountGrouping`, `ResolveWalls`.

- Replace with `SerializedProperty mode = serializedObject.FindProperty("wallGrouping")` and draw it with `PropertyField`.
- Draw `minimumWallCells` only when `mode.enumValueIndex == (int)WallGroupingMode.NamedAndDetected`.
- Thread the enum (not a bool) into `DrawMapSummary` / `DrawLayerPreview` / `ResolveWalls` / `CountGrouping`:
  - `None` → no wall colouring, wall count 0.
  - `NamedOnly` → colour cells by `wall_id` only; skip the `WallGrouping.Find` branch entirely.
  - `NamedAndDetected` → current behaviour.
- The preview must match the runtime result exactly; a preview that shows a wall the runtime does not build is a bug.

### 4. Doc comment on `KnockdownMapBlock.wall`

Update the XML comment to state the new contract: *"Left out, the block is built as a single block (unless the authoring component is set to NamedAndDetected)."*

## Converter dependency (verify, do not change here)

Because merging is now decided entirely by the JSON, the exporter in the Smash Builder web tool (`FierceTigerGameJam/Tools/BuildingConverterWeb`) must write `"wall": { "wall_id": "<unique id per merged panel>" }` for every block that belongs to a merged brick/concrete/glass wall. If it does not, switching the default to `NamedOnly` will turn every converted wall into single blocks and block count will jump. Check one exported map before merging this task; if the exporter lacks the field, open a separate task for it.

## Acceptance criteria

1. Building `Assets/GameJam/Maps/map_003_wall_groups.json` in `NamedOnly` mode produces walls only for the `wall_id` groups present in the file (`front_concrete`, `front_brick`, `deep_pillar`, plus any others in level 1); the glass row at `y = 2` (no `wall_id`) is built as 4 separate `glass_1x1` blocks.
2. Building `map_001.json` / `test_03.json` (no `wall_id` anywhere) in `NamedOnly` mode produces **0 walls**; in `NamedAndDetected` mode it produces the same walls as before the change.
3. `WallGroupingMode.None` spawns every placed block loose.
4. `LevelProgressTracker.TotalBlocks` equals the number of placed blocks in every mode (walls report `CellCount`), so clear percentage is unchanged by the mode.
5. Inspector preview and summary counts agree with the runtime build log for all three modes.
6. A named wall whose blocks are not connected is split with a warning, not built as one body.
7. No change to `_Shattered` prefabs, `BreakableWall`, `BlockDatabase`, or the JSON `schemaVersion`.
8. Project compiles with no new warnings; existing scene `Assets/GameJam/Scene/Gameplay.unity` opens with the authoring component migrated to `NamedOnly` (or `None` if the bool was off).

## Out of scope

- Steel material, nudge-without-damage, any physics tuning.
- Runtime performance work on `BuildMap` (see Task Brief 03).
- Map prefab baking (see Task Brief 04).
