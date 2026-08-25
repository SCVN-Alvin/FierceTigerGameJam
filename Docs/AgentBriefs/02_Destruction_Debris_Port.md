# Task Brief 02 — Block destroy / shatter / flying debris: port the luna_smashdown approach

## Goal

Make block destruction in `FierceTigerGameJam` cheap enough for a mid-range Android phone during a full cascade, using the patterns already proven in `luna_smashdown`: no `Instantiate` at break time, a hard cap on live debris, debris that ignores itself, and bodies configured for many simultaneous rigidbodies. Keep the current material/HP design and the visual "shrink away" despawn.

## Repositories

- Target: `/Volumes/Supercent/FierceTigerGameJam/FierceTigerGameJam` — scripts under `Assets/GameJam/Scripts/Gameplay/`
- Reference (read-only): `/Volumes/Supercent/luna_smashdown/unity_project/luna_smashdown/Assets/Supercent/SmashDown/Main/Scripts/`

Do not edit anything in `luna_smashdown`.

## How luna_smashdown does it (reference summary)

Read the reference files named below before implementing; the summary is here so the agent knows what to look for.

**Block lifecycle — `BlockPhysicsController.cs`.** Two states, `Prepared` and `Simulating`. Prepared bodies are `isKinematic = true`, `useGravity = false`, `interpolation = None`, `sleepThreshold = 0.005`. On release: `isKinematic = false`, `useGravity = true`, `collisionDetectionMode = Discrete`, `interpolation = None`, `solverIterations = 6`, collider gets a shared "released" `PhysicMaterial`, `WakeUp()`. The projectile owns hit detection (`ProjectileMover.OnCollisionEnter` → `ReleaseForProjectileImpact`). Impulse = `speed × 0.85`, capped at 42; first hit on a kinematic body gets ratio 1.0, an already-simulating body gets 0.2. After the impulse the velocity is **clamped** (horizontal 8.5, vertical 1.35) so blocks do not fly off-screen.

**Support cascade — `BlockStackWakeController.cs`.** No grid, no raycasts: an array sorted by world Y; "blocks above" is a forward scan. A block is supported if the vertical gap is in `[-0.08, 0.16]` and footprint overlap ratio ≥ 0.08. One projectile hit runs a cluster pass (axis gaps ≤ 0.3), a rear-column pass, then support cascade depth 2. All synchronous, O(N) per pass.

**Debris — `BlockBreakDebrisController.cs`, `BlockDebrisFragment.cs`, `BlockDebrisHierarchyBuilder.cs`.** Every block prefab carries a disabled `DebrisRoot` with its fragments already inside (kinematic, collider off, `SetActive(false)`). On break: hide the intact renderer, disable the intact collider, `DebrisRoot.SetParent(null, true)`, enable fragments, `ActivatePhysics(inheritVelocity × 0.42)`, `ApplyBreakImpulse(burstCenter, 0.78)` with upward bias 0.32, horizontal jitter 0.38, torque 0.3. **Nothing is instantiated at break time.** Fragments always use `BoxCollider` (`MeshCollider` is stripped in `EnsureBoxCollider`), live on layer 9, and `Physics.IgnoreLayerCollision(9, 9, true)` is set once at init (`BlockDebrisActivePool.Init`). Shared debris `PhysicMaterial` (friction 0.52/0.4, bounce 0.12), drag 0.32/0.32.

**Settle + despawn — `BlockDebrisFadeSession.cs`, `BlockDebrisPhysicsProfile.cs`, `BlockDebrisActivePool.cs`.** Settle 0.4 s (during which `DampGroundedMotion` multiplies horizontal velocity by 0.88 and angular by 0.86 per frame once |vy| ≤ 0.35), then fade 0.6 s. Hard caps: `MAX_ACTIVE_DEBRIS_COUNT = 12` sessions, `MAX_ACTIVE_FRAGMENT_COUNT = 96` fragments; registering a new session releases the oldest until under the limit. Update-driven state machine, no coroutines, no allocations. Fade is done by swapping to an alpha shader on a per-fragment material instance created once — **we will not copy this part** (opaque toon shaders; keep the existing scale-down).

**Projectile — `ProjectileMover.cs`, `ProjectilePool.cs`.** Queue pool sized to the shot budget. `ContinuousDynamic` in flight, switched to `Discrete` on first contact. `SphereCastNonAlloc` every `FixedUpdate` to prevent tunnelling. After the hit: 1 s wait → 0.4 s fade → return to pool.

**VFX — `CannonKnockdownVfxSpawner.cs`.** Static ring-buffer pools per effect (6 for smoke types, 12 for bursts); pool size is the hard cap on concurrent instances; a slot still playing is stopped and reused.

## Current state in FierceTigerGameJam (what the port replaces)

