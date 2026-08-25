# Task Brief 03 — Android runtime optimization: map build spikes, physics cost, rendering settings

## Goal

Remove the frame-time spikes and per-frame overhead that a mid-range Android device will hit in `FierceTigerGameJam`, in priority order, with a measured baseline before and after. This brief covers the **runtime path as it exists today** (map built from JSON at run start). The map-prefab bake pipeline is a separate brief (04) and should be started only after the P0 items here are measured.

## Repository

`/Volumes/Supercent/FierceTigerGameJam/FierceTigerGameJam` — Unity 6000.3.15f1, URP (`Assets/Settings/Mobile_RPAsset.asset`, `Mobile_Renderer.asset`), Android IL2CPP (`scriptingBackend Android: 1`), target SDK 36, ARM64+ARMv7 (`AndroidTargetArchitectures: 3`).

Physics project settings today: fixed timestep 0.02, max allowed timestep 0.333, solver iterations 6 / velocity 1, sleep threshold 0.005, contact offset 0.01, bounce threshold 2.

Test maps: `Assets/GameJam/Maps/test_03.json` (471 blocks) is the realistic stress map. `map_002_footprint_test.json` has 3285 blocks and is a footprint test, not a target — use it only to make pathological cases obvious.

## Step 0 — Baseline (mandatory before any change)

1. Set a Development Build with Autoconnect Profiler + Deep Profile off, build to the test device.
2. Capture: (a) selecting `test_03` and pressing Start Run — the build spike; (b) the biggest cascade you can trigger with explosive ammo; (c) 10 s of idle after the cascade.
3. Save captures to `ProfilerCaptures/` (folder exists, currently empty) with names `baseline_build.data`, `baseline_cascade.data`, `baseline_idle.data`. Note device model, GPU, Unity version in a `ProfilerCaptures/README.md`.
4. Record: worst frame ms in each capture, `Physics.Processing` ms, `Instantiate` / `AddComponent` counts, GC alloc per frame, draw calls / batches / SetPass.

Every later step must re-capture (a)–(c) and append the numbers to the README.

## P0 — Map build spike (`KnockdownLayoutMapAuthoring.BuildMap`)

### P0-1. The map is built twice per selection

`KnockdownLayoutMapAuthoring.OnEnable()` subscribes `mapSelection.SelectionChanged += HandleSelectionChanged`, which calls `BuildMap()` the moment a map is picked. `GameFlowController.HandleMapSelected` → `EnterAmmoPick()` then calls `mapBuilder.ClearMap()`, and `ConfirmAmmoPick()` calls `mapBuilder.BuildMap()` again. Net effect: full build + full clear + full build.

Fix: `GameFlowController` owns the build timing. Remove the `SelectionChanged` subscription and `HandleSelectionChanged` from `KnockdownLayoutMapAuthoring` (keep `mapSelection` only for `ResolveMapJson`). Keep `buildOnStart` for scenes opened standalone. **Verify in `Assets/GameJam/Scene/Gameplay.unity`** that the authoring component actually has `mapSelection` assigned — if it does not, this item is already moot and should be noted rather than changed.

### P0-2. Runtime `AddComponent` on every block

`WallBlockPhysicsSetup.PrepareBlocks()` runs `GetComponentsInChildren<KnockdownBlockAuthoring>` then, per block, `EnsureCollider` (adds `BoxCollider` if missing), `EnsureRigidbody` (adds `Rigidbody`), `EnsureKnockdownBlock` (adds `KnockdownBlock`). `BreakableWall.SpawnCell` repeats this per block via `PrepareBlock` when a wall comes apart.

Fix: bake the components into the block prefabs at author time.

- `BlockPrefabBuilder.BuildBlockPrefab` (editor): add `BoxCollider` sized from the mesh bounds, `Rigidbody` (`isKinematic = true`, `useGravity = false`, mass from spec, `interpolation None`, `collisionDetectionMode Discrete`), and `KnockdownBlock` with `startAsleep = true`. Rebuild prefabs with `Tools > Smashdown > Build Block Prefabs`.
- `WallBlockPhysicsSetup`: keep the API, but the `Ensure*` methods become `TryGetComponent` + `ApplyAuthoring` only; log a one-time warning if a component is missing (means a prefab was not rebuilt) and fall back to `AddComponent` so nothing breaks in the interim.
- Remove `[RequireComponent(typeof(Collider))]`/`[RequireComponent(typeof(Rigidbody))]` implications from the `BreakableBlock` remark once prefabs carry them (the remark currently explains why it is *not* there).

