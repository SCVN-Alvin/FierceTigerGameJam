# Task Brief 22 — Dragging the map must not knock the building down

Branch **`Fix/DragCollapse`** from `main`, one-line commits, no body.

Repository: `/Volumes/Supercent/FierceTigerGameJam/FierceTigerGameJam` — paths relative to `Assets/GameJam/`.

## The bug, diagnosed (verified in the code)

Repro: shoot → hit a block → drag to rotate the map → **every** block starts falling.

- Blocks are **kinematic until activated**: `KnockdownBlock` spawns with `isKinematic = true` and only `Activate()` (hit, hard neighbour contact over `collisionActivationVelocity`, or support release) makes one dynamic.
- The drag rotates the structure with **`transform.RotateAround` in `Update`** (`SpinOnAxis`, driven by `StructureRotateController.SetSpeed`). That teleports every collider's pose once per rendered frame, outside the physics step.
- Before the first shot that is harmless — everything is kinematic and moves as one. After a hit there are **dynamic** blocks and debris in contact with kinematic ones, and the teleporting poses do two bad things at once: contacts resolve with huge fake relative velocities (tripping `collisionActivationVelocity` on block after block), and dynamic bodies do not follow a parent transform, so the structure rotates out from under them and the support-release cascade (`ActivateFromSupportRelease`) finishes the job. Result: the whole building activates and falls.

The wanted behaviour, in the user's words: shoot → hit → physics falling happening → drag → **physics still happens normally** — pieces already falling keep falling (and get carried/pushed realistically), untouched blocks stay put.

## The fix — rotate through physics, not past it

`SpinOnAxis` drives a **kinematic `Rigidbody`** on the structure root with `MovePosition`/`MoveRotation` in **`FixedUpdate`**, which is the rigidbody equivalent of `RotateAround` and gives every contact a real surface velocity: kinematic children ride along as before, dynamic blocks resting against the structure are carried by friction instead of shoved by teleports, and activation thresholds only see genuine speeds.

1. **`SpinOnAxis`**:
   - Move the rotation from `Update` to `FixedUpdate`. Compute the step as today (`speed * Time.fixedDeltaTime` around the same pivot/axis), then:

```csharp
Quaternion step = Quaternion.AngleAxis(angle, axis);
if (body != null)
{
    // RotateAround, but through the physics engine: kinematic MovePosition/MoveRotation sweep
    // the colliders, so a dynamic block resting on the structure is carried by friction with a
    // real contact velocity instead of being teleported into its neighbours - which is exactly
    // what made a drag read as an explosion.
    body.MovePosition(pivot + step * (body.position - pivot));
    body.MoveRotation(step * body.rotation);
}
else
{
    transform.RotateAround(pivot, axis, angle);   // edit-mode preview and unwired test scenes
}
```

   - `body` is a `[SerializeField] Rigidbody` resolved in `Awake` (`GetComponent`), created-if-missing by the builder (§2), `isKinematic = true`, `interpolation = Interpolate` (the visual smoothness `Update` used to give), gravity off.
   - `ResetRotation()` keeps teleporting (direct `transform` write is fine — it runs between runs on a still structure; also sync `body.position/rotation` after it so the next `Move*` does not sweep across the map).
2. **`StructureRotateController`** — unchanged; it only sets speed.
3. **Structure root** — the object `SpinOnAxis` sits on gets the kinematic `Rigidbody`. Compound-collider note: the root having a `Rigidbody` makes any child collider **without** its own rigidbody part of the root's compound — every block already has its own `Rigidbody`, and the generated container objects carry none, so nothing changes hands; assert in the builder (log any child collider that would be captured) rather than assuming.
4. **Wiring** — extend whichever builder owns the playfield (`Editor/PlayfieldBuilder` or the map authoring setup, wherever `SpinOnAxis` is ensured today) to add and configure the `Rigidbody` with `SetIfEmpty` semantics. No threshold tuning: `collisionActivationVelocity` stays as it is — after this fix the velocities it sees are real.

## Acceptance

1. Fresh structure, no shots: drag left/right through full turns — nothing activates, nothing falls, rotation feels as before.
2. Shoot once, knock a few blocks loose, then drag hard both directions **while pieces are mid-air**: the untouched part of the building stays standing; falling pieces keep falling and land plausibly; pieces resting on the structure are carried with it rather than slipping through it.
3. Drag during a big cascade (concrete tower map): no explosion of extra activations attributable to the drag — the collapse looks the same dragged or not.
4. A dynamic block sitting ON a kinematic ledge is carried when the structure turns (friction), and can still topple off the edge — that is physics happening normally, not the bug.
5. The ball resting momentarily on the structure during its post-impact beat is swept correctly (no tunnelling at drag speed).
6. `ResetRotation` between runs still squares the structure; tutorial unaffected; Android build shows no measurable physics cost from the kinematic body (one extra rigidbody).
7. Domain-reload-off double-run clean.

## Out of scope

Camera-orbit instead of structure-rotation (bigger redesign), clamping drag speed, freezing physics during drags (explicitly not wanted), touch-input changes, and any activation-threshold retuning.
