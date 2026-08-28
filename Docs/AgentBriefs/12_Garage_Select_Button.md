# Task Brief 12 — Garage rows: the SELECT / EQUIPPED button above Buy

## Goal

Give every garage row (both tabs) the new equip control: a **SELECT** button sitting **directly above the Buy button**, replaced by the **EQUIPPED** chip on the row whose item is currently equipped. The tap goes through the select hook the shops already wire (`ShopItemView.SelectButton` → `BulletLoadout.Select` / `VehicleLoadout.Select`); today that hook falls back to a Button on the row root that the prefabs never had, so the feature exists in code and has no button — this brief gives it one.

Decisions already made (do not re-open):

- **Art**: `Textures/UI/Garage/Btn_Select.png` (144×51, yellow, text baked) and `UI_Equipped.png` (145×51, blue chip, text baked). Both imported as Sprite, no borders → `Image.Type.Simple`, `preserveAspect`.
- **States**: unlocked + not equipped → `Select` button active; unlocked + equipped → `Equipped` chip active (not a button — there is nothing to do on it); locked → **neither**. The two share one rect, so nothing ever reflows.
- **Everything else stays**: buying a vehicle still auto-equips it (`EconomyService.TryPurchaseVehicle` → `Select`); the equipped row still sits at `CanvasGroup` alpha 1 with the rest at 0.5 — the chip is a second cue on top, not a replacement. Bullets equip through the same button (that is what `BulletShopView.HandleRowSelected` already does); the AmmoPick screen keeps working unchanged, both write the same save field.

House rules (unchanged): idempotent builder, `SetIfEmpty`, anchors `(xMin, yMin)–(xMax, yMax)` of the parent with y from the bottom, `ResolveMissingReferences` by child name that never overwrites a set reference. Git: branch **`Feature/GarageSelect`** from `main`, one-line commits, no body.

## Repository

`/Volumes/Supercent/FierceTigerGameJam/FierceTigerGameJam` — paths relative to `Assets/GameJam/`.

| Existing | Role | Touched |
|---|---|---|
| `Scripts/UI/ShopItemView.cs` | row parts + `Bind(State)`; `State` already carries `Unlocked` and `Equipped`; `selectButton` falls back to `GetComponent<Button>()` on the root | select/equipped visuals added |
| `Scripts/UI/BulletShopView.cs`, `VehicleShopView.cs` | already wire `row.Item.SelectButton.onClick` → select the item and refresh | untouched |
| `Prefabs/UI/Garage/BulletTypeViewItem.prefab`, `VehicleTypeViewItem.prefab` | row 490×91: `Frame`, `Icon`, `Header`, `Levels`, `Buy` at `(0.786, 0.074)–(0.969, 0.412)` | two children added |
| `Editor/GarageScreenBuilder.cs` | builds the row prefabs idempotently | ensures the two children + references |

## 1. Row prefab additions (both prefabs, same numbers)

The Buy button occupies the row's bottom-right; the new control mirrors it in the band above (row-local sprite px: Buy y 87–137 of 148; Select y 22–72 — 15 px off the top edge, 15 px gap between them):

| Child | Component | Anchors of the row |
|---|---|---|
| `Select` | Image `Btn_Select`, Simple, preserveAspect + Button (ColorTint) | `(0.786, 0.514)–(0.969, 0.851)` |
| `Equipped` | Image `UI_Equipped`, Simple, preserveAspect, raycastTarget **off**; inactive by default | same anchors |

Both are siblings after `Buy`. The `Header` already stops at x 0.78 vertically overlapping this band is impossible (`Header` y 0.45–0.80 vs Select y 0.51–0.85 share no x range with it — Header text runs to 0.97 but sits behind; if a very long name ever collides visually, the header's Ellipsis is the safety, as before).

## 2. `ShopItemView`

```csharp
[Tooltip("Shown on the equipped row where Select is on the others. A chip, not a button: "
       + "there is nothing to do to the thing already mounted.")]
[SerializeField] private GameObject equippedBadge;
```

- `ResolveMissingReferences`: `selectButton` resolves to a child Button named `Select` **first** (same name-matching helper the class already uses for `Buy`), keeping the root-`GetComponent<Button>` lookup only as the legacy fallback; `equippedBadge` resolves by the child name `Equipped`.
- `Bind(in State state)` adds:

```csharp
bool unlocked = state.Unlocked;
if (selectButton != null) selectButton.gameObject.SetActive(unlocked && !state.Equipped);
if (equippedBadge != null) equippedBadge.SetActive(unlocked && state.Equipped);
```

Nothing else in `Bind` changes — alpha, pips, lock, captions all stay. The shops need no edits: they already wire `SelectButton` and already refresh on `SelectionChanged`, so the button appearing is the whole change.

## 3. Builder — `Editor/GarageScreenBuilder.cs`

In the row-prefab step: `Ensure` the two children with the §1 anchors and sprites on both prefabs; `SetIfEmpty` the component's `selectButton` / `equippedBadge`. Re-running on an already-updated prefab changes nothing.

## 4. Acceptance criteria

1. `Tools > Smashdown > Build Garage Screen` updates both row prefabs in place; a second run is a no-op; the scene's `GarageScreen` instance shows the new rows with no overrides.
2. **Ammo tab**, fresh save: Rock (equipped) shows the EQUIPPED chip and full alpha; Cannon Ball (locked) shows neither control and 0.5 alpha. After buying Cannon Ball it shows SELECT; tapping it moves the chip and the alpha to Cannon Ball, the preview follows, and the AmmoPick screen agrees with the choice.
3. **Vehicle tab**: the equipped cannon shows the chip; buying a locked one auto-equips it, so the chip appears on the new row without another tap (and the mounted model swaps — Brief 11). SELECT on an owned, non-equipped vehicle equips it.
4. Tapping SELECT never buys, tapping Buy never equips a bullet (vehicle purchase auto-equip excepted); the two buttons never overlap and the row never reflows in any state.
5. Locked rows: neither control; tapping where they would be does nothing.
6. Domain-reload-off: entering play twice leaves single listeners (existing rebuild-in-`OnEnable` pattern, still true).

## 5. Out of scope

A confirmation or animation on equip, showing SELECT on locked rows, any change to auto-equip-on-purchase, the AmmoPick screen.
