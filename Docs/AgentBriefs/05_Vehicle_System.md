# Task Brief 05 — Vehicle system: config, save data, shop, damage boost, model mount

## Goal

Add a **vehicle** the player picks before a run. A vehicle has 3 levels; the selected vehicle's current level multiplies the damage of whatever bullet is loaded. Vehicles are bought and upgraded in the shop with gold, exactly like bullets. Each level references its own model prefab so art can drop models in later and the game spawns the right one under the cannon.

Decisions already made (do not re-open):

- Boost is a **multiplier** on the bullet's `blockDamage` and `wallDamage`. A material the bullet cannot damage (0) stays 0, so the "unlock" design of `BulletDefinition` is untouched.
- **One model prefab per level.** An empty slot falls back to the nearest lower level's prefab, so shipping with a single model per vehicle works.
- The vehicle is a **base under the existing cannon**. The cannon barrel, `Animator`, `fireOrigin`, and smoke stay where they are; the vehicle prefab is spawned at a mount transform.
- Shop model is the **same as bullets**: buy once to unlock at level 1, then pay per level for 2 and 3. The starter vehicle is always owned. Selecting is separate from buying.

## Repository

`/Volumes/Supercent/FierceTigerGameJam/FierceTigerGameJam`

Existing patterns to mirror — read these first, the vehicle code should look like a sibling of them:

| Existing | Role | Vehicle counterpart |
|---|---|---|
| `Scripts/Gameplay/Combat/BulletDefinition.cs` | per-type data + levels | `VehicleDefinition` |
| `Scripts/Gameplay/Combat/BulletLoadout.cs` | catalogue + selection + level, reads `UserData` | `VehicleLoadout` |
| `Scripts/Config/PurchaseBulletConfig.cs` | unlock price by id | `PurchaseVehicleConfig` |
| `Scripts/Config/UpgradeBulletConfig.cs` | level prices by id | `UpgradeVehicleConfig` |
| `Scripts/Data/UserBulletData.cs` + `UserData.cs` | save record | `UserVehicleData`, new key in `UserData` |
| `Scripts/Economy/EconomyService.cs` | only place gold is spent | vehicle purchase/upgrade methods added |
| `Scripts/Gameplay/Cannon/GridKnockdownCannonProjectile.cs` → `ResolveDamage` | damage lookup | multiplier applied here |
| `Scripts/Gameplay/Cannon/GridKnockdownCannonFireController.cs` → `Fire` | tells the projectile what fired it (`SetAmmunition`) | also tells it the vehicle multiplier |
| `Scripts/UI/BulletShopView.cs`, `BulletTypeUpgradeView.cs` | shop list + row | `VehicleShopView` + `VehicleShopRowView` (two-button row, own prefab) |
| `Scripts/Gameplay/Flow/GameFlowController.cs` | states + screen wiring | `GameState.VehicleShop` |
| `Editor/GameConfigBuilder.cs`, `Editor/BulletDefinitionBuilder.cs` | create/fill/wire assets | extend with vehicle assets |

All paths are relative to `Assets/GameJam/`.

## 1. Data model

### 1.1 `VehicleDefinition` (ScriptableObject) — `Scripts/Gameplay/Combat/VehicleDefinition.cs`

