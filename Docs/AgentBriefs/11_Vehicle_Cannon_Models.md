# Task Brief 11 — Vehicles become the three pack cannons, and the model swaps under the cannon

## Goal

Replace the placeholder truck/tank with **three vehicles built from the Hyper-Casual Cannon Pack**: `cannon_a` (the free starter), `cannon_b`, `cannon_c`, three levels each, one pack prefab per level. Equipping or upgrading a vehicle swaps the model under **`World/Slingshot/Cannon`** in the scene; firing plays the pack model's own shoot animation. The garage needs no change here (Brief 12 adds the SELECT/EQUIPPED button).

Decisions already made (do not re-open):

- **The catalogue becomes exactly A, B, C.** `vehicle_truck.asset` / `vehicle_tank.asset` are deleted. Old saves that name them fall back to the default (`VehicleLoadout.Selected` already refuses unknown/locked ids); an owned tank is simply lost — accepted for the jam.
- **Level → prefab mapping** (all under `Assets/Hyper-Casual Cannon Pack – Animated Turrets (URP + Built-in)/Cannon_Pack_URP/Prefaps_URP/` — note the pack's own "Prefaps" typo):
  - `cannon_a`: `Cannon_A_URP`, `Cannon_A_B_URP`, `Cannon_A_C_URP`
  - `cannon_b`: `Cannon_B_URP`, `Cannon_B_B_URP`, `Cannon_B_C_URP`
  - `cannon_c`: `Cannon_C_URP`, `Cannon_C_B_URP`, **`Cannon_C_D_URP`** (there is no `C_C`; `C_D` is the third C model)
  - The `Cannon_D_*` family is left unused.
- **The model mounts under the existing `Cannon` object** inside `Imported/LunaSmashdown/Prefabs/SmashFest/Slingshot.prefab` — the object that carries the aim/recoil `Animator` and rotates with the aim, so a spawned model aims for free. The old visible model `CannonTank_Default_Red` becomes the **fallback**: shown only when no vehicle model resolves, hidden the moment one spawns.
- **`fireOrigin` stays the fixed `MuzzlePoint`** the fire controller already uses. The pack models have no muzzle node (their skeleton is `Cannon.A/B/C` bones), and the three cannons are close enough in size that one point serves; a per-model muzzle is out of scope.
- **The fire animation comes from the spawned model**: every pack prefab carries an `Animator` with the pack's `Cannon.controller`, whose single state is **`Armature|Shoting`** (the pack's own spelling). `CannonShotPresenter` plays that on the mounted model when one exists, and falls back to its old `Cannon_Shot` state (and keeps the smoke) otherwise.
- Placeholder numbers, a playable spread rather than balance (buy once, then pay per level, exactly the existing config shapes):

| Vehicle | Unlock | L1 mult | L2 mult / price | L3 mult / price |
|---|---|---|---|---|
| `cannon_a` (default) | free | 1.00 | 1.20 / 300 | 1.40 / 700 |
| `cannon_b` | 800 | 1.30 | 1.60 / 1200 | 2.00 / 2000 |
| `cannon_c` | 2000 | 1.50 | 2.00 / 2500 | 2.60 / 4000 |

House rules (unchanged): builders are idempotent — fill what is missing, never overwrite what is set; every subscriber unsubscribes in `OnDisable`; XML doc comments say *why*; no LINQ in runtime paths; ids never renamed once saved. Git: branch **`Feature/CannonVehicles`** from `main`, one-line commits, no body.

## Repository

`/Volumes/Supercent/FierceTigerGameJam/FierceTigerGameJam` — paths relative to `Assets/GameJam/` unless they start with `Assets/`.

| Existing | Role | Touched |
|---|---|---|
| `Scripts/Gameplay/Combat/VehicleDefinition.cs` | per-level `displayName`, `damageMultiplier`, `modelPrefab`, `icon`; `ResolveModelPrefab(level)` walks down to the nearest lower level with a model | untouched — the model slots finally get used |
| `Scripts/Gameplay/Cannon/VehicleMount.cs` | spawns `Selected`'s model for its level at `mountPoint`, follows `SelectionChanged`/`LevelChanged`; currently placed nowhere | gains the fallback-model field; placed on `Cannon` |
| `Scripts/Gameplay/Cannon/CannonShotPresenter.cs` | on the Slingshot root; `PlayShot()` plays `Cannon_Shot` on the old Animator + smoke particles | plays the pack state on the mounted model first |
| `Imported/LunaSmashdown/Prefabs/SmashFest/Slingshot.prefab` | `Cannon` (Animator; aim target) with children `CannonA [off]` → `MuzzlePoint`, `CannonTank_Default_Red` (visible model) | mount added, fallback wired |
| `Editor/VehicleDefinitionBuilder.cs` | creates truck/tank + the loadout | rewritten for the three cannons |
| `Editor/GameConfigBuilder.cs` | `FillVehiclePurchasePrices` / `FillVehicleUpgradePrices` (default free / flat prices), wires `EconomyService` | price tables per the table above; stale truck/tank rows removed |
| `Config/Vehicles/*` | `vehicle_truck`, `vehicle_tank`, `VehicleLoadout` | two assets deleted, loadout re-pointed |
| `Scripts/Economy/EconomyService.cs` | `TryPurchaseVehicle` equips on purchase via `vehicleLoadout.Select` | untouched |

## 1. `VehicleMount` — the fallback model

