# Note 26 — Falcon content drop: the mission 2 and 3 maps land on the new architecture

Not a task brief. This is a state note for whoever (human or agent) reads this branch next: what the
`Falcon/*` line added on top of `main` @ `24abd632`, what it deliberately did NOT touch, and what is
still open. Written 2026-09-01 by Falcon's side (Dong Duong + agent).

## What this branch adds — content and config only, ZERO code

| Change | Detail |
|---|---|
| 18 map JSONs | `Maps/mission2_map1..9.json` (beach set), `Maps/mission3_map1..9.json` (space set) |
| 8 textures | `Textures/`: BG_Beach_01, BG_New_01, BG_New_09, BG_Universe_01, Floor_New_01, Floor_New_09, Floor_Sand_01, Floor_Space_01 — background/floor art for missions 1–3 |
| `Config/MissionConfig.asset` | mission_2 filled to nine ids; mission_3 added with nine ids |
| `Config/MapConfig.asset` | 27 entries total: +15 new; the three `Dev_` placeholders (`mission2_map1..3`) re-pointed — displayName renamed, **content of their JSONs overwritten in place** (guids kept), `mapPrefab` cleared to `{fileID: 0}` because the old bakes no longer match the content |

**Not one `.cs`, scene, prefab or setting file is modified.** Everything from briefs 21–25 — audio pass,
camera orbit, barrel-only cannon, wall removal, MissionConfig unlock rule — is exactly as `main` has it.

## Where the maps come from, and what was done to them

Authored on `Falcon/UpdateMapData` against the pre-brief-23 schema, so every block carried `wall`
grouping data. Brief 23 removed walls from the runtime entirely, so before landing here each JSON was:

1. stripped of every `wall` object (≈3,700 entries across the set — same effect as the strip menu item),
2. renamed, file and internal `id` both, to the brief-25 scheme:

| New id | Was (Falcon name) | New id | Was |
|---|---|---|---|
| mission2_map1 | map_m2_001_beach_hut_row | mission3_map1 | map_m3_001_relay_dish |
| mission2_map2 | map_m2_002_lifeguard_tower | mission3_map2 | map_m3_002_launch_gantry |
| mission2_map3 | map_m2_003_the_pier | mission3_map3 | map_m3_003_solar_farm |
| mission2_map4 | map_m2_004_beach_bar | mission3_map4 | map_m3_004_fuel_tanks |
| mission2_map5 | map_m2_005_seaview_restaurant | mission3_map5 | map_m3_005_control_tower |
| mission2_map6 | map_m2_006_waterslide_tower | mission3_map6 | map_m3_006_biodome |
| mission2_map7 | map_m2_007_lighthouse | mission3_map7 | map_m3_007_landed_shuttle |
| mission2_map8 | map_m2_008_sea_fort | mission3_map8 | map_m3_008_docking_arm |
| mission2_map9 | map_m2_009_boardwalk_resort | mission3_map9 | map_m3_009_spaceport |

The pre-strip originals (walls intact) are kept outside the repo in `_port_pack/maps_goc_con_wall/` on
Falcon's machine, and in the `Falcon/UpdateMapData` branch history.

## Open items — the order matters

1. **No prefabs are baked for the 18 maps.** Every `mapPrefab` is `{fileID: 0}`, so they load through the
   JSON path until someone runs the baker. Do not resurrect the old Falcon bakes: they predate brief 23
   and contain BreakableWall components that no longer exist.
2. **`MapProgressionConfig.asset` only covers 13 ids.** `mission2_map4..9` and all of `mission3_*` have
   no entry, so they run on `ResolveMapRules` fallbacks (clear 0.8, pick limit 10). Entries wanted —
   suggested rewards continue the pass_map/clear_map series.
3. **Balance is unverified post-walls.** These maps were tuned when a wall was one body with pooled HP.
   As loose per-cell blocks they topple much more easily; every map needs a play pass before the 0.8
   requirement is trusted. Damage table note that still holds: concrete is immune to Rock — a mission 3
   map with concrete cores is uncompletable on Rock-only loadouts.
4. **Per-mission scenery is not built.** The textures above are shipped ahead of it. Falcon's old
   implementation (per-mission background/floor/tiling fields + a LevelScenery playfield dresser +
   editor preview) targeted the retired `MissionPanelView.missions[]` and was left behind on purpose;
   whoever builds it next should hang the three fields off `Mission` in `MissionConfig` instead.
   Reference implementation: `Assets/GameJam/Scripts/Gameplay/Playfield/LevelScenery.cs` and
   `Assets/GameJam/Editor/MissionEditorWindow.cs` on branch `Falcon/UpdateMapData`.