```csharp
[CreateAssetMenu(menuName = "GameJam/Vehicle Definition", fileName = "VehicleDefinition")]
public sealed class VehicleDefinition : ScriptableObject
{
    [Serializable]
    public sealed class Level
    {
        [Tooltip("Shown to the player, e.g. \"Truck II\".")]
        public string displayName;

        [Tooltip("Multiplies the loaded bullet's blockDamage and wallDamage. 1 = no boost. "
               + "A material the bullet cannot hurt (0) stays 0 whatever this is.")]
        [Min(0f)] public float damageMultiplier = 1f;

        [Tooltip("Model spawned under the cannon at this level. Left empty, the nearest lower "
               + "level's model is used, so one model per vehicle is enough to ship.")]
        public GameObject modelPrefab;

        [Tooltip("Optional. Shown in the shop row / preview. Falls back like modelPrefab.")]
        public Sprite icon;
    }

    [Tooltip("Stable id used in saves and configs, e.g. vehicle_truck. Never rename after release.")]
    [SerializeField] private string id;
    [SerializeField] private string displayName;
    [TextArea] [SerializeField] private string description;

    [Tooltip("Index 0 is level 1. Three levels for now; the code does not assume three.")]
    [SerializeField] private Level[] levels = Array.Empty<Level>();

    public string Id => id;
    public string DisplayName => string.IsNullOrEmpty(displayName) ? id : displayName;
    public string Description => description;
    public int LevelCount => levels.Length;

    /// <summary>One-based to the player, clamped like BulletDefinition.GetLevel.</summary>
    public Level GetLevel(int level);

    /// <summary>Multiplier at a level; 1 when the vehicle defines no levels.</summary>
    public float GetDamageMultiplier(int level);

    /// <summary>
    /// Model for a level, walking down to lower levels when the slot is empty. Null only when
    /// no level has a model at all.
    /// </summary>
    public GameObject ResolveModelPrefab(int level);

    /// <summary>Same fallback rule for icons.</summary>
    public Sprite ResolveIcon(int level);

    private void OnValidate()
    {
        // id defaults to asset name (same as BulletDefinition); multipliers clamped >= 0;
        // warn if a level's multiplier is lower than the level below it (allowed, but almost
        // always a typo).
    }
}
```

### 1.2 `VehicleLoadout` (ScriptableObject) — `Scripts/Gameplay/Combat/VehicleLoadout.cs`

Mirror of `BulletLoadout`, no state of its own:

```csharp
[CreateAssetMenu(menuName = "GameJam/Vehicle Loadout", fileName = "VehicleLoadout")]
public sealed class VehicleLoadout : ScriptableObject
{
    [SerializeField] private VehicleDefinition[] vehicles;
    [Tooltip("Owned and selected before the player has bought anything.")]
    [SerializeField] private VehicleDefinition defaultVehicle;

    public event Action<VehicleDefinition> SelectionChanged;
    /// <summary>Raised when the selected vehicle's level changes; the mount re-spawns the model.</summary>
    public event Action<VehicleDefinition, int> LevelChanged;

    public IReadOnlyList<VehicleDefinition> Vehicles { get; }
    public VehicleDefinition DefaultVehicle { get; }

    /// <summary>Saved choice if it exists and is owned, otherwise the default.</summary>
    public VehicleDefinition Selected { get; }
    public int SelectedLevel { get; }

    /// <summary>The number the projectile multiplies by. 1 when nothing is configured.</summary>
    public float SelectedDamageMultiplier => Selected != null ? Selected.GetDamageMultiplier(SelectedLevel) : 1f;

    public bool Select(VehicleDefinition vehicle);      // refuses locked; saves; raises SelectionChanged
    public bool SelectById(string id);
    public int GetLevel(VehicleDefinition vehicle);
    public bool IsUnlocked(VehicleDefinition vehicle);  // defaultVehicle always true
    public void Unlock(VehicleDefinition vehicle);
    public int SetLevel(VehicleDefinition vehicle, int level); // clamps to LevelCount; raises LevelChanged if selected
    public bool IsMaxLevel(VehicleDefinition vehicle);
    public VehicleDefinition Find(string id);
}
```

`SelectionChanged` and `LevelChanged` must be cleared in `OnDisable` (same reason as `EconomyService.GoldChanged`: the asset outlives play mode).

### 1.3 Save record — `Scripts/Data/UserVehicleData.cs` + `UserData.cs`

```csharp
[Serializable] public sealed class VehicleProgress { public string vehicleId; public bool unlocked; public int level = 1; }

[Serializable]
public sealed class UserVehicleData
{
    public int version = 1;
    public string selectedVehicleId;
    public List<VehicleProgress> vehicles = new List<VehicleProgress>();
    // IsUnlocked / GetLevel / Unlock / SetLevel / TryGet / GetOrCreate — same as UserBulletData
}
```

`UserData`: add `VehiclesKey = "user.vehicles"`, `public static UserVehicleData Vehicles`, and include it in `Save()`, `Reload()`, `ResetAll()`, `ResetOnEnterPlayMode()`. Old saves have no record → default vehicle at level 1, no migration needed.

### 1.4 Price configs — `Scripts/Config/PurchaseVehicleConfig.cs`, `UpgradeVehicleConfig.cs`