### P0-3. Mesh leak on rebuild

`BuildPanelMesh` does `Instantiate(source)` and `CombineToMesh` does `new Mesh` for every wall on every build. `ClearGeneratedBlocks` destroys the GameObjects only; `MeshFilter.sharedMesh` assets are not destroyed, so every Retry leaks one mesh per wall. (`BuildWeldedMesh` already destroys its temporaries — only the final meshes leak.)

Fix: keep `private readonly List<Mesh> builtMeshes` on the authoring component; add every mesh returned by `BuildPanelMesh` / `BuildWeldedMesh` to it; in `ClearMap()` and at the start of `BuildMap()` destroy them (`Destroy` in play mode, `DestroyImmediate` in editor) and clear the list. Also destroy in `OnDestroy`.

### P0-4. Redundant hierarchy walks in one build

`BuildMap` currently walks the generated tree several times: `physicsSetup.PrepareBlocks` (`GetComponentsInChildren<KnockdownBlockAuthoring>`), `SubscribeWalls` (`GetComponentsInChildren<BreakableWall>`), `ResolveStructureCenterLocalPosition` (`GetComponentsInChildren<Renderer>` + `Bounds.Encapsulate` per renderer). After P0-2 the first is cheap; replace the third with bounds accumulated from `PlacedBlock.LocalPosition` ± half block size during placement (no renderer walk). Keep `SubscribeWalls` but drive it from the `walls` list returned by `BuildPlacedBlocks` instead of a tree search.

### P0-5. Spread the build over frames (only if P0-1..4 leave a visible hitch)

Convert `BuildMap` into `BuildMapAsync(Action onComplete)` that yields every N spawned objects (start N = 40) using a coroutine on the authoring component, and have `GameFlowController.ConfirmAmmoPick` wait for completion before `runController.BeginRun()`. The synchronous `BuildMap()` stays for the editor button and `buildOnStart`. Hide the structure (or keep the ammo-pick UI up) until complete so the player never sees a half-built building.

## P1 — Physics during play

### P1-1. Body settings

`KnockdownBlock.ApplyRuntimeBodySettings()` sets `Interpolate` + `Continuous` on every block. Change to `None` + `Discrete`. (Also listed as Step 1 of Brief 02 — do it in whichever lands first, do not do it twice.)

### P1-2. Support cascade is O(N) per activation

`KnockdownBlock.ReleaseSupportedBlocksAbove()` iterates every sibling under the parent, calls `GetComponent<KnockdownBlock>` per child, allocates a `List`, sorts, and does this on **every** `Activate()`. In a cascade that is O(N²) with allocations.

