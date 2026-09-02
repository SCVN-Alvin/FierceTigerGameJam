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
- **2026-09-01f** `MissionPanelView.OnEnable` now opens the board on the FURTHEST UNLOCKED mission
  (new `FurthestUnlockedMission()`, walks `IsMissionUnlocked` forward, stops at the first locked)
  instead of always mission 1. Only the opening index changed - paging, unlock rule and rebuild
  untouched.

  **Why "always mission 1" was actively harmful, not just one extra tap.** `OnEnable` runs on
  every re-show of the board, and the board re-shows after every run. So for a player who is INTO
  mission 2, the sequence was: fail (or pass) mission2_map1 -> flow returns to the board ->
  `missionIndex = 0` -> the board is standing on MISSION 1 - a page of maps they already finished.
  Every single retry of a mission-2 level therefore cost a page-forward tap, and worse, it *reads*
  like a bug: the player just played a beach map, lost, and the game appears to dump them back to
  the city maps as if their progress were gone. A fail on the first level of a fresh mission is
  exactly the moment a player retries most, which made the misdirection most frequent right where
  frustration is already highest. Opening on the frontier makes the post-run board land on the
  mission the player was just in, in every case: mid-mission-1 players still see mission 1
  (frontier = 1), mission-2 players see mission 2 whether they failed or passed.




- **2026-09-01g** Numbers-backed audit of the branch as it stands (economy + verified defects).
  Full detail below in "Audit appendix"; headline: 15 of 27 campaign maps have no
  MapProgressionConfig entry (fallback clear 0.8 / 10 shots, zero reward), the economy earns at
  most 10,050 gold against 14,300 to max the garage, and six known gameplay defects survived the
  rework (fire-through-UI, lateral support cascade, stale pooled bulletOverride, monotonic
  StructureRegistry.highestRow, double ballistic solve on tap, aim-origin Awake race).

- **2026-09-01h** Acted on the audit's top three, in one pass:
  1. **Progression + rewards authored for all 27 maps.** `RewardConfig` +36 entries continuing the
     existing arithmetic series exactly (pass_map_N = 50+50N -> 550..1,400; clear_map_N continues
     +50 -> 800..1,650). `MapProgressionConfig` +15 entries: clear 0.5 like every authored map
     (kills the 0.8-fallback cliff at mission2_map4), bulletPickLimit = round(blocks/7) clamped
     14..40 from each map's REAL block count (e.g. mission3_map1 = 370 blocks -> 40 shots).
     Also re-pointed mission2_map1..3 from mission 1's pass/clear_map_4..6 to their own
     pass/clear_map_10..12 - ends the double-pay. Cross-checked: every reward id referenced
     exists, no id is used by two maps. Campaign now pays 47,100 total vs 14,300 to max the
     garage - deliberately generous, flagged for a balance pass rather than guessed downward.
  2. **Support cascade completes.** `KnockdownBlock.ActivateFromSupportRelease` now calls
     `ReleaseSupportedBlocksAbove()` exactly as `Activate()` does, so a release climbs the whole
     column instead of stopping one storey up. Cycles terminate on the existing isActivated
     guard. This is the fix for "100% clear unreachable on overhang maps".
  3. **Input gated at the UI.** `CannonInputShooter` ignores gestures that BEGIN over raycastable
     UI and re-checks at release (a Cleared/Fail screen can appear while the finger is down).
     One helper (`PointerOverUi`) asks EventSystem for both the mouse pointer and the primary
     touch id, because the no-arg overload only speaks for the mouse under the input-system UI
     module. The full-canvas screen dimmer makes this one check double as a "screen is up" gate.

- **2026-09-01i** Two field reports from playtesting mission 3, both fixed:
  1. **Hovering slabs.** 01h's cascade fix climbed columns, but a slab held up by leg columns is
     WIDE: when a leg breaks, only the slab cell over the leg's footprint was ever walked - its
     sideways neighbours were over nothing, so no release ever reached them and the slab hung in
     mid-air (screenshot: mission 3, 21%, whole hull floating). New rule in `StructureRegistry`:
     `CollectUnsupportedNeighbours` + `HasSupport` - when a block activates, its side-adjacent
     neighbours whose ENTIRE footprint has nothing live beneath (and are not on row 0) are
     released too, and each release repeats the check, so the collapse sweeps down the slab cell
     by cell. Grounded or column-supported neighbours are never touched. Wired into both
     `Activate()` and `ActivateFromSupportRelease()`; cycles terminate on the isActivated guard.
     Cost: one small dictionary scan + a short list per activation, only during collapses.
  2. **Dead tap zone under the percent readout.** The 01h input gate did its job too well: the
     HUD's clear-percent label was a raycast target, so the gate treated the whole label corner
     as UI and swallowed the tap - fire worked at the right corner (nothing raycastable there)
     and not at the left. `RunHudView` now switches `clearPercentLabel.raycastTarget` off at
     Awake; the label is decoration. Rule of thumb recorded: any HUD graphic that is not a
     button must not be a raycast target, or the UI input gate turns it into a dead zone.