Copy `PurchaseBulletConfig` / `UpgradeBulletConfig` with `bulletId → vehicleId`, same `TryGetPrice` / `TryGetUpgradePrice` / `TryGetMaxLevel` / `OnValidate` de-dup warnings. Level 1 is never priced (it comes with the unlock), so upgrade rows are `targetLevel` 2 and 3.

> Optional refactor, **not** for this task: the four price configs are the same table keyed by id. If a third progression type appears, extract `IdPriceTable` / `IdLevelPriceTable` and make the bullet/vehicle configs thin wrappers. For now mirroring keeps the diff reviewable.

### 1.5 Placeholder content (created by the editor tool, §5)

| Vehicle id | Display | Unlock | L1 mult | L2 mult / price | L3 mult / price |
|---|---|---|---|---|---|
| `vehicle_truck` (default) | Truck | free | 1.00 | 1.20 / 300 | 1.40 / 700 |
| `vehicle_tank` | Tank | 800 | 1.30 | 1.60 / 1200 | 2.00 / 2000 |

Playable starting point, not a balance pass. Truck L3 (1.4) is deliberately above Tank L1 (1.3), so a fully upgraded starter is not strictly dominated by an un-upgraded purchase; what the player buys with the tank is its ceiling (2.0).

## 2. Economy — `EconomyService.cs`

Add serialized `purchaseVehicleConfig`, `upgradeVehicleConfig`, `vehicleLoadout`, and these methods, each a line-for-line sibling of the bullet version (same "check everything, then charge, then write, then `Save()`, then `GoldChanged`" order):

```
bool TryGetVehiclePurchasePrice(VehicleDefinition v, out int price)
bool CanPurchaseVehicle(VehicleDefinition v)
bool TryPurchaseVehicle(VehicleDefinition v)       // UserData.Vehicles.Unlock, level stays 1
bool TryGetVehicleUpgradePrice(VehicleDefinition v, out int price, out int targetLevel)
int  GetVehicleMaxLevel(VehicleDefinition v)        // min(defined LevelCount, priced max) — same rule as GetMaxLevel
bool CanUpgradeVehicle(VehicleDefinition v)
bool TryUpgradeVehicle(VehicleDefinition v)         // UserData.Vehicles.SetLevel(targetLevel); if selected → loadout raises LevelChanged
```

`TryUpgradeVehicle` must go through `vehicleLoadout.SetLevel` (not `UserData` directly) **or** raise the loadout's `LevelChanged` itself, otherwise the mounted model never refreshes after an upgrade. Pick one and comment why; the bullet path writes `UserData` directly because nothing visual depends on a bullet level.

## 3. Damage application

### 3.1 `GridKnockdownCannonProjectile`

Add:

```csharp
[Tooltip("Multiplies the ammunition's damage. Set per shot by the fire controller from the "
       + "selected vehicle; 1 when no vehicle system is wired.")]
[SerializeField] private float damageMultiplier = 1f;

public void SetDamageMultiplier(float multiplier) => damageMultiplier = Mathf.Max(0f, multiplier);
```

In `ResolveDamage(...)`, after `float amount = isWall ? damage.wallDamage : damage.blockDamage;`:

```csharp
amount *= damageMultiplier;
```

before the `if (amount <= 0f || direct) return amount;` line, so both the direct hit and the splash path (`amount * splashShare * falloff`) are boosted, and a 0 stays 0. The flat fallback (`directHitDamage` / `splashDamage`, used when no bullet is configured) is **not** multiplied — that path exists for scenes with no progression wired, and boosting it hides misconfiguration.

Reset `damageMultiplier = 1f` when the projectile is returned to a pool (relevant once Brief 02 lands).

### 3.2 `GridKnockdownCannonFireController`

Add `[SerializeField] private VehicleLoadout vehicleLoadout;` and in `Fire()` right after `projectile.SetAmmunition(...)`:

```csharp
if (vehicleLoadout != null)
{
    projectile.SetDamageMultiplier(vehicleLoadout.SelectedDamageMultiplier);
}
```

Read at fire time, not cached: the player can change vehicle between runs without the cannon being re-enabled.

### 3.3 Formula (for design / tuning reference)

```
final = bullet.damage[level][material].(block|wall)Damage × vehicle.levels[vLevel].damageMultiplier
splash = final × bullet.levels[level].splashShare × falloff
```