Fix: a `StructureRegistry` (plain C# class owned by `KnockdownLayoutMapAuthoring`, or a component on the generated root) holding `Dictionary<Vector3Int, KnockdownBlock>` keyed by every cell a block covers (`GridPosition` + `LogicalSize`). Filled during `BuildMap` (blocks and walls both register; walls register all their cells) and updated by `BreakableWall.BreakUp` (unregister wall, register spawned blocks) and `BreakableBlock.Break` / `FallBreakZone.Despawn` (unregister). `ReleaseSupportedBlocksAbove` then looks up the cells directly above its footprint, walks up the column for `ColumnAbove`, no allocation, no `GetComponent`. Keep the old sibling scan as a fallback when no registry is present (blocks placed by hand in a test scene).

### P1-3. Projectile allocations

`GridKnockdownCannonProjectile.KnockBlocks`: `Physics.OverlapSphere` (allocates) and `new HashSet<KnockdownBlock>` per shot. Use `OverlapSphereNonAlloc` with a static `Collider[32]` and a static, cleared `HashSet`. `IgnoreSpawnOverlaps` likewise.

### P1-4. Contact callbacks

`BreakableBlock.OnCollisionEnter`, `BreakableWall.OnCollisionEnter`, `KnockdownBlock.OnCollisionEnter`, `FallBreakZone.OnCollisionEnter` all run for every contact of every block. They early-out on speed thresholds, which is fine, but each one calls `collision.GetContact(0)` or `collision.relativeVelocity` — cheap. The only thing to change: do not keep `allowCollisionCascade = true` **and** CCD on concrete (mass 2, activation velocity 2.5) — after P1-1 this is resolved. No other change unless the profiler shows `Physics.ProcessReports` as significant; if it does, confirm `Physics.reuseCollisionCallbacks` is on (default) and make sure no script with `OnCollision*` callbacks sits on debris chunks — `ShatteredBlock` lives on the debris root only and chunks should carry nothing but `MeshFilter`/`MeshRenderer`/`BoxCollider`/`Rigidbody` (verify in the `_Shattered` prefabs).

### P1-5. Settle detection

`LevelRunController.SettleThenJudge` polls `CalculateClearPercent` every 0.25 s — fine. `LevelProgressTracker.Update` samples every 0.25 s with a child walk + `TryGetComponent` ×2 per child. When the Game Feel doc's ≤ 0.15 s refresh is adopted, switch to event-driven counting: `BreakableBlock.Broken` already exists; add `BreakableWall.BrokenUp` and a `FallBreakZone` despawn event, decrement a counter, and keep the periodic recount only as a drift check every 1 s.

## P2 — Rendering

### P2-1. Quality / URP asset

`ProjectSettings/QualitySettings.asset` — `Mobile` level: `shadows: 2` (soft), `shadowResolution: 1`, `pixelLightCount: 2`, `antiAliasing: 0`, `vSyncCount: 0`. Check `Mobile_RPAsset.asset`: SRP Batcher on, shadow cascades 1, shadow distance small enough to cover only the platform, soft shadows off (hard is fine for toon), HDR off, MSAA off, render scale 1.0 (try 0.85 on low-end). Confirm `Mobile_RPAsset` is actually the asset assigned to the `Mobile` quality level (`m_RenderPipeline` / `customRenderPipeline` field), not `PC_RPAsset`.

### P2-2. Shader / batching

Blocks use Toony Colors Pro 2 materials. Verify in the Frame Debugger on device that block draws are SRP-batched (one "SRP Batch" node covering many blocks). If Toony shaders are not SRP-Batcher compatible on this URP version, regenerate them with the TCP2 shader generator with the URP/SRP Batcher template. Debris chunks: `shadowCastingMode = Off`.

### P2-3. Frame rate + textures

- Set `Application.targetFrameRate = 60` explicitly at bootstrap (`GameFlowController.Awake` or a small `Bootstrap` component). Android default is 30 and nothing in the project sets it.
- Texture import for `Assets/GameJam/Texture*`: Android override ASTC 6×6, mipmaps on, max size 1024 for block/wall textures. Wall panels rely on UV tiling (`BuildPanelMesh`), so wrap mode must be Repeat.

## P3 — Cleanup

### P3-1. Legacy destruction path

`SmashBlock`, `RuntimeGlassFracture`, `DemoLevelRuntimeBuilder`, `CannonProjectile`, `CannonFireController` (the non-`Grid*` versions) form an older demo path. `RuntimeGlassFracture.TryFracture` builds a new `Mesh` per shard at runtime and `Destroy(..., 8f)`s it. Check `Gameplay.unity` and all prefabs for references (`grep -l "SmashBlock\|DemoLevelRuntimeBuilder\|CannonFireController" Assets -r --include=*.unity --include=*.prefab`). If unreferenced, delete the five scripts and `SmashMaterialType.cs`; `CannonInputShooter` keeps only the `gridFireController` branch.

### P3-2. `SpinOnAxis`

`RotateAround` on the structure root moves hundreds of kinematic rigidbodies every frame the player drags. That is expected and only runs while dragging (speed is set to 0 on release), so leave it — but confirm `Physics.autoSyncTransforms` is **off** (default) so the transform moves are synced once per physics step, not per call.

## Acceptance criteria

1. `ProfilerCaptures/README.md` has baseline and after-P0, after-P1, after-P2 numbers for the same three captures on the same device.
2. Start Run on `test_03.json`: exactly one `BuildMap` per run (log count), zero `AddComponent` calls in the build frame, worst frame in the build capture reduced versus baseline (target: under 100 ms on the test device; record the actual value).
3. Five consecutive Retries: `Resources.FindObjectsOfTypeAll<Mesh>().Length` stable (no growth per retry).
4. Cascade capture: no GC allocation from `KnockdownBlock`, `GridKnockdownCannonProjectile`, `ShatteredBlock` per frame (GC Alloc column = 0 for those scripts).
5. All block rigidbodies `Discrete` / `None`.
6. Frame Debugger shows block draws in SRP batches; `targetFrameRate` is 60 on device.
7. Legacy scripts removed or documented as still referenced (with the referencing asset named).

## Out of scope

- Debris pooling and caps (Brief 02), wall grouping (Brief 01), map prefab bake (Brief 04).
- Any gameplay/feel tuning.
