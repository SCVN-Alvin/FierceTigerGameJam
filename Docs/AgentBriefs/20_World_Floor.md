# Task Brief 20 — A real floor: blocks shatter where they land, balls stop rolling on it

Branch **`Fix/WorldFloor`** from `main`, one-line commits, no body. House rules as always.

Repository: `/Volumes/Supercent/FierceTigerGameJam/FierceTigerGameJam` — paths relative to `Assets/GameJam/`. Reference project (read-only): `/Volumes/Supercent/luna_smashdown/unity_project/luna_smashdown/Assets/Supercent/SmashDown/` — cited below as `SmashDown/…`. **Do not copy code from it or touch it**; it is the behavioural reference.

## Diagnosis (verified — this is why the floor feels missing)

1. **The ball ignores the floor on purpose.** `GridKnockdownCannonProjectile.OnCollisionEnter` calls `IgnoreCollision(collision.collider)` for any collider without a `KnockdownBlock` — so the Ground is permanently ignored (per pooled instance, `Physics.IgnoreCollision` persists) and every miss flies until its lifetime kills it. Under the old flat 105 u/s shots that happened off-screen; with the Brief 18 arc the ball now visibly dives through the world.
2. **The Ground is too small for the arc.** The scene has a `Ground` (Plane at `(0,0,0)`, scale `(4,1,4)` → 40×40, MeshCollider + `FallBreakZone(Break, min 1.5)`, built by `Editor/PlayfieldBuilder.BuildGround`). The structure stands at z = 10, so the plane ends 10 units behind it (z = 20): overshot arcs and blocks knocked backwards or sideways leave its edge and fall forever.
3. **There is no out-of-bounds catch.** `FallBreakZone` supports `Despawn` on triggers, but nothing in the scene uses it — anything that escapes the floor lives below the world until the run ends.

The shatter machinery itself is fine: the existing `FallBreakZone` on the Ground already drives `BreakableWall.BreakUp` / `BreakableBlock.Break`, so blocks that do land on it come apart into the same debris as shot blocks.

## The reference, and where we deviate

`SmashDown/Main/Scripts/CannonKnockdownFloor.cs`: a kinematic box floor sized to the table plus padding, **friction physic material (static 0.9, dynamic 0.68)**, surface at the table's Y. `ProjectileMover.cs`: the ball **never ignores the floor** — it lands, rolls on that friction, and fades out when slow/sleeping or after a hit (`BeginDespawnWait`). Blocks reaching the floor get detected (`BlockFallFloorUtil`, tolerance 0.12) and fade out (`BlockRollingFloorFadeOut`).

Deviation, on purpose: our blocks **shatter** on the floor (the `FallBreakZone.Break` behaviour that already exists) rather than luna's rolling fade — that is the requested feel. What we take from the reference: the floor's friction values, the ball colliding with and resting on the floor, and the despawn-when-done pattern.

## A. The ball stops at the floor — `GridKnockdownCannonProjectile`

In `OnCollisionEnter`, **before** the block lookup:

```csharp
// The floor ends a flight instead of being ignored: the ball lands, rolls out its post-impact
// beat on the floor's friction, and goes home to the pool. Ignoring it was harmless when shots
// were flat and died off-screen; under the arc it reads as the world having no ground.
if (collision.collider.GetComponentInParent<FallBreakZone>() != null)
{
    if (!hasHit)
    {
        hasHit = true;
        sinceHit = 0f;
        projectileRigidbody.collisionDetectionMode = CollisionDetectionMode.Discrete;
    }
    return;   // no damage, no ignore - physics keeps the ball on the surface
}
```

- The existing `sinceHit` / `postImpactLifetime` path then despawns it; velocity is left alone so it visibly rolls to rest first. A ball that hits the floor **after** hitting a block (`hasHit` already true) just keeps its running timer.
- Do not `IgnoreCollision` floors anywhere; the block-miss ignore stays for everything else (activated blocks, the vehicle, debris).
- `FallBreakZone` gets one guard so a `Despawn`-mode zone cannot `Destroy` a pooled ball: in `Despawn(Collider hit)`, first check `GetComponentInParent<GridKnockdownCannonProjectile>()` and, when found, send it home through its own despawn (expose the private `Despawn()` as `public void ReturnToPool()` with a doc comment) instead of `Destroy`. `Break`-mode zones already leave the ball alone (it has no breakable component).

## B. The floor covers the play space — `Editor/PlayfieldBuilder`

`BuildGround` constants change (the builder already forces transform and components on re-run, so `Tools > Smashdown > Set Up Playfield` applies this to the scene):

- Position `(0, groundY, 20)`, scale `(10, 1, 10)` → a 100×100 plane spanning x ±50, z −30…70: the whole reachable arc space with margin, structure comfortably inside.
- Give it a physic material, per the reference numbers: new asset `Materials/PM_Ground.physicMaterial` — static friction **0.9**, dynamic **0.68**, bounciness 0, friction combine Average, bounce combine Minimum — created by the builder if absent and assigned to the Ground's `MeshCollider.material`. (The `M_Ground` render material is untouched.)
- The `FallBreakZone(Break, minimumImpactSpeed 1.5, affectDebris false)` setup stays exactly as it is.

## C. An out-of-bounds catch — same builder

New step `BuildOutOfBounds(float groundY)`: object `OutOfBounds` at `(0, groundY − 12, 20)` with a `BoxCollider` (`isTrigger`, size `(220, 10, 220)`), no renderer, plus `FallBreakZone` set to **`Despawn`, `affectDebris true`**. Anything that still finds an edge — debris included — is removed a beat after it falls out of sight, and pooled balls return through the §A guard. Idempotent like the rest of the builder (find-by-name, `SetIfEmpty` semantics for the zone fields it seeds).

## Acceptance

1. **Ball, miss over the top**: an arcing shot that clears the structure lands on visible ground behind it, rolls briefly on the friction, and despawns; twenty such shots in a row leave the pool intact (fire twenty more — no "pool empty" fallback instantiates, no leaked balls in the hierarchy).
2. **Ball, point blank at the floor**: aiming at the ground in front of the structure ends the shot at the floor without damage to anything and without the old fly-through.
3. **Blocks**: knock a structure apart so pieces fly forward, sideways and backwards — every piece that reaches the floor at ≥ 1.5 m/s shatters into debris where it lands (including behind the structure, where the old plane ended); gently settled pieces survive, as before.
4. **Nothing falls forever**: after a full clear of the biggest map, the hierarchy holds no balls, blocks or debris below the floor; the OutOfBounds volume is empty.
5. **No friendly fire from the zones**: the despawn volume never destroys the vehicle, the slingshot, or a pooled ball (the guard routes balls to the pool — verify by count).
6. **Builder**: `Set Up Playfield` on the current scene produces exactly this floor + volume and is a no-op when run again; a fresh scene gets the same from scratch.
7. Tutorial, cleared/fail flow, and damage numbers are untouched (floor contact deals no projectile damage; `FallBreakZone` impact-break thresholds unchanged).

## Out of scope

Luna's rolling-fade for blocks, floor impact VFX/smoke (`Floor_Smoke` exists in the reference — a later polish brief), dust decals, per-map floor sizes, changing `postImpactLifetime`, and any edit inside `/Volumes/Supercent/luna_smashdown`.
