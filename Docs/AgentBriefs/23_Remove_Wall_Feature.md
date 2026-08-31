# Task Brief 23 — Remove the wall feature: blocks only, maps cleaned, prefabs rebaked, meshes gone

Walls (runs of cells welded into one panel body) leave the game entirely: **the JSON loses its `wall` data, every map is cleaned, the build path spawns plain blocks only, the baked map prefabs are rebaked without welded meshes, and the `*_Meshes.asset` files are deleted** (~122 MB out of the repo — they existed solely to persist the welded wall meshes, which no longer exist).

Branch **`Feature/RemoveWalls`** from `main`, one-line commits, no body, one commit per numbered step so the big diff stays reviewable.

**Coordinate first, delete second**: this erases your teammate's armored/bare-wall feature (`wall.bare`, `BreakableWall.IsArmored`) from `Falcon/UpdateMapData`, and rewrites every map JSON they author. Their branch is merged, but confirm nothing else of theirs is in flight against walls before running this — the brief's job is the removal, not the conversation.

Repository: `/Volumes/Supercent/FierceTigerGameJam/FierceTigerGameJam` — paths relative to `Assets/GameJam/`.

## Verified blast radius

| Where | Wall involvement | Fate |
|---|---|---|
| `Scripts/Gameplay/Wall/BreakableWall.cs` | the wall body: cells, `BreakUp`, `IsArmored`, `MaterialId` | **deleted** |
| `Scripts/Gameplay/Wall/KnockdownLayoutMapAuthoring.cs` (~195 wall-touching lines) | grouping modes (`WallGroupingMode`, `minimumWallCells`, `useWallPanels`, `wallTextureTilesPerCell`, `groupBlocksIntoWalls`, migration flags), `BuildPanelMesh`/`CombineMeshes` welding, panel spawning | grouping + welding removed; every cell spawns its block prefab; `BuildFromPrefab` stays |
| `Scripts/Gameplay/Wall/KnockdownMapDefinition.cs` | `wall` block field, `WallArmored`, `bare` | fields and doc comments removed; parser ignores unknown JSON keys, so old files would still load — clean them anyway (§2) |
| `Scripts/Gameplay/Cannon/GridKnockdownCannonProjectile.cs` | `ResolveDamage(materialId, isWall …)`, `wall != null && wall.IsArmored` | wall lookup gone; `ResolveDamage` loses its wall flag and always reads `blockDamage` |
| `Scripts/Gameplay/Flow/LevelProgressTracker.cs` | a wall counts as `CellCount` blocks | counts plain blocks only — clear-percent math must stay consistent before/after for block-only maps |
| `Scripts/Gameplay/Playfield/FallBreakZone.cs` | `BreakableWall` branch in `Affect`/`Despawn` | branch removed |
| `Editor/MapPrefabBaker.cs` | `PersistRuntimeMeshes` (existed only for welded meshes) | removed; the bake becomes a plain saved hierarchy |
| `Scripts/Gameplay/Wall/BlockDatabase.cs` + `Config/BlockDatabase.asset` | `wallPanel` per entry, `TryGetWallPanel` | field, lookup and asset rows removed |
| `Prefabs/Blocks/*/{brick,concrete,glass}_wall_Panel.prefab` | panel art | deleted once nothing references them |
| `Maps/*.json` (14 files carry `"wall"` — 12 to 344 occurrences each) | `wall` objects on blocks | stripped (§2) |
| `Prefabs/Maps/*_Meshes.asset` (+ their `.meta`) | persisted welded meshes | **deleted**; prefabs rebaked (§3) |
| `Scripts/Gameplay/Combat/BulletDefinition.cs` `MaterialDamage.wallDamage` (+ authored values in `Config/Bullets/*`) | damage split by surface | field **kept, deprecated** (tooltip: "unused since walls were removed; kept so authored configs do not churn") — nothing reads it |

Untouched: the `GameJam.Gameplay.Wall` namespace and folder name (renaming ~15 files buys nothing), `WallBlockPhysicsSetup` (despite the name it is the per-block physics setup — verify it has no wall-only branches and leave it), the JSON spec docs in `Docs/` (a one-line note in the run notes for whoever owns them).

## 1. Runtime removal

Work top-down so each commit compiles: projectile → tracker → fall zone → authoring → `BreakableWall` deleted → `BlockDatabase` field. In the authoring, the block-spawn loop is what remains of `BuildMap`'s layout pass; delete the dead serialized fields outright (their stale values in scenes/prefabs deserialise away silently).

## 2. Map JSON cleanup

New menu item `Tools > Smashdown > Strip Wall Data From Maps`: for every `Maps/*.json`, parse, remove the `wall` member from every block, write back with the file's existing formatting conventions (2-space indent, same key order — the diffs should show only deletions). Runs before the rebake and is idempotent. All 14 affected files are committed in one step.

## 3. Rebake and delete the meshes

1. Run the existing bake (`MapPrefabBaker`, now mesh-free) for every map in `MapConfig` — the prefabs become plain block hierarchies referencing the shared block prefabs, a few hundred KB instead of MB.
2. Delete every `Prefabs/Maps/*_Meshes.asset`; verify no prefab or scene still references their guids (grep) before the delete lands.
3. `Map_2.prefab` + `Map_2_Meshes.asset`: bake ONLY if the `id: 2 / Lv8_Test03` `MapConfig` entry is still wanted — otherwise delete the pair and flag the entry again; do not decide silently either way (ask in the run notes, keep the entry working meanwhile).

## 4. Acceptance

1. Every map in `MapConfig` builds from JSON and from its rebaked prefab with **zero** `BreakableWall` components anywhere; visual layout identical to before at block level (same blocks, same positions).
2. `Prefabs/Maps` contains only `.prefab` files; repo sheds the mesh assets; a clean clone imports without missing-reference warnings.
3. Damage: every hit uses `blockDamage`; a shot that used to chip an armored wall now treats those cells as loose blocks (expected design change — verify the required-clear tuning still lets each map pass, and note any map that got dramatically easier/harder for the balance pass).
4. Clear percent: full-clearing any map reads 100 %, the pass threshold triggers at the same visual point as a block-only count implies; the cleared/fail flow, floor shatter, tutorial and drag rotation all behave as before.
5. **Performance gate** (walls were the draw-call/rigidbody optimisation): profile `map_009_hollow_concrete_tower` and `map_007` on the Android device — frame time during a full collapse compared against pre-change. Kinematic-until-activated keeps physics acceptable; if draw calls regress badly, note it — the mitigation (instancing/batching for dormant blocks) is a follow-up brief, not scope creep here.
6. Compile with zero references to `BreakableWall`, `TryGetWallPanel`, `WallGroupingMode`; builder re-runs (map strip, bake) are no-ops the second time.

## Out of scope

Renaming the `Wall` folder/namespace, removing `wallDamage` from configs, dormant-block rendering optimisations, the JSON spec document rewrite, and any balance retune beyond noting what changed.