No rounding. `BreakableBlock.maxDamagePerImpact` does **not** apply to projectile damage (it only caps collision damage), so a high multiplier one-shots as authored.

## 4. Model mount — `Scripts/Gameplay/Cannon/VehicleMount.cs`

```csharp
/// Spawns the selected vehicle's model for its current level at a mount point under the
/// cannon, and swaps it when the selection or the level changes.
[DisallowMultipleComponent]
public sealed class VehicleMount : MonoBehaviour
{
    [SerializeField] private VehicleLoadout loadout;
    [Tooltip("Where the model goes. Left empty, this transform.")]
    [SerializeField] private Transform mountPoint;
    [Tooltip("Applied to the spawned model so art can be authored at any scale.")]
    [SerializeField] private Vector3 modelLocalScale = Vector3.one;

    private GameObject current;
    private VehicleDefinition currentVehicle;
    private int currentLevel;

    OnEnable: subscribe loadout.SelectionChanged / LevelChanged → Refresh(); Refresh().
    OnDisable: unsubscribe; keep the model (the cannon is always visible).
    Refresh(): resolve (Selected, SelectedLevel); if unchanged return; Destroy(current);
               prefab = Selected.ResolveModelPrefab(level); if null → warn once, return;
               current = Instantiate(prefab, mountPoint); localPos/rot = zero/identity; localScale = modelLocalScale.
    [ContextMenu("Refresh")] for editor testing.
}
```

Place it on the cannon root in `Scene/Gameplay.unity` with `mountPoint` = a new empty child `VehicleMount` positioned under the barrel. The model prefab is pure visuals: no colliders (or colliders on a layer the projectile ignores — `IgnoreSpawnOverlaps` already ignores overlaps at the muzzle, but a vehicle collider in the flight path would be hit). Enforce in `Refresh`: strip/disable `Collider`s on the spawned model in development builds with a warning, so an art prefab with a collider cannot block shots.

For the shop preview, the same component can sit on a preview rig (`mountPoint` inside a `RenderTexture` camera or a world-space UI slot). Out of scope for this task, but do not make `VehicleMount` depend on the cannon.

## 5. Shop and flow

### 5.1 `VehicleShopView` — `Scripts/UI/VehicleShopView.cs`

Mirror `BulletShopView`: one row per `VehicleLoadout.Vehicles`. The vehicle row has **two buttons**, so it gets its own row component and prefab rather than reusing `BulletTypeUpgradeView` (which has one):

- `Scripts/UI/VehicleShopRowView.cs` — same shape as `BulletTypeUpgradeView` (name label, level label, `ResolveMissingReferences` by child name, `Bind(...)`), with `primaryButton` + `primaryLabel` (buy / upgrade) and `selectButton` + `selectLabel`. Expose both buttons; the shop wires the clicks.
- `Prefabs/UI/VehicleShop/VehicleShopRow.prefab` — duplicate of the bullet row prefab with a second button added, children named `Name`, `Level`, `Primary`, `Select` so the auto-wiring finds them.

Row logic — both buttons are always present; a button that has nothing to do is disabled, never hidden, so rows keep the same width:

| State | Level label | Primary button | Select button |
|---|---|---|---|
| locked | "Locked" | "Buy {price}" → `economy.TryPurchaseVehicle` (disabled when unaffordable) | "Select" disabled |
| owned, not selected, upgradable | "Lv {n}/{max} · ×{mult:0.00}" | "Upgrade {price}" → `economy.TryUpgradeVehicle` (disabled when unaffordable) | "Select" enabled → `loadout.Select` |
| owned, not selected, max | "Lv {n}/{max} · ×{mult:0.00}" | "MAX" disabled | "Select" enabled |
| owned, selected, upgradable | "Lv {n}/{max} · ×{mult:0.00}" | "Upgrade {price}" → `economy.TryUpgradeVehicle` | "Selected" disabled |
| owned, selected, max | "Lv {n}/{max} · ×{mult:0.00}" | "MAX" disabled | "Selected" disabled |

Show the multiplier so the player can see what they are buying. Upgrading a non-selected vehicle is allowed (it is what the player saves toward); selecting a locked vehicle is never allowed.

Subscribe to `economy.GoldChanged`, `loadout.SelectionChanged`, `loadout.LevelChanged`, `UserData.Changed` → `Refresh()`; unsubscribe in `OnDisable`.

