> **Completed and merged.** Originally numbered 24; renumbered when a later set of briefs
> reused that number. Kept for the record - the work described here is already on `main`.

# Task Brief 24 — The drag orbits the camera and cannon; the structure never moves

Branch **`Fix/CameraOrbit`** from `main`, one-line commits, no body. House rules as always.

Repository: `/Volumes/Supercent/FierceTigerGameJam/FierceTigerGameJam` — paths relative to `Assets/GameJam/`.

## Why this replaces Brief 22

Brief 22 tried to fix "drag after a shot collapses the building" by rotating the structure *through* physics
instead of past it. Two attempts failed, and the reason is structural rather than a detail either got wrong:

1. **Brief 22 as written could not work.** It put a kinematic `Rigidbody` on the structure root and drove it
   with `MovePosition`/`MoveRotation`. Every collider under that root is owned by a *child* rigidbody, so the
   root body owns no shapes — no contacts, no friction, no surface velocity. The stated mechanism was
   impossible. (Verified: `structureRoot` in `Gameplay.unity` carries only `Transform` + `SpinOnAxis`.)
2. **Sweeping the blocks' own bodies (the second attempt, currently on `main`) fixes the physics but not the
   feel.** Only *un-activated, kinematic* blocks can be swept; anything already knocked loose is dynamic and
   must be left to physics. So after a shot the intact part turns and the damaged part stays — the player
   reads this as "the structure is stuck". That is inherent to moving the structure at all, not a bug in the
   sweep.

The fix is to stop moving the structure. If the camera and the cannon orbit it instead, the physics scene is
never disturbed: no teleports, no sweeps, no drift, no fake contact velocities, no activation cascades to
suppress, and loose blocks and debris simply keep falling. The bug class disappears rather than being
mitigated.

Brief 22 listed this as out of scope ("bigger redesign"). It has been reopened deliberately.

## Ground truth (verified in the scene — confirm before relying on it)

| Thing | Where |
|---|---|
| Orbit pivot | `Structure Center`, child of `structureRoot`, local `(0, 0.375, 0)` → **world `(0, 0.375, 10)`** |
| `structureRoot` | child of `structureSetup`, at `(0, 0, 10)`; children `GeneratedLayoutBlocks`, `Structure Center` |
| Camera | `Main Camera` under `CameraController`, which sits at `(0, -0.26, 1.86)` |
| Cannon | the `Slingshot` prefab instance at `(0, 0, -4.33)` |
| Aim plane | `AimPlaneAnchor` under `World` at `(0, 2.89, 2.783)` |
| Backdrop | built by `PlayfieldBuilder.BuildBackdrop` as flat tiles placed with `Quaternion.identity` — **it faces one way only** |
| Ground | 100×100 plane centred at `(0, 0, 20)` |

## 1. The orbit rig

A new `CameraOrbit` component and one pivot object:

- Pivot GameObject named `OrbitPivot`, positioned at the **world position of `Structure Center`**, rotation
  identity, parented so it is *not* a child of `structureRoot` (the structure must be able to rebuild without
  disturbing the orbit). Under `=====GAMEPLAY=====`, beside `World`, is the natural home.
- Reparented under it, preserving world pose: `CameraController`, the `Slingshot` instance, `AimPlaneAnchor`,
  and the `Backdrop` root.
- `Backdrop` orbits deliberately: it is flat tiles facing one direction, so a camera that swung around a
  static backdrop would see it edge-on and then from behind. It has no physics, so carrying it costs nothing
  and it always sits behind the structure. Comment this at the site — it is the non-obvious member of the set.
- `Ground` stays put. It is symmetric about the orbit and large enough that the camera stays over it.

`CameraOrbit` exposes `SetSpeed(float)` and `ResetRotation()`, mirroring `SpinOnAxis`'s surface so the drag
controller needs no new concepts, and rotates the pivot about `Vector3.up` in `Update` (a plain transform
write is correct here — nothing under the pivot has physics that cares).

## 2. The drag drives the orbit

`StructureRotateController` currently calls `SpinOnAxis.SetSpeed`. Point it at `CameraOrbit` instead.
**Invert the sign** so a drag still turns the world the same way on screen: orbiting the camera left is the
visual equivalent of rotating the structure right. Verify the direction rather than assuming, and say which
way you settled on.

`GameFlowController.ResetPlayfield` calls `structureSpinner.ResetRotation()`; it must reset the orbit instead,
so each run starts from the authored viewpoint.

## 3. Retire the sweep

Once the orbit works, revert `SpinOnAxis` to its pre-Brief-22 form — a plain `RotateAround` component — and
leave it undriven. Nothing rotates the structure any more, so the sweep, the interpolation toggle and the
collider audit all have no purpose. **Do this as its own commit**, so the reasoning stays legible in history.
Do not delete the file: `DemoLevelSceneBuilder` still adds it and the demo scenes still spin their structures
with it.

## 4. Wiring

Extend `Editor/PlayfieldBuilder` (it already owns the scene's playfield furniture) with the pivot creation and
the reparenting, idempotently: find-by-name, reparent only what is not already under the pivot, `SetIfEmpty`
the component references. Re-running must be a no-op. Do not hand-edit `Scene/Gameplay.unity`.

## 5. Acceptance

1. Fresh structure, no shots: drag left/right through full turns — the view orbits smoothly, the structure is
   visibly motionless, nothing activates or falls.
2. **The bug**: shoot, knock blocks loose, then drag hard both directions while pieces are mid-air and while
   they settle. The building behaves exactly as it would undragged — nothing extra activates, nothing appears
   stuck, loose pieces fall normally.
3. Aim and fire from several orbit angles: the shot leaves the muzzle in the aimed direction and lands where
   tapped. The ballistic solver works in world space, so this must hold at any angle — verify at 0°, 90° and
   180°.
4. The backdrop stays behind the structure at every angle; the ground fills the frame; no world edge visible.
5. `ResetPlayfield` between runs returns the view to the authored angle; the tutorial is unaffected.
6. Builder re-run is a no-op; domain-reload-off double-run is clean.
7. Android: orbiting is one transform write per frame, so frame time during a drag should *improve* against
   the sweep it replaces. Worth a profile since Brief 22 raised the question.

## Out of scope

Clamping or easing the orbit, orbit-angle limits, moving the ground, replacing the flat backdrop with a
skybox, soft-locking input during flight (considered and rejected — the bug happens after the ball lands),
touch-input changes, and any activation-threshold retuning.
