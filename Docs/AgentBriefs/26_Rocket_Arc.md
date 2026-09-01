# Task Brief 26 — Projectile flight: no spin, and a guaranteed rocket arc to the tap

Branch **`Fix/RocketArc`** from `main`, one-line commits, no body.

Repository: `/Volumes/Supercent/FierceTigerGameJam/FierceTigerGameJam` — paths relative to `Assets/GameJam/`.

## Ground truth

- Brief 18 added a forward **tumble** (`projectileRigidbody.angularVelocity = tumbleAxis * tumbleRate` on launch) — the "all bullets are spinning" the user wants gone.
- The arc today is a fixed-speed ballistic solve (`projectileSpeed: 22` in the scene; `CannonAimController.GetFireDirection` → `CannonBallisticAimMath.TryGetLaunchDirection`). At fixed speed the shape varies: near targets get a flat-ish push, far ones a taller lob, and extreme aims can be unsolvable. The user wants the opposite guarantee: **every shot visibly arcs up, crests, and comes down onto the tapped point — a rocket flying to its target.**

## The change: solve for a fixed apex, not a fixed speed

Replace "given speed, find the angle" with "given the arc's shape, find the velocity". For muzzle `M`, target `T`, effective gravity `g`:

```
apexY  = max(M.y, T.y) + apexHeight            // apexHeight: serialized, default 2.5
hUp    = apexY - M.y                            // both strictly > 0 by construction
hDown  = apexY - T.y
vy     = sqrt(2 * g * hUp)
tUp    = vy / g
tDown  = sqrt(2 * hDown / g)
T      = tUp + tDown
vXZ    = (T.xz - M.xz) / T                      // horizontal velocity, aimed at the tap
launchVelocity = (vXZ.x, vy, vXZ.z)
```

Every target is solvable (no reachability rejections — delete Brief 18's rejection logging for range), every flight rises then falls, and the crest sits `apexHeight` above the higher of muzzle and target, so hitting the top of a tower still lobs over it.

Implementation:

1. **`CannonBallisticAimMath`** gains `public static Vector3 GetLobVelocity(Vector3 origin, Vector3 target, float apexHeight, float gravity)` — the closed form above, doc comment explaining the fixed-apex trade (speed varies with distance instead of shape varying). The old fixed-speed API stays for the legacy `CannonProjectile` demo path.
2. **Effective gravity, for snap**: at world gravity (9.81) the fixed-apex flight takes ~1.4 s regardless of distance — floaty. Serialized `gravityMultiplier` (default **2.5**) on `GridKnockdownCannonProjectile`: the launch solve uses `g = Physics.gravity.magnitude * gravityMultiplier`, and the projectile applies the extra pull itself each `FixedUpdate` while flying (`linearVelocity += Physics.gravity * (gravityMultiplier - 1) * fixedDeltaTime`, stopped on hit/despawn and reset on pool return). ~0.9 s flights, tight rocket-like arcs, and the arc math and the physics agree because both use the same `g`.
3. **Fire path**: `GridKnockdownCannonFireController` asks the aim controller for the **aim world point** (it already has `lastAimWorldPoint`; expose it or add `TryGetAimPoint(out Vector3)`), computes `GetLobVelocity(muzzle, aimPoint, apexHeight, g)`, and calls a new `projectile.Launch(Vector3 velocity, float lifetime)` overload (the old `direction * speed` overload delegates to it). `projectileSpeed` stays only as the legacy/no-aim fallback, with a tooltip saying so. `apexHeight` is serialized on the fire controller next to it.
4. **No spin, nose-first**: delete the tumble (field, launch line; the zero-out on pool return stays). Instead, while flying and moving faster than ~0.5 u/s the projectile aligns `transform.rotation = Quaternion.LookRotation(linearVelocity)` in `Update` — the rocket flies nose-first up the arc and tips over the crest. Stop aligning after the hit (physics owns it), reset on pool return. The ball meshes are radially symmetric so they simply stop spinning; the rocket meshes read correctly.
5. **Muzzle offset**: spawn at `muzzle + launchVelocity.normalized * muzzleSpawnOffset` and solve once more from the spawn point (same two-pass trick `GetFireDirection` already used).

## Acceptance

1. Tap the base, the middle, and the top of the tallest structure, plus the nearest allowed ground point and a far corner: every shot rises, crests visibly **above** both muzzle and target, and lands on the tap (five spread taps, impact matches).
2. No projectile rotates around its own axis in flight; the rocket ammunition flies nose-first along the arc; balls just travel.
3. Flight feels snappy (~1 s to mid-field at the defaults); tuning `apexHeight`/`gravityMultiplier` in the inspector changes shape/pace without breaking the landing point.
4. Damage, splash, pooling, per-type prefabs, floor stop (Brief 20) and the aim-plane validation all behave exactly as before; a pooled projectile fired twice shows no inherited gravity boost, spin or orientation.
5. The tutorial block is hittable; DemoGameplay still fires (legacy path untouched).
6. Domain-reload-off double-run clean.

## Out of scope

A trajectory preview line (natural follow-up now the path is deterministic — note it, don't build it), barrel pitch matching the launch angle, per-ammunition arc shapes, thrust/smoke trails.
