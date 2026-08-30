# Task Brief 18 — Projectile feel: slower shots that fly a real parabola

Branch **`Fix/ProjectileArc`** from `main`, one-line commits, no body. Small and mostly tuning — the machinery already exists; the numbers hide it.

Repository: `/Volumes/Supercent/FierceTigerGameJam/FierceTigerGameJam` — paths relative to `Assets/GameJam/`.

## Ground truth (verified — read these first)

- The projectiles already fly under gravity: both bullet prefabs' Rigidbodies have `useGravity: 1`, zero drag, ContinuousDynamic collision; `Launch(direction, speed, lifetime)` sets `linearVelocity = direction * speed`.
- The aim already solves ballistics: `CannonAimController.GetFireDirection` calls `CannonBallisticAimMath.TryGetLaunchDirection(origin, target, speed)` (closed form on `Physics.gravity`, with a simulation fallback), and even re-solves once for the muzzle spawn offset. Aim rejections (`AimRejectReason`, `LogAimRejected`) already exist for unreachable targets.
- The reason everything looks fast and dead straight is one number: **`projectileSpeed: 105`** on the `GridKnockdownCannonFireController` in `Scene/Gameplay.unity` (and once more in `Scene/DemoGameplay.unity`). At 105 u/s the solver's answer is near-flat and the flight lasts a fraction of a second — the parabola is there, just invisible.

## The change

1. **Speed.** Drop `projectileSpeed` to **22** in both scenes, and change the field's initializer in `GridKnockdownCannonFireController` from `105f` to `22f` so a fresh scene matches. 22 is the starting point, not a law — tune per §Acceptance 2/3 and record the final number in the run notes. Leave `projectileLifetime` at 5 (a 22 u/s lob to the structure lands well inside it) and `muzzleSpawnOffset` as is.
2. **The low arc, on purpose.** Verify `TryGetLaunchDirection` returns the *lower-pitch* root of the two ballistic solutions (the direct lob, not the mortar drop). If it turns out to pick the high root at low speeds, prefer the root with the smaller angle against the horizontal and say so in a comment; do not add a toggle.
3. **Reachability guard.** At low speed, far or high aims can have no solution and the shot is refused (existing `LogAimRejected` path — today it only logs in the editor). Tune the speed so the highest block of the tallest current map is comfortably solvable from the muzzle; the acceptance test pins it. If a map ever outgrows the speed, the visible symptom is "taps at the top do nothing", so add one development-build log line to the rejection path naming the reason — players never see it, the next map author does.
4. **Feel (small, still in scope).** On `Launch`, give the ball a forward tumble so the slower flight reads as heavy rather than floaty: `projectileRigidbody.angularVelocity = Vector3.Cross(Vector3.up, velocity).normalized * (speed / ballRadius)` — radius from the SphereCollider, factor clamped so it does not strobe; reset on pool return (where velocity and the level look already reset). Purely visual — the physics of impact are untouched.

Nothing changes in `ResolveDamage`, splash, pooling, the level-look meshes, or the fire events. `CannonBallisticAimMath` itself should not need edits beyond (2) — if the sim fallback proves jittery at 22, tighten its step in that file rather than special-casing callers.

## Acceptance

1. A shot at a mid-height block leaves the muzzle visibly slower, climbs, and comes down on the aimed block in a clear arc; the impact point still matches the tap (the solver, not the eye, guarantees this — verify on five spread taps).
2. The top row of the tallest map in `MapConfig` is still hittable from the cannon; no rejected-aim logs during a normal clear of any current map.
3. Aiming at the closest allowed point still works (no overshoot from the arc at minimum range).
4. The tutorial block is still trivially hittable; the fail-screen continue and full-clear finishes behave as before (flight time grew — the settle logic keys on stillness, not on a stopwatch, so no tuning needed there; confirm the end-of-run settle still triggers).
5. DemoGameplay scene fires with the same feel; a pooled ball reused across shots does not inherit spin or velocity from its last flight.

## Out of scope

Wind, drag curves, per-bullet speeds (one global speed stays until design asks), a trajectory preview line, aligning the barrel's visual pitch to the ballistic solution (the barrel tracks the aim point as before), and any damage change.