5. **Background art safe frame** (measured pre-orbit; re-measure if the rig moved): vertical FOV 22.5°,
   backdrop plane z 19.43 → of a 20 × 16.7 sprite only 34–66% of width and 28–95% of height (from top)
   is ever on screen. Compose subjects at 26–40% and 60–74% of width.

---

## Running log — updated as Falcon works on this branch (read this before assuming the state above)

- **2026-09-01a** Content drop landed as described above. Committed nothing yet; all working-tree.
- **2026-09-01b** Fixed a self-inflicted YAML fault in `Config/MissionConfig.asset`: the first insert
  mis-indented `- id: mission_2`, which Unity read as a TENTH mapId of mission_1 (the board showed a
  broken "LEVEL 10" card) and mangled mission_2 (its board showed mission_1's maps). Rewritten clean:
  3 missions x 9 ids, verified 27 total. If you saw those two symptoms in a build from this branch,
  they are this, not the board code.
- **2026-09-01c** Observation on brief 24's ground decision: "Ground stays put" assumes the floor
  reads as symmetric, but the current floor texture has strong directional streaking, so an orbit
  visibly spins the floor against the camera-locked backdrop. Not a code bug. Two candidate fixes on
  the table: (A) hang Ground under OrbitPivot like the backdrop, or (B) keep the design and switch to
  rotation-neutral floor textures (the shipped Floor_Sand_01 / Floor_Space_01 qualify) once
  per-mission scenery lands. Leaning B; not decided yet.
- **2026-09-01d** Decision on the above: **B**. Ground stays world-static exactly as brief 24 wrote it.
  The floor-spin read is answered by per-mission floor art, which now exists:
  - `Mission` (MissionConfig.cs) gained three authored fields: `background` (Sprite),
    `floorTexture` (Texture2D), `floorTiling` (0 = 1). Plus lookups `MissionIndexOf(mapId)` and
    `TryGetScenery(mapId, ...)`. No behaviour of the existing unlock/board paths touched.
  - New `Scripts/Gameplay/Playfield/LevelScenery.cs` (~190 lines, runtime-only): watches
    `MapSelection.Selected`, swaps the four Backdrop sprites (width-fit against the authored sprite
    so any pixel size covers the same world area) and puts the floor on Ground through a
    MaterialPropertyBlock (`_BaseMap`/`_MainTex` + `_BaseMap_ST`) - the ground material asset is
    never touched. Falls back to scene defaults for a mission with no scenery.
  - `Scene/Gameplay.unity`: one new root GameObject `Level Scenery` wired to MapSelection,
    MissionConfig, the four backdrop SpriteRenderers and Ground. Nothing else in the scene moved.
  - `MissionConfig.asset`: mission_1 = BG_New_01 + Floor_New_09 (tiling 0.6), mission_2 =
    BG_Beach_01 + Floor_Sand_01 (2.5), mission_3 = BG_Universe_01 + Floor_Space_01 (20).
    Tilings are first guesses scaled for the 100x100 ground; tune by eye in the asset.
- **2026-09-01e** Refinement of B, Falcon's idea: the floor VISUAL now rides the orbit while the
  floor PHYSICS stays put. Concretely: new `GroundVisual` (plane, no collider) parented under
  `OrbitPivot` at Ground's world pose; the original `Ground` keeps its MeshCollider but its
  MeshRenderer is disabled; `Level Scenery` paints the mission floor onto GroundVisual. Net cost is
  zero - still one floor drawn, one transform carried by the pivot. Trade-off accepted: debris
  resting on the (invisible, static) physics floor appears to skate against the floor picture while
  the player is actually dragging the orbit, and only then.



---

## TL;DR tiếng Việt

Nhánh này = `main` mới nguyên vẹn (không sửa dòng code nào) + 18 map Falcon (đã gỡ wall, đổi tên theo
chuẩn mission) + 8 texture + MissionConfig/MapConfig nối đủ 3 mission. Còn nợ: bake prefab, thêm entry
MapProgressionConfig cho 15 map mới, chơi lại cân bằng vì hết wall, và viết hệ BG/floor per-mission
gắn vào MissionConfig (code tham khảo ở nhánh Falcon/UpdateMapData).
