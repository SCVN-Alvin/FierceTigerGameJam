# Task Brief 13 — Bullet models: each ammunition type fires its own projectile, levels swap the mesh

## Goal

Hook the artist's projectile prefabs into the config so **the type the player has equipped is the ball they see fly**: Rock fires `CannonBall_01`, Cannon Ball fires `CannonBall_02`, and the bullet's **level** picks the right `…LV1/LV2/LV3` mesh child inside the prefab. Switching ammunition (in the garage, on the AmmoPick screen, or mid-run when one type runs dry) changes the next shot's look with no other wiring.

Decisions already made (do not re-open):

- **Mapping**: `Prefabs/Bullets/CannonBall_01.prefab` → `rock_type`, `Prefabs/Bullets/CannonBall_02.prefab` → `cannon_type`. Both already carry `GridKnockdownCannonProjectile` plus `Boom0N_LV1/LV2/LV3` mesh children, authored by the artist. `Prefabs/Bullets/CannonBall.prefab` still runs the **legacy** `CannonProjectile` script and is **left alone** (old demo scenes use it); it is not part of the config.
- **One prefab per type, one mesh child per level** — the prefab is chosen by the resolved ammunition at fire time, the level child by its level. No per-level prefab field on `BulletDefinition`; the `LV` children are the levels.
- The artist's prefabs come with their own child active-states (in `_02`, LV2/LV3 are switched off by hand); the projectile normalises this at spawn, so the authored on/off states in the prefab stop mattering.
- The pool becomes **per-prefab**: rocks and cannon balls must not come back out of the pool wearing each other's look.

House rules (unchanged): idempotent builders with `SetIfEmpty`; subscribers unsubscribe in `OnDisable`; `Try*` returns bool; XML doc comments say *why*; no LINQ in runtime paths (the level-child scan runs per spawn — keep it allocation-free). Git: branch **`Feature/BulletModels`** from `main`, one-line commits, no body.

## Repository

`/Volumes/Supercent/FierceTigerGameJam/FierceTigerGameJam` — paths relative to `Assets/GameJam/`.

| Existing | Role | Touched |
|---|---|---|
| `Scripts/Gameplay/Combat/BulletDefinition.cs` | id, levels (damage tables, icon) | gains `projectilePrefab` |
| `Scripts/Gameplay/Cannon/GridKnockdownCannonProjectile.cs` | flight + damage; `SetAmmunition(bullet, level)` from the fire controller; `SetDamageMultiplier`; reset on pool return | gains the level-look swap |
| `Scripts/Gameplay/Cannon/GridKnockdownCannonFireController.cs` | resolves the ammunition (`bulletLoadout.Selected` if it has rounds, else first available), spends, spawns via pool or `Instantiate(projectilePrefab)`, `projectilePool.Warm(budget)` in its prewarm | resolves the **prefab** per shot |
| `Scripts/Gameplay/Cannon/ProjectilePool.cs` | single-prefab pool: serialized prefab, `Warm(size)`, `Rent(pos, rot, parent)`, `Return`, `ReturnAll`, `HasPrefab` | keyed per prefab |
| `Prefabs/Bullets/CannonBall_01.prefab`, `CannonBall_02.prefab` | the artist's projectiles (root: MeshFilter/Renderer, SphereCollider, Rigidbody, the script; children `Boom01_LV1..3` / `Boom02_LV1..3`) | referenced from the definitions; their script fields checked (§3) |
| `Config/Bullets/Rock.asset`, `Cannon.asset` | the two definitions | prefab wired |
| `Editor/BulletDefinitionBuilder.cs` | creates/fills the definitions | fills `projectilePrefab` |
| `Scene/Gameplay.unity` | `fireController.projectilePrefab` currently points at `CannonBall_02` | stays, as the fallback for an unconfigured bullet |

## 1. `BulletDefinition`

```csharp
[Tooltip("The projectile this ammunition fires. Its LV1/LV2/LV3 children are the level looks; "
       + "the projectile enables the one for the level it was fired at. Left empty, the fire "
       + "controller's own prefab is used, so an unconfigured bullet still shoots.")]
[SerializeField] private GridKnockdownCannonProjectile projectilePrefab;

public GridKnockdownCannonProjectile ProjectilePrefab => projectilePrefab;
```

Per type, not per level — deliberately: the levels live inside the prefab as the `LV` children, which is how the artist authored them.