- **2026-09-01j** Three requests from Falcon, all landed:
  1. **3D props on the shop tables.** New `UI/ShopModelShowcase.cs`: a runtime-built offscreen rig
     (camera at a 16-degree toy angle, one key light, a generated radial blob shadow, everything
     parked at (1000,-1000,1000)) renders the prop to a 512 RenderTexture shown through a RawImage
     laid over each shop's `previewImage` rect. Ammo tab shows the bullet's own
     `ProjectilePrefab`; vehicle tab shows `ResolveModelPrefab(level)` for the equipped level.
     Props are stripped of behaviours/colliders/bodies, auto-fitted to frame, yawed 32 degrees.
     Rig hides with the shop (`OnDisable`). Runtime-built so the baked shop prefabs stay
     untouched until someone authors this properly.
  2. **Failure flow is now three-act** (per map entry; counter resets in `StartSelectedMap`):
     1st fail -> BUY ROUNDS: stepper (runtime-built from the continue button's own visuals),
     `LoseConfig.bulletPrice` = 500 gold per round, cap `firstLoseMaxBullets` = 20, suggestion =
     about one round per 2.5% short of the requirement, price label shows the total. 2nd fail ->
     the old flat continue (4,000 -> +5). 3rd fail -> nothing for sale, only the close button.
     Code: `FailScreenView.Present(mode, suggested, max)` + `GameFlowController.PresentFailOffer`
     / `ContinueRunWithPurchase` (mirrors ContinueRun's charge-last ordering exactly),
     `EconomyService.TryPayLoseBullets`. NOTE for art: the fail screen's baked "+5" art is wrong
     in BuyBullets mode - the price label carries the real numbers, but the art wants a variant.
  3. **Every campaign map's bulletPickLimit set to 20** (27 maps; tutorial stays 3) at Falcon's
     request - his call to tune from here, replacing the interim blocks/7 values from 01h.

- **2026-09-01k** Field fixes on 01j after Falcon's playtest:
  1. Showcase overflow: the RawImage copied the preview's anchors to a sibling; with a stretched
     preview, sizeDelta is an inset and reusing it as a size blew the cannon over the whole
     screen. Now parented INSIDE `previewImage`, full-stretch, zero offsets - it cannot leave
     the frame.
  2. Empty ammo table: Rock has no `projectilePrefab` of its own (by design - the fire
     controller's ball is the fallback). The shop now falls back the same way: new
     `GridKnockdownCannonFireController.DefaultProjectilePrefab` accessor, used when the bullet
     brings no prefab.
  3. First-lose screen was showing the baked "+5 / Add 5 ammo!" banner over the real numbers.
     In BuyBullets mode the banner art is hidden and the purchase draws its own card in the
     banner's rect: big "+N", per-round price, x-count with minus/plus flanking. The banner
     comes back untouched for the flat-continue mode. Node found by name ("Banner", from
     FailScreenBuilder); absent, everything anchors to the continue button as before.

- **2026-09-01l** PRIORITY NOTE for the dev side, on the shop 3D showcase specifically: the dev
  has their own 3D shop display in review. **Theirs wins.** Falcon's `ShopModelShowcase` from
  01j/01k is a stopgap so the tables were not empty this week - it was runtime-built precisely
  so it would be cheap to delete. When the dev's version lands, remove without hesitation:
  - `Scripts/UI/ShopModelShowcase.cs` (+ .meta) - self-contained, nothing else references it;
  - the `showcase` field and the "3D prop on the table" block in `BulletShopView.RefreshPreview`
    and `VehicleShopView.RefreshPreview`, plus the `showcase.Hide()` lines in both OnDisable;
  - KEEP `GridKnockdownCannonFireController.DefaultProjectilePrefab` - it is a generic accessor
    (the same fallback the shot takes) and useful to any preview implementation.
  No data or prefab depends on Falcon's showcase; deleting it cannot strand a scene reference.

- **2026-09-01m** Three more from playtest:
  1. **Backdrop is adjustable again, per mission.** `Mission` gained `backdropOffsetY`
     (world-units slide, +-20) and `backdropScale` (0 = 1): `MissionConfig.GetBackdropPlacement`
     serves them and `LevelScenery` applies them to the backdrop strip's parent EVERY frame -
     deliberately, so both sliders answer live while dragged in Play mode (ScriptableObject
     edits persist out of Play, so tuning there is authoring). Mission 2's beach picture was
     framed on its sky half; slide it up with offsetY until the horizon sits where wanted.
  2. **Off-centre rotation root-caused, again and properly this time.** The orbit pivot is fixed
     at the structure root; main's `ResolveGridOrigin` centres the DECLARED grid, so any map
     whose content does not fill its grid stands with its mass off the orbit axis. Ported
     Falcon's content-bounds centring (`TryResolveContentBounds`, measured from real block
     footprints) into main's file - now with no WallVisual baggage, since the dev's own baker
     rewrite made the earlier conflict moot. JSON-built maps (all 18 new ones) centre correctly
     at build time; if any REBAKED mission-1 prefab still sits off-axis, re-bake it - the
     centring happens at build, and a prefab keeps whatever it was baked with.
  3. **First-lose suggestion: wallet outranks need.** suggested = clamp(min(needed, affordable),
     1, 20) where affordable = gold / bulletPrice. A 6,500-gold suggestion at 1,990 gold was a
     wall, not a suggestion. The +/- can still dial past it; the buy button simply refuses what
     the wallet cannot cover.

- **2026-09-01n** Purchase-card polish per Falcon's UX pass: the small "xN" under the price is
  gone (the big "+N" already says it); minus and plus now sit on ONE row flanking "500 / round";
  the cloned pill buttons are stripped of every inherited child (the coin icon and price label
  came along with the clone) down to background + one bold glyph; and the pill background is
  `preserveAspect` so no rect on any aspect ratio can squash it. Layout remains anchored to the
  banner rect, so it scales with the card rather than the screen.

- **2026-09-01o** Menu and shop flow per Falcon:
  1. Main menu: the PLAY pill is gone as a button-shaped thing - `InstallTapToPlay()` in
     GameFlowController stretches the existing playButton invisible over the whole menu (first
     sibling, so the bottom bar's buttons still raycast in front) and rewrites its label to
     TAP TO PLAY at the old pill's height. Same wiring, same EnterMapSelection - a tap anywhere
     on the menu background enters mission select.
  2. Shop: a runtime TAP TO PLAY text-button at the shop's foot, also into EnterMapSelection.
     Both are stopgap runtime UI like the showcase - baked screens untouched, dev art welcome
     to replace them.
  3. New menu item `Tools/Smashdown/Reset Garage Save (keep missions)`: deletes the
     `gamejam.user.bullets` and `gamejam.user.vehicles` PlayerPrefs records and calls
     `UserData.Reload()` so cached statics cannot write the old garage back on exit. Missions,
     gold, tutorial untouched.

- **2026-09-01p** Tap-to-play placement redone after Falcon's screenshots: the menu label now
  stands on the lawn (own label under mainMenuRoot at anchor y 0.3; the stretched-invisible PLAY
  pill keeps the tap, its authored labels are deactivated instead of reused - stretching their
  parent had thrown them into the mission header). The shop label hangs from the panel's bottom
  edge (anchor bottom, pivot top) so it lives in the gap between panel and bottom bar, and the
  label itself is the only hit area - nothing was added inside the blue panel. Also verified
  progression numbers on request: bullet damage per level and vehicle multipliers are live data;
  fire rate has NO stat anywhere - no cooldown in input or fire controller, so spam speed is
  finger-limited and identical across every bullet and cannon (flagged as a possible design
  lever / abuse point, especially with the double aim-solve per tap).

- **2026-09-01q** EXPERIMENT, kept parallel to the original firing (a toggle, not a rewrite):
  `GridKnockdownCannonFireController.burstPerVehicleLevel` (default ON) makes one tap fire as
  many rounds as the equipped cannon's LEVEL - level I one round, II two, III three - leaving the
  muzzle SEQUENTIALLY at `burstInterval` (0.09s) apart, aimed at the same solution. The tap's
  total damage is unchanged: each round carries damageMultiplier x (1/rounds). ONE bullet of
  inventory pays for the whole burst, `Fired` raises once per paid tap (tutorial counts stay
  right), a disable stops an in-flight burst. Switch the toggle off and the path is byte-for-byte
  the old single shot. Falcon's framing: "upgrades add a barrel" as feel, without touching the
  models. Also answered: there is NO coded fire-rate limit; the spam ceiling is the input shape -
  a shot fires on pointer RELEASE, so each shot needs a full press+release cycle (two Update
  frames minimum, in practice the player's tap rhythm, ~120-200ms). The pool cannot be exhausted
  by spam: it is warmed to the run's bullet budget.
- **2026-09-01r** Menu TAP TO PLAY nudged by screenshot ping-pong to anchor y 0.22 of
  mainMenuRoot. CAUTION: the lawn art scales with aspect while the anchor is a screen fraction,
  so "middle of the lawn" drifts between game-view shapes - if it reads wrong on device, this
  one number in `MakeTapLabel`'s call site is the whole adjustment.
- **2026-09-01s** Burst rounds now leave from the SIDE barrels: round one from the centre
  muzzle, the follow-ups offset along the shot's own right axis by `burstMuzzleSpacing`
  (default 0.35, serialized) - one follow-up takes one side, two take both. Directions stay
  parallel to the aimed solution. Spawn points are now PER ROUND, PER MODEL:
  `VehicleDefinition.Level.muzzleOffsets` (Vector2 per round; x = rightward, y = upward against
  the aim, world units; empty = symmetric auto-spread). Two field reports drove this shape: a
  two-round burst from centre-plus-one-side read as "a three-barrel cannon missing one", so the
  unauthored two-round default is now half a spacing to EACH side; and Cannon B's level II model
  stacks its barrels VERTICALLY, which no horizontal spacing could match - hence two axes,
  applied along the shot's own right/up. Seeded: A II (+-0.16,0), C II (+-0.18,0), B II
  (0,+-0.14); III = centre+sides for A/C, a vertical column for B. Eyeballed from screenshots -
  tune in the vehicle assets, no code involved.
  The shop's TAP TO PLAY
  is re-pinned by SCREEN fraction (18% up, centred) against the canvas corners, because the shop
  root's rect is a prefab child with bounds nothing like the screen - anchoring to it had put
  the label anywhere but the gap under the panel. New menu item
  `Tools/Smashdown/Set All Cannons To Level II` for eyeballing the two-barrel variants.

- **2026-09-01t** Support model rebuilt as GROUND REACHABILITY, replacing both of 01h/01i's
  per-block rules after a field report: touching ONE leg of the relay dish dropped the whole
  dish, because "release any neighbour without direct support below" declares every interior
  plate cell unsupported the moment its neighbour moves. The two retired rules were each wrong
  in one direction (column-climb left overhangs floating; neighbour-release collapsed plates).
  Now `StructureRegistry` runs one batched flood per physics step when dirtied
  (`RequestSupportFlood` from Activate/ActivateFromSupportRelease/Unregister): BFS from every
  grounded block through 6-neighbour cell adjacency; whatever cannot reach the ground is
  released together (`KnockdownBlock.ReleaseFromFlood`). Consequences, all intended: a dish on
  four legs loses only the struck column strip until its LAST leg goes, then the whole plate
  drops as one; laterally-held overhangs stand until actually severed; a severed piece always
  falls, so 100 percent clears stay reachable. The dev's own column-release on a direct hit is
  untouched. Cost: one O(cells) BFS per dirty physics step, ~2,200 dictionary probes on the
  largest map.

- **2026-09-01u** Stars, part one (mission board). `MissionConfig` gained two authored
  thresholds - `twoStarClearPercent` (0.75) and `threeStarClearPercent` (1.0) - and
  `StarsFor(passed, bestClearPercent)`: no pass no star, the pass itself is the first star, the
  best-ever percent buys the rest. Best percent is already monotonic in the save, so a worse
  replay can never lose a star. The board's card shows the stars IN PLACE of the retry arrow
  (gold ★ glyphs, runtime row over the button's rect, taps pass through to the button
  beneath); an unplayed or failed card keeps its plain blue button, exactly as before. The
  cleared-screen presentation is the declared next step and should reuse the same StarsFor.

- **2026-09-01v** Board fixes after the star rollout blanked every passed card: a runtime-added
  TMP text has NO font unless the project authored a TMP default, so the star row drew nothing
  and errored once per card - and with the retry arrow already hidden for starred cards, passed
  cards showed empty. First fix borrowed the title's font - not enough: the
  project's font is a baked BITMAP atlas (LilitaOne Outline 64) with no U+2605 glyph and no
  fallbacks, so TMP swapped the star for a space (console: "character \u2605 was not found").
  Text stars are a dead end on this project. The row is now three UI Images. Art comes from the
  Layer Lab pack the project already ships - `Icon_ImageIcon_StarGrade_l_On/Off`, wired into
  `MissionProgressItemView.prefab` as `starOnSprite`/`starOffSprite` - so a passed card shows
  three grade slots with the earned ones lit. The procedurally drawn star (5-point polygon,
  supersampled, shared static Sprite) stays in the view as the fallback when the sprite fields
  are empty, so an unwired card still shows something. Two more per Falcon:
  the WHOLE card is now the tap target (invisible full-card catcher behind the art, forwarding
  to the same wired click, dead when the button is dead) - no more aiming at the small button;
  and a new menu `Tools/Smashdown/Reset Mission 3 Progress` removes only `mission3_*` records
  from the save (missions 1-2, garage, gold untouched).

- **2026-09-01w** Stars, part two (cleared screen). `ClearedScreenView` gained `missionConfig` +
  the same StarGrade On/Off sprites (wired in ClearedScreen.prefab) and draws a three-slot row
  under the CLEAR! banner (anchor y 0.66, middle star raised and larger). It shows THIS RUN's
  stars from `StarsFor(true, result.ClearPercent)` - deliberately not the saved best: the board
  is the best-ever record, the result screen is tonight's score, and both read from one StarsFor
  so a star can never mean two things. Per Falcon's juice pass the grey slots stand still from
  the first frame and each EARNED star drops onto its slot one at a time (0.16s gravity fall
  from 2.4x scale, BallImpact on touch, 0.3 squash, a decaying 14px shake of the whole screen
  root per landing, 0.12s between stars) - coroutine-driven, no tween library, stopped and
  reset on disable. Revised same day: shake retired (read as jank at size - the slam alone
  carries it), stars up to 128/156px at 150px spacing, CLEAR! badge scaled 1.3x in the prefab.

- **2026-09-01x** Audio status checked (dev side wired it fully in the 43-commit drop: 17/17
  slots filled, service + music director in scene, 14 play sites, 29 clips committed with
  metas). One addition from Falcon: new AudioSlot.StarLand - the result screen's stars land
  with a THUMP, not a chime, per Falcon - seeded with the unused sfx_hitwood; swap the clip in
  AudioConfig.asset if it reads wrong. ClearedScreenView plays it per landing instead of the
  borrowed BallImpact. Unused clips remaining: 12, all for features that do not exist yet
  (hearts, wood/ice/steel/blackhole materials).

- **2026-09-01y** Result/fail polish round from playtest: cleared-screen star SLOTS are now the
  On art tinted mid-grey (0.42,0.47,0.58) - the pack Off sprite is a dark navy that vanished on
  night backdrops; row lowered 0.66 -> 0.60 (was crowding the banner); StarLand clip swapped
  hitwood -> crackedwood for a heavier thump (Play has no per-call volume, so louder = heavier
  clip). Fail screen: the +N count up to 190pt, and the buy button breathes (1 -> 1.07 sine at
  4.4 rad/s, unscaled time) only in BuyBullets mode - stopped and scale-reset on mode change
  and disable.

- **2026-09-01z** The missing reward panel root-caused: 01y's 1.3x badge scale grows DOWN from a
  top pivot, so the bigger banner's foot (now 0.714) covered the Reward band (0.696-0.743) -
  the gold panel was behind the ribbon, not gone. Relayout, all in the prefab: badge raised to
  y -20 (foot 0.745), Reward band moved to 0.60-0.647, star row seated between them at 0.695.
  Note for testers: on a REPLAYED map the gold panel hides by dev design (rewards are
  claim-once; GoldAwarded 0 is not drawn) - only first passes and first full clears pay. Star
  art swapped to Icon_ImageIcon_Star01_l on both screens - the StarGrade art reads washed at
  size; slots stay the same sprite grey-tinted. Fail screen +N raised to y 115 in a 520x230
  box so the glyph centres true.

- **2026-09-02a** Two crispness fixes: mission-header arrows shrunk 46x44 -> 34x32, dropped to
  anchor y 0.966 and tucked 26px inward - they sat above the tab's visual centre and read
  oversized. And the runtime-built labels (+N, per-round, step glyphs, TAP TO PLAY) now render
  with the pack's LilitaOne-Regular SDF font asset (new serialized labelFont/tapLabelFont,
  wired in FailScreen.prefab and the scene): the project default they inherited is a 64px
  BITMAP atlas, which is why 190pt text looked soft - an SDF stays sharp at any size.

- **2026-09-02b** Menu TAP TO PLAY breathes (1 -> 1.08 sine, 3.6 rad/s, unscaled) like the buy
  button. Test-bench resets reorganised into ONE fold-out, Tools/Smashdown/Reset/: All (whole
  save to first launch), Tutorial (plays again next entry), Garage (bullets+cannons, keep
  missions), Mission 1/2/3 Progress (per-mission map records only). Each logs exactly what it
  cut and what it left. Unlock Full Garage / Set All Cannons To Level II stay one level up.

- **2026-09-02c** Second tutorial beat, taught in place: entering mission1_map1 with the new
  UserTutorialData.dragTaught flag unset shows HOLD & DRAG TO ROTATE with the pack tutorial
  hand sliding LEFT, fading, then RIGHT, on a loop - until the player performs a real
  hold-and-drag (60px of sideways travel while pressed), which sets the flag for good. Leaving
  the level without dragging lets it return next entry; Reset/Tutorial and Reset/All clear the
  flag (same save record). New DragHintController on the GameFlowController object, runtime UI
  under the main canvas, SDF font, no prefab edits.

- **2026-09-02d** Drag hint v2 after review: the FIRST tutorial's own dim (Filter.png, the
  grey-with-spotlight fallback the builder ships) now sits behind the hint, oversized and slid
  so its painted hole frames the structure at screen y 0.63; the hand demonstrates ON the
  structure inside the spotlight (was floating under the caption); the caption moved below the
  board at y 0.40; the group no longer blinks - the dim holds steady and only the hand travels
  and fades per swipe. On the "another hand?" question: the pack ships exactly one
  (Icon_ImageIcon_Tutorial_Hand) - a different pointer needs new art.

- **2026-09-02e** Drag hint v3: shows the FRAME the level starts (delay and fade-in removed -
  matching the first tutorial prompt); the words moved into the first tutorial's own rounded
  panel (UI_Tutorial sprite, dark-blue text, two lines HOLD & DRAG / TO ROTATE) seated at
  y 0.42 under the board. Falls back to plain white text when the panel sprite is unwired.

- **2026-09-02f** Lesson ordering bug from the screenshot: after Reset/Tutorial BOTH flags are
  clear, so the shoot prompt and the drag hint stacked on one screen. The drag hint now also
  requires UserData.Tutorial.completed - it is strictly lesson two: shoot first, then rotate.

- **2026-09-02g** The "two stacked prompts" were ONE panel all along: "Tap to shoot" is
  PAINTED INTO UI_Tutorial.png, so borrowing that sprite for the drag hint put baked art text
  under the TMP text. Swapped to the pack's plain Label_Round01_White (no baked words), panel
  170 tall. And per Falcon: firing is refused while the drag lesson is up - new static
  DragHintController.BlockingFire, read in CannonInputShooter's release branch, so taps buy
  nothing until the player answers with a real drag (rounds cannot be wasted on the lesson).

- **2026-09-02h** Label_Round01_White was the wrong stand-in: near-square art + preserveAspect
  collapsed it to a thin pill with the text overflowing. Falcon's call ("fake UXUI"): back to
  UI_Tutorial.png (panel 560x246, its real 551x242 aspect) with a flat "Cover" Image over the
  baked words - colour 0.984/0.973/0.937, the measured average of the art's paper, insets
  70px sides / 55px top-bottom so it stays inside the blue rim - and the TMP label on top.
  Hierarchy inside the hint: Panel(UI_Tutorial) > Cover(flat paper patch) > Label(TMP). If the
  art ever gets a text-free export, delete the Cover block in DragHintController.EnsureHint and
  this note. NOTE for AI: DragHintController.cs was accidentally truncated and rebuilt this
  session - behaviour is identical to 02c..02g (gating, BlockingFire, 60px dismissal, swipe
  loop), only the panel block is new; diff-review the whole file rather than assuming an
  incremental edit.

- **2026-09-02i** Star bands retuned (Falcon): 3-star drops from 100% to **95%**. One asset
  value - `MissionConfig.asset` threeStarClearPercent 1 -> 0.95; every consumer (mission board,
  cleared screen) reads `MissionConfig.StarsFor`, so no code moved. Bands now:
  1* = [50%, 75%) - 2* = [75%, 95%) - 3* = >= 95%.
  GOLD, for the record (RewardConfig, claim-once per map): pass (>=50%) = 50+50xN gold
  (map1 100 ... map27 1,400; total 20,250); clear bonus = 250/350/450 for maps 1-3 then
  500..1,650 in +50 steps (total 26,850); grand total 47,100. WATCH OUT: the clear bonus still
  requires **100%** (`LevelRunController:270` FullyCleared = clearPercent >= 1f) - a 95-99% run
  now earns 3 stars but NOT the clear gold. Deliberate for now; if Falcon wants gold to follow
  the 3rd star, point NewlyCleared at 0.95 or add a reward tier - not done, flagged only.
  Stars still award no gold of their own (open question from 09-01).

- **2026-09-02j** Reload system, per Falcon ("toc do reload + thanh reload ben duoi"), replacing
  the old fire-as-fast-as-you-tap:
  - `VehicleDefinition.Level.reloadSeconds` (new field + `ResolveReloadSeconds`): seconds
    between shots at that vehicle level. **0 = no gate** (exact old behaviour), so anything
    unauthored is unchanged. Falcon tunes these himself; seeded placeholders in
    cannon_a/b/c.asset: level I 1.2s, II 1.0s, III 0.8s (upgrades shoot faster).
  - `GridKnockdownCannonFireController`: `TryFireAtScreenPoint` refuses silently while
    `IsReloading` (before the ammo check - no out-of-ammo popup mid-reload, no ammo spent);
    the timer arms right after the PAID FireSingle in `Fire()` (burst rounds 2..n are the same
    shot, never gated); `PrepareForRun` zeroes it so a run never starts mid-reload. Public for
    UI: `IsReloading` / `ReloadRemaining` / `ReloadDuration`.
  - NEW `Scripts/UI/ReloadBarController.cs` (guid 8a4f1c29d3b64e7f9a02c5d1e6f7a8b9), on the
    GameFlowController GO (scene component 1500100012): runtime-built slim bar (340x16, dark
    back, gold fill) bottom-centre at screen-height 0.075 (serialized `screenHeightAnchor` -
    the one number to move it). Visible ONLY while reloading, fills left->right, hides when
    ready. Anchor-driven fill (no sprite needed). Same stopgap pattern as the drag hint:
    delete file + scene component to remove.

- **2026-09-02k** Multi-shot RETIRED as default behaviour (Falcon): every tap is one round
  again, whatever the cannon's level. Multi-shot is re-cast as a CHARGE - destined for the
  **ads / paid-item packages** (Falcon's call, not built yet) - and, unlike the old burst,
  **each round of a charged shot costs its own bullet** (damage still split /2, /3).
  QUICK-SWITCH MILESTONES, nothing deleted:
  - `burstPerVehicleLevel` (fire controller, code default now false, not serialized in the
    scene): flip ON to restore the 09-02 experiment exactly - level = rounds per EVERY tap,
    ONE bullet per burst.
  - `ArmShotBoost(rounds)` (public, fire controller): the charge hook. The free intro popup
    calls it today; the ads/shop flows call the same method later. Armed charge is consumed
    by the next paid tap, rounds each spend a bullet (`FireBurstRest` got a `spendPerRound`
    flag; pouch empty mid-burst = remaining rounds silently don't fire), cleared by
    `PrepareForRun`.
  - Per-model `muzzleOffsets` and `burstInterval` serve both modes unchanged.

- **2026-09-02l** The "1 FREE" intro popups, NEW `Scripts/Gameplay/Flow/ShotBoostIntroController.cs`
  (guid 7b3e9d1a5c2f4e8ab6d0f4c8a9e21b37, scene component 1500100014 on GameFlowController):
  first level entered after ANY cannon first reaches level 2 -> centred popup, soft fade+scale
  0.25s, "1 FREE" 72pt over "DOUBLE SHOOT" 40pt on the UI_Tutorial fake-UXUI panel; the whole
  panel is the button - tapping arms ArmShotBoost(2) and saves `doubleShotIntroDone`
  (UserTutorialData). Same again at first level-3 cannon -> "TRIPLE SHOOT",
  `tripleShotIntroDone` (a save that jumps straight to level 3 gets ONLY the triple, both
  flags set). While open: full-screen raycast dim + static `ShotBoostIntroController.BlockingInput`
  checked in CannonInputShooter's BeginPress AND release - no firing, no board rotation.
  Queues BEHIND the drag lesson (waits while DragHintController.BlockingFire). Requires
  Tutorial.completed. Reset/Tutorial tool clears the flags with the rest of the tutorial key.

- **2026-09-02m** Reward fixes (Falcon): every milestone pays ONCE per map, and the ladder is
  now pass / 2-star / clear-at-95:
  - **2-star (>=75%) = +75 gold**: one shared RewardConfig entry `two_star_bonus` (75);
    once-only is per map via new `MapProgress.twoStarRewardClaimed` +
    `MapAttemptResult.NewlyTwoStar` (bar = `UserMapProgressData.TwoStarRewardPercent` 0.75,
    keep equal to twoStarClearPercent). Granted in `LevelRunController.GrantRewards`, skipped
    for maps outside the economy (tutorial authors no passMapRewardId).
  - **Clear bonus now at >=95%, not 100%**: `UserMapProgressData.ClearRewardPercent` 0.95
    (keep equal to threeStarClearPercent) replaces the two hardcoded `>= 1f` (RegisterAttempt
    + LevelRunController.Judge). Falcon considered an extra 100% tier and decided against it -
    95% IS the top: 3rd star and clear gold land together. `fullyCleared`/`FullyCleared` now
    mean "reached 95%" everywhere downstream.
  - Full-ladder totals per map: pass gold + 75 + clear gold; campaign total 47,100 + 27x75 =
    **49,125**.

- **2026-09-02n** Reload seeds felt slow -> retuned in cannon_a/b/c.asset: level I 0.8s,
  II 0.6s, III 0.45s (was 1.2/1.0/0.8). Still placeholders; Falcon owns the final numbers.
  Also Tools/Smashdown/**Give 999,999 Gold** added to GarageSaveReset for economy testing
  (sets UserData.Inventory.gold outright, touches nothing else).

- **2026-09-02o** Boost-intro popup UX pass (Falcon feedback on the screenshot): cluster
  raised to mid-frame (panel anchor y 0.5 -> 0.6); a white pulsing "TAP TO CONTINUE" line
  hangs under the panel; confirm is now a tap ANYWHERE (Button on the full-screen dim; panel
  no longer a raycast target). New 0.5s lock (`MinViewSeconds`): for the first half second
  taps are swallowed and the continue line is hidden - it appears, pulsing, exactly when taps
  start counting, so the player provably saw "1 FREE DOUBLE SHOOT" before it can be
  dismissed. Guard is double: the reveal delay AND a time check inside UseCharge.

- **2026-09-02p** Barrel alternation (Falcon: single shots on a multi-barrel cannon looked
  origin-less - they always left barrel one). New `muzzleCycle` in the fire controller:
  single taps walk the authored `muzzleOffsets` in asset order - cannon_a/c II left->right,
  cannon_b II top->bottom (assets already list top/left first), III centre->left->right -
  advancing only when the tap is actually paid, reset to barrel one at every run start
  ("dau tran nong tren truoc"). A Double/Triple burst consumes its rounds from the cycle in
  order and advances it by that count, wrapping when the burst outnumbers the barrels.
  Un-authored levels keep the symmetric defaults, untouched.

- **2026-09-02q** Mission-unlock helpers + locked-mission look (Falcon, for the review video):
  - Clarified first: "Unlock Full Garage khong mo" was a scope mix-up - that tool DID work (all
    cannons at III, the earlier question proved it); it never touched missions. Missions unlock
    off map `passed` flags, so both new helpers mark all 27 campaign maps passed at 50% best
    (rewards left unclaimed - replaying and passing still pays).
  - Editor: menu regrouped as **Tools/Smashdown/Unlock/{All Missions, Full Garage}** (mirrors
    the Reset fold-out, Falcon's #3). Give 999,999 Gold stays at the Smashdown level.
  - In-game: **UNLOCK ALL MISSIONS (DEV)** red row appended to the GET GOLD placeholder screen
    (IapShopView.AddMissionUnlockDevRow) - deliberately on the screen that already cannot ship;
    it dies with the placeholder when real IAP lands. Named with RowNamePrefix so ClearRows
    sweeps it.
  - `MissionPanelView.ApplyBackgroundMood`: paging onto a LOCKED mission now darkens the board
    art itself (prefab "Background" Image, tint x(0.45,0.45,0.52), authored colour restored on
    unlock) on top of the existing card grey-out. Falcon asked blur+dark; a real blur needs a
    shader/RenderTexture pass, so tint carries the mood - revisit if the dev wants true blur.

- **2026-09-02r** Tutorial reward + vehicle price curve (Falcon):
  - Tutorial first completion pays **+100 gold** (`TutorialController.TutorialRewardGold`,
    guarded by the not-yet-completed check so replays never pay twice; a tutorial reset makes
    it a fresh player, who is paid again on purpose).
  - New vehicle prices, tuned to pass-gold pacing (tutorial 100 + pass 50+50xN):
    buy A 0 / B **1,200** (was 800) / C **2,500** (was 2,000);
    upgrades A **500/1,500** (was 300/700), B **2,000/3,500** (was 1,200/2,000),
    C **4,000/6,000** (was 2,500/4,000).
  - The pacing it encodes: A-II lands right after map 3 (income 550); B lands right after
    map 7 (1,850 earned, 1,350 in pocket after A-II); A-III lands around map 9 IF the player
    also collects some 2-star/clear bonuses (pass-only leaves ~400 short - deliberate, stars
    are the top-up). Falcon's rules hold: each later gun costs more than the one before;
    pushing an old gun a tier above the next gun costs a bit more than that gun's entry
    (A-III 1,500 > B 1,200; B-III 3,500 > C 2,500); maxing gun A (2,000 total) stays cheaper
    than buying gun C (2,500).
  - Full garage now costs 20,400 (bullets 800 + A 2,000 + B 6,700 + C 12,500, rock_type's
    orphaned 400 upgrade still unbuyable) vs 49,125 earnable - the campaign can max out with
    room for lose-continues.

- **2026-09-02s** Replay pass = **+25 gold, every time** (Falcon). New shared RewardConfig
  entry `replay_pass_bonus` (25); `LevelRunController.GrantRewards` pays it whenever an
  attempt passes but the map's own pass reward is already claimed (the else-branch of
  NewlyPassed, so a first pass never gets both). Deliberately NOT claim-once - it is the
  grind trickle; tune by editing the one entry. Tutorial and other economy-less maps skip it
  (same empty-passMapRewardId gate as the 2-star bonus). This closes the earlier
  "replays legitimately pay 0" note. The 2-star and clear bonuses on a replay still pay only
  their first time, as designed.

- **2026-09-02t** Tutorial 100 gold made VISIBLE (Falcon: "sao ko co 100 vang?" - the 02r
  version paid silently into a 998k test wallet and only when the completed flag was fresh).
  Reworked through the reward pipeline: RewardConfig entry `tutorial_complete` (100),
  MapProgressionConfig's tutorial row now authors `passMapRewardId: tutorial_complete` -> the
  first tutorial pass pays claim-once AND shows on the cleared screen like any map reward.
  TutorialController's silent Inventory.Add removed (would have double-paid). The 2-star and
  replay-25 gates switched from passMapRewardId to **clearMapRewardId** (tutorial authors
  none), so the tutorial gets exactly its 100 and no trickles. NOTE: claim-once now rides the
  "tutorial" map's passRewardClaimed - Reset/Tutorial alone repeats the tutorial WITHOUT
  paying again (map progress keeps the claim); Reset/All pays again, as a truly fresh player.

- **2026-09-02u** mission1_map8 difficulty trim (Falcon: "Level 8 kho qua"): the two 1-wide,
  3-tall concrete strips flanking the long concrete pillars on EACH face - front layer 0 and
  back layer 6, columns x=4 and x=7, rows y=3..5 - converted concrete_1x1 -> brick_1x1.
  Exactly 12 cells, clean 12-line JSON diff, geometry untouched. Block mix now 120 concrete /
  132 brick (was 132/120). REMEMBER: the map loads from its BAKED PREFAB
  (Map_mission1_map8.prefab), so the JSON edit is invisible until Tools/Smashdown/Bake Map
  Prefabs is re-run - Falcon does that in-editor after pulling this change.

- **2026-09-02v** Barrel convergence fix (Falcon: 2/3-barrel shots drift, worst aiming to the
  sides). Root cause: the ballistic solve runs once from the central muzzle, and offset-barrel
  rounds reused that direction verbatim -> parallel arcs that land exactly one barrel-offset
  beside the tap; with barrel alternation (02p) even single taps showed it. New
  `ResolveBurstDirections` in the fire controller: each round's solved direction is YAWED so
  its horizontal heading runs from ITS barrel to `CannonAimController.LastAimWorldPoint` -
  speed and launch angle untouched (same arc shape in a rotated vertical plane), so every
  round crosses the tap's vertical line at the block, per Falcon's spec ("diem cuoi theo
  phuong doc thang hang"). Stacked barrels' small height offset intentionally kept (reads as
  barrel position, not aim error). No valid aim point (demo path) -> old parallel behaviour.
  FireBurstRest now takes a per-round directions array.

- **2026-09-02w** FOR THE DEV - far shots deliberately fly low (Falcon schedules this for
  tomorrow; nothing changed in code, this entry is the whole handover, itemized per Falcon).

  **INSTRUCTION TO THE DEV'S AI, read first:** before touching anything, COMPARE this note
  against the code as it stands in your checkout - open `CannonBallisticAimMath.cs` and
  `CannonAimController.GetFireDirection`, verify each numbered claim below (the ~line numbers
  may have drifted), and report to the dev where the note and the code agree or differ. Then
  put item 5 (was it deliberate?) to the dev BEFORE implementing any option in item 6.

  1. SYMPTOM (Falcon's words): if the targeted block is within a certain range ("neu block
     duoc target trong pham vi nao do"), the ball arcs up on its own and takes the drop point
     ("dan tu canh bay len troi lay diem roi"); past that range it visibly chooses a LOW flat
     path ("co tinh bay thap") and lands short of the tapped block. The boundary between the
     two behaviours is exactly the solver's reach at projectile speed 22.
  2. MECHANISM, in-range: closed-form ballistic solve, minus-root = shallow arc
     (`CannonBallisticAimMath.TryBuildAnalyticDirection`, ~:75-95). This is the pleasant
     auto-arc.
  3. MECHANISM, out-of-range: the discriminant (~:51-53) goes negative once the target
     exceeds what projectileSpeed covers (speed **22**, serialized on
     GridKnockdownCannonFireController; scene value at Gameplay.unity:3212). Flow drops into
     `TryGetLaunchDirectionBySimulation` (~:101+): a 45-step angle search **capped at
     MaxLaunchAngleDegrees = 35** (const ~:14) that fires the least-miss angle ANYWAY - that
     best effort is the flat ~35-degree shot landing short. (The straight-line fallback in
     `CannonAimController.GetFireDirection` ~:126-133 only covers degenerate solves - not the
     main path.)
  4. REPRODUCE: editor / dev build logs "Cannon aim out of reach: no launch at or below 35
     degrees..." (~:131) on exactly these taps. Elevated targets lose reach first (the
     -2*g*dy*v^2 term); orbiting so the block sits across the far diagonal adds distance.
  5. FALCON'S VERDICT OPTION: if this was DELIBERATE, leave it - no change ("neu la dev co
     tinh thi ko can chinh them"). The code reads half-intentional: the 35-degree cap has a
     design comment ("a mortar is never what this cannon wants"), but firing the least-miss
     shot instead of refusing an unreachable tap looks like a fallback, not a feature. The
     dev decides; the AI should present this comparison, not decide it.
  6. OPTIONS IF IT IS TO CHANGE (pick per feel, none implemented):
     a. Raise MaxLaunchAngleDegrees toward 45 - max range, but the mortar look creeps in.
     b. Raise projectileSpeed - more reach, faster feel everywhere.
     c. Scale speed with target distance so the solve always closes - keeps near-shot feel;
        the aim preview must use the same scaled speed.
     d. Clamp the aim plane / refuse unreachable taps visibly instead of undershooting
        silently.
  7. NO KNOCK-ON WORK: the muzzle re-solve (`GetFireDirection`, spawnOffset 0.28) and the
     per-barrel yaw convergence (entry 02v) both consume whatever direction the solver
     returns - none of the options above requires touching them.

- **2026-09-02x** NEW MAP `mission3_map10` - "Orbital Space Station", built from Falcon's
  concept art (LEVEL 10, ring station with 6 arms, solar wings, 12 weak points). Mission 3
  now has TEN maps - first mission to exceed the blueprint's 9; the board grows a 10th card
  by itself (cards are spawned per mapId).
  - Geometry (grid 17x12x7, 190 entries: 137 concrete / 29 glass / 24 brick, JSON-only, NO
    baked prefab): plan-ellipse hull ring (2 rows, y4-5) standing on 4 concrete legs at the
    diagonals (y0-3); hollow 3x3 core shaft y0-7 with glass viewport band y5-6, cross cap
    y8, glass dome tip y9, brick antenna y10-11; six brick truss arms (y4) core->ring with
    a brick module pod (2 tall) on each junction; two 2x3 flat glass solar wings
    cantilevered off the ring's E/W hull at y5. Concept art floats the station over a pad -
    the engine's no-floating rule grounds it on the legs instead, which IS the level: cut
    legs -> ring+modules crash; cut the core stem -> dome and antenna ride down; wings are
    cheap glass rain. Weak-point count lands ~12 like the art sheet.
  - Registration: MapConfig entry M3_10_OrbitalStation (json guid
    4e7a2b91c5d34f8ab0e6d1c7f9a35b28, mapPrefab 0 -> loads from JSON, bake optional);
    MissionConfig mission_3 mapIds += mission3_map10; MapProgressionConfig entry (0.5 clear,
    pass/clear_map_28, 20 bullets); RewardConfig pass_map_28=1450, clear_map_28=1700
    (ladders continue +50). Campaign totals move: 28 maps, earnable 49,125 + 1,450 + 1,700 +
    75 + 25-per-replay = ~52,350 base.
  - For the dev's AI: the generator honoured the flood-support model (every block reaches
    ground via 6-neighbour paths; solar wings hang off the ring on purpose). If the map
    reads too sparse in-play, thicken the ring to 3 rows or double the arms before touching
    anything else.
  - v2 SAME DAY (Falcon rendered v1: "trong hoi te" / "cho no chi tiet, khong lo"): rebuilt
    boss-scale - grid 20x14x11, **404 entries** (303 concrete / 61 brick / 40 glass). Ring
    now 2 cells thick x 3 rows with a brick accent stripe; legs doubled with 3-cell feet;
    core a hollow 4x3 drum, 2-row glass viewport, slab cap, stepped glass dome, spire with
    brick antenna nubs; 6 truss arms 2 rows (upper chord gapped); module pods now lie
    HORIZONTALLY outward (v1's stood up like chimneys), E/W capsules 3 long with white
    noses; 4 glass solar paddles (3x2 + strut) sweep into the empty plan corners like the
    art's angled panels. Generator now auto-prunes any cell the 6-neighbour ground flood
    cannot reach (v1 shipped 4 floaters; diagonal-step wings pruned themselves until the
    connector cells made them face-adjacent) - keep that prune step in any future map
    generation. Entry count is boss-tier (blueprint boss ~380): watch Android perf, every
    cell is its own rigidbody on this branch.

- **2026-09-02y** Mission board SCROLLS (Falcon: the 10th card was cut off with no way to
  reach it). `MissionPanelView.EnsureScroll` (called from OnEnable, idempotent): adds at
  runtime a RectMask2D on the card container's parent, a ContentSizeFitter (vertical
  preferred - the prefab's GridLayoutGroup supplies the height) on the container, and a
  ScrollRect (vertical only, Elastic 0.1, **inertia on**, deceleration 0.135) with that
  parent as viewport - hold and drag up, a hard flick keeps gliding, per Falcon. Nothing
  hand-wired into MissionScreen.prefab; card Buttons still click (UGUI's drag threshold
  arbitrates). If scroll feel is off, the numbers live in EnsureScroll.
  FIX same day x2: (1) the fitter first grew the grid around its CENTRE pivot - the grid
  slid down the screen with no drag; EnsureScroll now top-anchors the grid first, restoring
  its top edge to the pixel. (2) THE PREFAB ALREADY HAS A SCROLLRECT - on "List", authored
  by the dev horizontal-only (m_Vertical: 0), content already = the card grid. Adding a
  second ScrollRect made the two fight and every vertical drag elastically snapped back
  (Falcon: "keo no tu giat ve"). EnsureScroll now finds that existing ScrollRect via
  GetComponentInParent and just sets vertical = true on it - which produced a DEAD board
  (fix x3, Falcon: "gio do luon roi"). Final architecture: NEW `Scripts/UI/MissionBoardScroller.cs`
  (guid 6c8d2e4fa1b94d07b3e5c9f2a8d61e43), a hand-rolled IBegin/IDrag/IEndDragHandler added
  at runtime to the VIEWPORT - being closer to the cards than the dev's ScrollRect, event
  bubbling hands it every drag first, so the authored ScrollRect is left 100% untouched and
  simply never sees drags (its horizontal axis was likely swipe-paging; revisit with the dev
  if that gesture is wanted back). Scroller: 1:1 finger tracking / released velocity decays
  ~1s / clamped live between first and last row; EnsureScroll preps geometry (top-anchor
  grid keeping its top edge, ContentSizeFitter, RectMask2D, a clear raycast Image so drags
  between cards register) and Refresh calls ResetToTop on mission switch.
  ROOT CAUSE of the freeze, found last: the prefab's own ContentSizeFitter on the grid is
  authored horizontal=Preferred, **vertical=Unconstrained** - so the grid rect's height is 0,
  scroll range = max(0, 0 - viewportH) = 0, every drag clamps dead. EnsureScroll now FORCES
  verticalFit = PreferredSize on whichever fitter exists instead of only adding one when
  missing. (Grid data for reference: 3 fixed columns, cell 150x129, spacing 17x33 -> 10
  cards = 4 rows = 615 high vs viewport ~570 -> the whole scroll range is only ~50px, just
  enough to reveal row 4 - correct, not a bug, missions with 9 cards fit and do not scroll.)

- **2026-09-02z** mission3_map10 v3 - "do so, nhieu tang, nhieu lop" (Falcon rated v2 crude,
  demanded 800+): grid **24x16x13, 887 entries** (667 concrete / 141 brick / 79 glass),
  0 floaters (auto-prune). Multi-tier silhouette bottom-up: 2x2 legs with 3-cell feet ->
  main ring 2-3 thick x 4 rows with TWO brick accent bands + railing studs on the outer rim
  -> 6 gapped truss arms with 2-wide 3-long capsule pods (white noses) -> upper second ring
  (0.62 scale, glass-flecked) bridged to the core -> hollow 4x4 drum with 2-row viewport ->
  mushroom flare deck (6x5 slab) -> stepped glass dome x2 -> spire + brick antenna nubs; 6
  thruster nubs under the ring; 4 glass paddles (4x2) off the diagonal pods running along X
  (depth is exhausted at the diagonals - lesson: on a 13-deep grid, diagonal features must
  finish their growth along X, and every walk must stay FACE-adjacent or the prune eats it).
  WARNINGS for the dev: 887 rigidbodies is well past the ~380 boss budget - profile on
  Android before shipping; and the structure tops at y15 (3.75u) - with projectileSpeed 22
  and the 35-degree cap (entry 02w) the top blocks may be unreachable, so this map is also
  the test case for whichever 02w option the dev picks.

---

## Audit appendix (2026-09-01g) — evidence, file:line

### Economy
- Cost to buy+max everything: **14,300** (bullets 600+200; vehicles: cannon_a 0+300+700,
  cannon_b 800+1,200+2,000, cannon_c 2,000+2,500+4,000). Orphaned: rock_type has a 400 upgrade
  authored but NO purchase entry - the shop shows it "not for sale" (BulletShopView reads
  PurchaseBulletConfig, which lacks rock_type).
- Earnable: RewardConfig authors 18 rewards = **7,500** distinct (pass 2,700 + clear 4,800).
  mission2_map1..3 REUSE pass/clear_map_4..6 ids; claims are per-map
  (`UserMapProgressData:126-127` gates on `passRewardClaimed`), so no farming, but the same
  reward id pays twice -> a full first pass yields **10,050**. Gap to max garage: **-4,250**,
  before the player spends 4,000 on a single continue (`LoseConfig: continuePrice 4000,
  continueAmmo 5` - one continue costs more than any map pays).
- 15 of 27 maps (mission2_map4..9, all mission3) have NO MapProgressionConfig entry: they run on
  `ResolveMapRules` fallbacks (clear 0.8, 10 shots) and pay nothing. Authored maps use 0.5 and
  10..25 shots, so the difficulty cliff at mission2_map4 is 0.5->0.8 with the shot budget nearly
  halved, for zero gold.
- IAP is still a giveaway: `IapShopView` buttons call `GrantGold` (500/$0.99, 3,000/$4.99,
  7,500/$9.99) and charge nothing.

### Verified defects (current code)
1. Fire-through-UI - PRESENT. `CannonInputShooter.cs:40-65` polls `Pointer.current`, no run-state
   gate, no `IsPointerOverGameObject` anywhere in Assets/GameJam/Scripts; Cleared screen can be
   fired through, and a drag over UI spins `CameraOrbit` (`:88-91`).
2. Lateral support cascade - PRESENT. `KnockdownBlock.ActivateFromSupportRelease` (:396-409)
   never calls `ReleaseSupportedBlocksAbove`; `StructureRegistry.CollectSupportedAbove:124-149`
   only walks the direct footprint. A block supported only by a neighbour column hangs in air ->
   100% clear unreachable on overhang maps.
3. Stale pooled ammo - PRESENT. `GridKnockdownCannonProjectile.cs:395-405` resets
   damageMultiplier only; `bulletOverride`/`bulletLevelOverride` survive the pool. Corollary:
   `Fire:269-272` calls SetAmmunition only when ammunition != null, so an unarmed shot inherits
   the previous rental's bullet.
4. `StructureRegistry.highestRow` monotonic across rebuilds (:37,67-70, no reset; authoring reuses
   the component). Register-overwrite is FIXED (unregister first, :216-219).
5. Wall-break progress dip - MOOT (walls removed; `LevelProgressTracker:86-112` counts plain
   blocks only).
6. Double ballistic solve on the tap frame - PRESENT. `CannonAimController:107-121` re-solves for
   the muzzle offset; fallback is 45x250 sim steps (`CannonBallisticAimMath:101-120`).
7. NEW: aim-origin Awake race - `CannonAimController.cs:36-39` initialises from its own null
   field; if it runs before `GridKnockdownCannonFireController.Awake:61-64`, aim silently falls
   back to the pivot instead of the muzzle. Undefined Awake order across GameObjects.

---

## TL;DR tiếng Việt

Nhánh này = `main` mới nguyên vẹn (không sửa dòng code nào) + 18 map Falcon (đã gỡ wall, đổi tên theo
chuẩn mission) + 8 texture + MissionConfig/MapConfig nối đủ 3 mission. Còn nợ: bake prefab, thêm entry
MapProgressionConfig cho 15 map mới, chơi lại cân bằng vì hết wall, và viết hệ BG/floor per-mission
gắn vào MissionConfig (code tham khảo ở nhánh Falcon/UpdateMapData).