```csharp
[Tooltip("Shown only while no vehicle model resolves — the old tank, so an unwired or "
       + "model-less loadout still shows a cannon rather than a floating barrel.")]
[SerializeField] private GameObject fallbackModel;

/// <summary>The spawned model's Animator, for whoever presents the shot. Null on the fallback.</summary>
public Animator CurrentAnimator { get; private set; }
```

In `Refresh()`: after resolving the prefab — `fallbackModel.SetActive(prefab == null)`; when a model is spawned, cache `CurrentAnimator = current.GetComponentInChildren<Animator>(true)`. Clear `CurrentAnimator` when the model is destroyed or nothing resolves. The collider strip already exists (`StripColliders` runs on every spawn), so a pack collider cannot eat the shot; everything else in the component stays as it is.

## 2. `CannonShotPresenter` — the pack's shoot animation

```csharp
[Tooltip("Where the current vehicle model comes from. With a mounted model its own Animator "
       + "plays the pack's shot; without one the legacy cannon animation still fires.")]
[SerializeField] private VehicleMount mount;

[Tooltip("State name inside the pack's Cannon.controller. Their spelling, not ours.")]
[SerializeField] private string mountedShotState = "Armature|Shoting";
```

`PlayShot()`: if `mount != null && mount.CurrentAnimator != null && …activeInHierarchy` → `Play(Animator.StringToHash(mountedShotState), 0, 0f)` (hash cached when the string changes, not per shot); **else** the existing `_cannonAnimator` path. Smoke plays in both cases. `ResetPresentation` unchanged.

## 3. Data and prices — `Editor/VehicleDefinitionBuilder.cs`, `Editor/GameConfigBuilder.cs`

`Tools > Smashdown > Create Default Vehicle Definitions` now:

1. Creates/fills `Config/Vehicles/cannon_a.asset`, `cannon_b.asset`, `cannon_c.asset` — ids `cannon_a/b/c`, display names `Cannon A/B/C`, level display names `Cannon A I…III` etc., multipliers from the table, `modelPrefab` per level loaded from the pack paths above (`AssetDatabase.LoadAssetAtPath<GameObject>`; keep the paths in one constant table — the pack folder name has an en-dash and a typo, copy it exactly). Existing values are never overwritten (`SetIfEmpty` per serialized property), so hand-tuning survives.
2. Points `VehicleLoadout.asset` at the three (`vehicles` array filled only when empty **or when it still names a deleted asset**; `defaultVehicle` = `cannon_a`).
3. Deletes `vehicle_truck.asset` and `vehicle_tank.asset` (`AssetDatabase.DeleteAsset`) after the loadout no longer references them.

`GameConfigBuilder`: the vehicle price fills take their numbers from one static table keyed by id (the §Goal table) instead of the current "default free, others 800 / flat upgrade" rule; rows for ids that no longer exist in any `VehicleDefinition` are removed from both configs. Everything else (`EnsureAsset`, wiring into `EconomyService`) unchanged.

## 4. The Slingshot prefab

In `Imported/LunaSmashdown/Prefabs/SmashFest/Slingshot.prefab` (edit the prefab, not the scene instance — the builder does it via `LoadPrefabContents`, a `Tools > Smashdown > Wire Vehicle Mount` step inside the definitions menu item is fine):

- `Ensure<VehicleMount>` on the `Cannon` child; `SetIfEmpty`: `loadout` = `VehicleLoadout.asset`, `mountPoint` = the `Cannon` transform itself, `fallbackModel` = `CannonTank_Default_Red`.
- `SetIfEmpty` on the root's `CannonShotPresenter`: `mount` = that `VehicleMount`.
- Nothing else moves: `MuzzlePoint`, the old Animator, the smoke particles all stay.

Spawn pose: the model goes in at local position zero / identity / `modelLocalScale` (existing field, default 1). The pack cannons are authored around the same footprint as the tank; if one sits wrong, the fix is the mount's `modelLocalScale` or a nudge of `Cannon` — by hand, not in code. Call this out in the run notes rather than guessing offsets.

## 5. Acceptance criteria

1. **Clean build**: `Create Default Vehicle Definitions` then `Create Game Configs` on a fresh checkout produces the three vehicle assets with models wired, the loadout on A/B/C (default A), prices per the table, no truck/tank assets or price rows left; a second run changes nothing.
2. **In the scene**, entering play with a fresh save shows `Cannon_A_URP` under `World/Slingshot/Cannon`; `CannonTank_Default_Red` is inactive. With the `VehicleLoadout` asset emptied in a test scene the tank shows again (fallback path).
3. **Firing** plays the pack model's `Armature|Shoting` and the smoke; aiming still rotates the model with the `Cannon` object; the projectile still spawns at `MuzzlePoint` and nothing on the model blocks it (colliders disabled — verify with a point-blank shot).
4. **Garage vehicle tab** lists Cannon A `LEVEL 1` equipped, Cannon B locked `800`, Cannon C locked `2,000`. Buying B swaps the mounted model to `Cannon_B_URP` without leaving the screen (purchase auto-equips, existing behaviour). Upgrading the equipped vehicle to L2 swaps to the `_B` model; upgrading a **non-equipped** one changes no model.
5. **Damage** still multiplies: A L1 ×1.00 exactly equals the bullet's authored damage; B L1 hits ×1.30 (log both in a development build).
6. **Old save** with `selectedVehicleId: vehicle_tank`: the game boots, Cannon A is equipped and mounted, nothing throws.
7. **Domain-reload-off**: entering play twice does not double-subscribe the mount or leak a spawned model.

## 6. Out of scope

Vehicle icons in the garage (no art), per-model muzzle points, wheel/idle animations, the D family, balance beyond the table, any garage UI change (Brief 12).