## 2. `GridKnockdownCannonProjectile` — the level look

On `SetAmmunition(bullet, level)` (and once in `Awake` for the no-loadout demo path), apply the look:

```csharp
/// <summary>
/// Enables the mesh child for the level this shot was fired at. Children are matched by the
/// "LV{n}" suffix the artist uses (Boom01_LV2); the highest n not above the level wins, so a
/// prefab with fewer looks than the bullet has levels shows its best one rather than nothing.
/// Children without the suffix (none today) are left alone.
/// </summary>
private void ApplyLevelLook(int level)
```

Implementation notes: scan direct children once and cache `(child, n)` pairs in `Awake` (parse a trailing `LV<digit>`, ordinal, case-insensitive — no allocation per shot after the cache); pick `n =` the largest cached n ≤ `max(1, level)`, falling back to the smallest if none is below; `SetActive` accordingly. A prefab with no `LV` children (the fallback sphere) is untouched. The look is re-applied on every `SetAmmunition`, which the pool path already calls per shot, so a reused instance can change level or type freely.

## 3. Fire controller and pool

- `GridKnockdownCannonFireController.Fire(...)`: resolve `prefab = ammunition != null && ammunition.ProjectilePrefab != null ? ammunition.ProjectilePrefab : projectilePrefab` and rent/instantiate **that**; everything else (SetAmmunition, damage multiplier, speed, lifetime) unchanged.
- `ProjectilePool` becomes keyed: `Rent(GridKnockdownCannonProjectile prefab, pos, rot, parent)`; internally a `Dictionary<GridKnockdownCannonProjectile, Stack<GridKnockdownCannonProjectile>>` plus a per-instance origin map (`Dictionary<instance, prefab>`) so `Return` files an instance under the prefab it came from — never under the one currently selected. `Warm(prefab, size)` warms one kind; `ReturnAll` unchanged in spirit (walks the live list). The old single-prefab `Warm(size)`/`Rent(pos,…)` overloads stay and delegate using the serialized fallback prefab, so nothing else breaks.
- Prewarm: where the controller warms today (`projectilePool.Warm(budget)`), warm **each type actually brought**: for every bullet id with a count in `BulletInventory`, warm its definition's prefab with that count (fallback prefab for unconfigured ones). The budget-sized single warm goes.
- The artist prefabs' serialized script fields: the builder (§4) verifies `loadout` is empty (correct — the fire controller injects ammunition per shot) and `SetIfEmpty`s nothing else; their tuned physics values are theirs to keep. If a prefab's `bulletOverride` is set, log a warning — an override would pin the damage to one type regardless of what fired.

## 4. Editor — `BulletDefinitionBuilder`

In `Create Default Bullet Definitions`: `SetIfEmpty` `projectilePrefab` — `rock_type` → `Prefabs/Bullets/CannonBall_01.prefab`, `cannon_type` → `Prefabs/Bullets/CannonBall_02.prefab` (path constants in one table). Also run the §3 field check on both prefabs and warn, not fix, on surprises.

## 5. Acceptance criteria

1. **Clean build**: the menu item wires both definitions; a second run is a no-op; the scene's serialized `projectilePrefab` fallback is untouched.
2. **Rock at level 1** fires a ball that is visibly `Boom01_LV1`; after upgrading Rock to level 2 in the garage, the next run's rocks are `Boom01_LV2`. Cannon Ball fires `Boom02_*` at its level.
3. **Mid-run switch**: bring both types; when the equipped type runs out and the controller falls back to the other, the very next shot changes look. Ten alternating shots through the pool never show the wrong mesh or the wrong type's prefab (the per-prefab pool holds).
4. **Damage unchanged**: the look swap changes no numbers — a level-2 rock deals exactly the authored level-2 damage with either mesh cached or fresh (log in a development build).
5. **Unconfigured bullet** (empty `projectilePrefab` in a test copy): fires the controller's fallback prefab, no errors, no look swap.
6. **Legacy**: `CannonBall.prefab` and any demo scene using `CannonProjectile` still compile and run untouched.
7. **Domain-reload-off**: two play sessions in a row — pool rebuilt cleanly, no duplicated children states, no leaked instances (`ReturnAll` still collects everything).

## 6. Out of scope

Trail/impact VFX per type, a third bullet type, per-level prefabs, AmmoPick screen icons (kept text-only by decision), rebuilding the legacy `CannonBall.prefab`.