| Concern | Current code | Problem |
|---|---|---|
| Break | `BreakableBlock.SpawnDebris()` → `Instantiate(shatteredPrefab, ...)` then `ShatteredBlock.Launch(...)`; block `Destroy(gameObject)` | Instantiating 8–12 rigidbodies per block in one frame; a wall breakup can break several blocks in the same frame |
| Debris lifetime | `ShatteredBlock` (`lifetime = 2`, `shrinkDuration = 0.4`, `freezeWhileShrinking`) then `Destroy` | No cap on live debris; no ground damping; every chunk keeps default collider material |
| Debris collisions | Chunks on default layer | Debris collides with debris |
| Body settings | `KnockdownBlock.ApplyRuntimeBodySettings()` sets `interpolation = Interpolate`, `collisionDetectionMode = Continuous` on **every** block | luna's source comments say CCD + Interpolate on many bodies "melts frames"; only the projectile needs CCD |
| Projectile | `GridKnockdownCannonFireController.Fire()` instantiates `CannonBall_Grid` per shot; `destroyOnImpact = true` | No pooling; no life after impact (Game Feel doc wants 0.25–0.60 s) |
| Shot impulse | `GridKnockdownCannonProjectile.KnockBlocks` — `impactForce = 18`, radius 0.65, `Physics.OverlapSphere` + `new HashSet` per shot | No velocity clamp after impulse; allocations per shot |
| VFX | `BreakableBlock.breakEffectPrefab` → `Instantiate` per break (if assigned) | No pool, no cap |
| Debris prefab build | `BlockPrefabBuilder.BuildShatteredPrefab` (editor) builds `<block>_Shattered.prefab` with `ShardChunks` grid (2×2×2 brick, 3×3×1 glass, 2×2×2 concrete, 3×2×2 brick_2x1), chunk meshes saved as sub-assets, `ShatteredBlock` on root | Fine as-is; it is the source for the pool |

## Required changes

Implement in this order; each step is independently shippable.

### Step 1 — Body settings (2 lines, biggest win)

`KnockdownBlock.ApplyRuntimeBodySettings()`:

```csharp
blockRigidbody.interpolation = RigidbodyInterpolation.None;
blockRigidbody.collisionDetectionMode = CollisionDetectionMode.Discrete;
```

Keep `ContinuousDynamic` on `GridKnockdownCannonProjectile` only. If tunnelling of the ball through thin glass shows up in testing, port `ProjectileMover.ClampToNearestFrontBlock` (a `SphereCastNonAlloc` per `FixedUpdate`) rather than turning CCD back on for blocks.

### Step 2 — Debris layer + shared physic material

- Add a `Debris` layer in `ProjectSettings/TagManager.asset` (pick a free index; do not reuse 9 blindly).
- New static `DebrisPhysicsProfile` (mirror of luna `BlockDebrisPhysicsProfile`): shared `PhysicsMaterial` (Unity 6 name for `PhysicMaterial`) friction 0.52/0.4, bounce 0.12, combine Min/Average; drag 0.32/0.32; `DampGroundedMotion(Rigidbody)` with the 0.35 / 0.88 / 0.86 constants; `Init()` that calls `Physics.IgnoreLayerCollision(debrisLayer, debrisLayer, true)` once.
- `BlockPrefabBuilder.CreateChunkObject` (editor) sets chunk layer to `Debris` and assigns the shared physic material so prefabs are correct at author time; `ShatteredBlock.Awake` also enforces it at runtime for safety.

### Step 3 — Pool + cap for `_Shattered` prefabs

New `ShatteredBlockPool` (scene singleton or static, initialised by `GameFlowController` at run start):