### 5.2 `GameFlowController`

Add `GameState.VehicleShop`, a `vehicleShopButton` on the main menu / bottom bar, `EnterVehicleShop()`, and the screen prefab `Prefabs/UI/VehicleShop/VehicleShopScreen.prefab` built from `BulletShopScreen.prefab` (duplicate, swap the view component, point `rowPrefab` at `VehicleShopRow.prefab`). Back button returns to `MainMenu` like the bullet shop.

### 5.3 Editor tooling — `Editor/GameConfigBuilder.cs` (+ new `Editor/VehicleDefinitionBuilder.cs`)

- `Tools > Smashdown > Create Default Vehicle Definitions`: creates `Config/Vehicles/vehicle_truck.asset` and `vehicle_tank.asset` with the §1.5 levels (empty `modelPrefab`, empty icons), and `Config/Vehicles/VehicleLoadout.asset` listing both with truck as default. Never overwrites existing assets (same rule as `GameConfigBuilder`: "nothing already set is overwritten").
- `CreateGameConfigs`: also `EnsureAsset<PurchaseVehicleConfig>`, `EnsureAsset<UpgradeVehicleConfig>`, fill from every `VehicleDefinition` found (`FillVehiclePurchasePrices`: default vehicle free, others 800; `FillVehicleUpgradePrices`: 300/700 for the default, 1200/2000 otherwise, only for levels the definition actually has), wire them + the loadout into `EconomyService`, wire `vehicleLoadout` into `GridKnockdownCannonFireController` and `VehicleMount` via `SetIfEmpty`.

## 6. Constraints

- No change to `BulletDefinition`, bullet configs, or `UserBulletData`.
- `EconomyService` remains the only place gold is spent; `VehicleShopView` never touches `UserData.Inventory`.
- ScriptableObject events (`SelectionChanged`, `LevelChanged`, `GoldChanged`) cleared in `OnDisable`; every MonoBehaviour subscriber unsubscribes in `OnDisable`.
- Ids are ordinal strings, never renamed after they reach a save.
- `VehicleMount` must work with a null `loadout` (no vehicle system in a test scene) — it simply spawns nothing.
- Keep the code style of the neighbours (XML doc comments explaining *why*, `Try*` methods returning bool, no LINQ in runtime paths).

## 7. Acceptance criteria

1. Fresh save: `VehicleLoadout.Selected` is the truck at level 1, multiplier 1.0; a shot's damage equals the bullet's authored damage exactly (log both in a development build).
2. Upgrade truck to L2 in the shop: gold decreases by the configured price, `UserData.Vehicles` records level 2, next shot's block **and** wall damage are ×1.2, splash damage scales accordingly, and a material with 0 damage still takes 0.
3. Buy the tank, select it: mount swaps the model (or logs the "no model" warning once if prefabs are empty); truck remains owned at its level; re-selecting the truck restores its multiplier.
4. Upgrade the **selected** vehicle: model refreshes without leaving/re-entering the scene (`LevelChanged` path). Upgrade a **non-selected** vehicle from its row's primary button: level and gold change, no model change, and the row's Select button stays enabled.
4b. Every row shows both buttons in every state; the Select button reads "Selected" and is disabled on the current vehicle, and is disabled on locked vehicles.
5. Level 3 with an empty `modelPrefab` uses level 2's (or level 1's) model — no null spawn.
6. Prices: `GetVehicleMaxLevel` is the min of defined levels and priced levels; a vehicle with no upgrade rows shows MAX at level 1.
7. Restart the game: selection and levels persist; `UserData.ResetAll()` clears them.
8. `Tools > Smashdown > Create Default Vehicle Definitions` followed by `Create Game Configs` on a clean checkout produces all assets and wires the scene with no manual steps except placing `VehicleMount`'s mount point.
9. Domain-reload-off editor: entering play mode twice does not double-fire `SelectionChanged`/`LevelChanged` (events cleared in `OnDisable`).

## 8. Out of scope

- Vehicle affecting anything other than damage (shot budget, projectile speed, cooldown) — the `Level` class is the place to add such fields later; do not add them now.
- Shop 3D preview, IAP purchase of vehicles, vehicle-specific SFX/VFX.
- Balance values beyond the placeholders in §1.5.