- One `Queue<ShatteredBlock>` per shattered prefab, pre-warmed from `BlockDatabase` entries (read each block prefab's `BreakableBlock.shatteredPrefab`; warm 4 per type, configurable).
- `Rent(prefab, position, rotation, parent)` → returns an inactive instance reset to its authored chunk local poses/scales (`ShatteredBlock` must cache the authored local position/rotation/scale of each chunk in `Awake` and expose `ResetChunks()`).
- `Return(ShatteredBlock)` → chunks kinematic, colliders off, `SetActive(false)`, back to the queue. Replace `Destroy(gameObject)` in `ShatteredBlock.Update` with `ShatteredBlockPool.Return(this)`.
- Active cap, mirroring `BlockDebrisActivePool`: `maxActiveSessions = 12`, `maxActiveChunks = 96` (serialized so design can tune). `Rent` first returns the oldest active sessions until under the limit. Order by a monotonically increasing activation counter, not `Time.time`.
- Overflow policy when the pool for a type is empty and the cap is not reached: instantiate one more and log once in development builds.

`BreakableBlock.SpawnDebris()` becomes: `ShatteredBlock debris = ShatteredBlockPool.Rent(shatteredPrefab, transform.position, transform.rotation, transform.parent); debris.Launch(...)`. Keep the `Launch` signature.

### Step 4 — `ShatteredBlock` settle behaviour

- Add a settle phase before shrink: for the first `settleSeconds = 0.4` call `DebrisPhysicsProfile.DampGroundedMotion` on each chunk in `FixedUpdate`.
- Keep `lifetime` / `shrinkDuration` / `freezeWhileShrinking` as they are (scale-down despawn stays; do not port the alpha fade).
- Cache `Rigidbody[]` and `Collider[]` per chunk in `Awake`; no `TryGetComponent` in `Update`.
- Chunk renderers: `shadowCastingMode = Off`, `receiveShadows = false` (set in `BlockPrefabBuilder.CreateChunkObject`).

### Step 5 — Velocity clamp after impulse (readability, from luna)

In `KnockdownBlock.Knock(...)` after `AddForceAtPosition`, clamp: horizontal speed to `maxKnockHorizontalSpeed` (start 8.5), vertical to `maxKnockVerticalSpeed` (start 1.35 upward). Expose both on `KnockdownBlockAuthoring` so material specs in `BlockPrefabBuilder.Specs` can set them (glass may want higher). This is what keeps "a small chain of destruction the player can understand" from becoming blocks leaving the screen.

### Step 6 — Projectile pool + life after impact

- `GridKnockdownCannonFireController`: replace `Instantiate(projectilePrefab, ...)` with a `ProjectilePool` (queue, warm to `BulletPickLimit` from `LevelRunController`, fallback 10). `CreateDefaultProjectile` stays as the no-prefab fallback and is not pooled.
- `GridKnockdownCannonProjectile`: `destroyOnImpact` → `postImpactLifetime` (serialized, default 0.4 s, Game Feel doc range 0.25–0.60). On first hit: `collisionDetectionMode = Discrete`, keep flying/bouncing, `hasHit = true` prevents a second `KnockBlocks`. After `postImpactLifetime` → return to pool (reset `hasHit`, velocity, CCD back to `ContinuousDynamic`, clear `Physics.IgnoreCollision` pairs it set — track them in a list).
- `KnockBlocks`: `Physics.OverlapSphereNonAlloc` into a static `Collider[32]`; replace the per-shot `HashSet` with a static one cleared per shot.

### Step 7 — VFX pool (only if `breakEffectPrefab` is used)

If block prefabs have `breakEffectPrefab` assigned (verify in the Blocks prefabs), replace the `Instantiate` in `BreakableBlock.SpawnBreakEffect` with a ring-buffer pool per prefab (size 6) modelled on `CannonKnockdownVfxSpawner.GetPooledInstance`: reuse the slot even if still playing. If nothing is assigned yet, add the pool with an empty registry and leave a TODO for the VFX task.

## Constraints

- Keep `BreakableBlock`/`BreakableWall` HP and material-damage design untouched; this task is about cost, not feel.
- Keep `LevelProgressTracker.CountRemainingBlocks` correct: pooled debris must keep the `ShatteredBlock` component on the root so it is still excluded from the remaining count, and must stay parented under the generated root while active (it is today, via `transform.parent`). When returned to the pool, reparent under the pool root so `ClearMap` does not destroy pooled instances.
- `ClearMap()` / `GameFlowController.TearDownRun()` must return all active debris and projectiles to their pools.
- No allocations in `Update`/`FixedUpdate` paths of `ShatteredBlock`, `KnockdownBlock`, `GridKnockdownCannonProjectile` (verify with the Profiler's GC Alloc column).
- Editor-only code stays under `#if UNITY_EDITOR`.

## Acceptance criteria

1. Firing into `test_03.json` (471 blocks) and breaking a wall shows **no `Instantiate` calls** for debris in the Profiler after warm-up; `ShatteredBlockPool` active count never exceeds 12 sessions / 96 chunks.
2. Debris chunks never generate contacts with other debris chunks (Physics Debugger or `Physics.GetIgnoreLayerCollision`).
3. All block rigidbodies report `Discrete` / `None`; the projectile reports `ContinuousDynamic` before impact and `Discrete` after.
4. Projectile survives `postImpactLifetime` after the first hit and can visibly bounce into a second block; second contact does not deal damage.
5. Cascade of a 5-block-tall column: no block exceeds the horizontal clamp speed (log in development build).
6. Retry the same map five times in a row: pool sizes stable, no leaked `ShatteredBlock` or projectile objects in the hierarchy, no growth in `Mesh`/`Material` counts (Memory Profiler or `Resources.FindObjectsOfTypeAll<Mesh>().Length`).
7. Frame time on the test device during the biggest cascade of `test_03.json` improves versus the baseline capture taken before Step 1 (record both in `ProfilerCaptures/`).

## Out of scope

- Steel material, crack state, nudge-without-damage (design decision pending).
- Alpha-fade despawn, camera shake, SFX, haptics.
- Wall grouping (Task Brief 01), map build cost (Task Brief 03), prefab bake (Task Brief 04).
