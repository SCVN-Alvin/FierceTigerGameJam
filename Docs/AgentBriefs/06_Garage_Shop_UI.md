# Task Brief 06 — Garage screen: frame art, tabs, item rows, level pips, preview

## Goal

Build the **Garage screen** from the supplied art and make it **replace the current `ShopScreen`**. The flow does not change: the wrench on the bottom bar still enters `GameState.Shop`, and what the player now sees there is the Garage. It is **one panel** (`UI_Shop_Frame`) with two tabs across its top, **VEHICLES** and **AMMO**, that swap between a selected and an unselected sprite. Under the tabs the frame's baked-in garage table is the **preview**: the equipped item's picture floats over it. Under that is the **list**: one row per catalogue entry, each row a `BulletTypeViewItem` (ammo tab) or `VehicleTypeViewItem` (vehicle tab) showing the item's icon, its name and level, a strip of level pips built from `UpgradeLevelView`, and a Buy/Upgrade button. A row the player does not own shows the `UI_Locked` graphic instead of its level.

**The Garage only buys and upgrades.** It does not equip anything: ammo is still chosen on the pre-run AmmoPick screen, and a vehicle is equipped by buying it. The Garage shows what is equipped — that row at full alpha, the rest dimmed — and that is all.

Reference: `Assets/GameJam/RefAI/Garage_Ammo.png` for the row layout (this brief was measured against the newer mock with three rows: LEVEL 2 ball / LEVEL 1 rocket / LOCKED orb, all with a green price button). The vehicle tab uses the **same row layout**, not the card grid of `Garage_Vehicle.png`.

Decisions already made (do not re-open):

- **No equipping in the Garage.** Rows have exactly one button. No tap-to-select, no Select button. `BulletLoadout.Select` is called only by the AmmoPick screen, as today.
- **Buying a vehicle equips it.** `EconomyService.TryPurchaseVehicle` calls `vehicleLoadout.Select(vehicle)` after the unlock (the loadout refuses locked vehicles, so the order matters). The truck stays owned; the tank is what the player drives from then on. This is the one change to the economy.
- **Equipped row = `CanvasGroup.alpha` 1, every other row 0.5.** A `CanvasGroup` on the row root; `interactable` stays true so a dimmed row can still be bought or upgraded. No ring, no outline.
- **Nothing left to buy → the Buy button reads "MAX" and is dimmed** (non-interactable). Never hidden: the button is positioned, not laid out, so hiding gains nothing and loses the row's shape.
- **The level line carries the name**: `TRUCK · LEVEL 2`. A locked row shows the `UI_Locked` graphic followed by the name (`🔒LOCKED  TANK`).
- **Screen chrome is the gold chip (top-left) and the close X (top-right, `Btn_Esc`).** No mission chip.
- **The preview shows the equipped item's sprite**, the same sprite as the row icon, drawn larger. No render-texture rig for vehicle models.
- **The layout is authored in a new prefab by an editor menu item**, not by `UiBuilder.BuildShop`. The last commit on `main` (`f5e640c`) stopped the builder restructuring prefab instances for good reason. Everything is positioned by anchor fractions of its parent, so one `sizeDelta` on the frame scales the whole screen.

## Git

Work on branch **`Feature/GarageUI`** (created from `main`; this brief and the sprites are its first commit). Commit messages are **one line, no body, no trailers**, in the repo's voice — `Give the garage its row prefab`, not `feat: ...`. Commit per logical step like the existing history (data change, row components, builder, scene), not one commit at the end.

## Repository

`/Volumes/Supercent/FierceTigerGameJam/FierceTigerGameJam` — all paths below are relative to `Assets/GameJam/`.

Read these first; the new code should look like a sibling of them:

| Existing | Role | Touched by this task |
|---|---|---|
| `Scripts/UI/ShopTabsView.cs` | tab strip: one button + one panel per tab, tints on select | gains a sprite-swap mode |
| `Scripts/UI/BulletShopView.cs` | ammo list: rows, prices/refusals from `EconomyService` | new typed-row path, equipped state, preview |
| `Scripts/UI/VehicleShopView.cs` | vehicle list, two-button rows via `VehicleShopRowView` | new typed-row path, equipped state, preview |
| `Scripts/UI/BulletTypeUpgradeView.cs`, `VehicleShopRowView.cs` | old row components (`ResolveMissingReferences` by child name, `Bind`) | untouched; pattern for the new rows |
| `Scripts/Gameplay/Combat/BulletDefinition.cs` | ammo data, `Level[]`, no icon | gets a per-level `icon` + `ResolveIcon` |
| `Scripts/Gameplay/Combat/VehicleDefinition.cs` | vehicle data, already has `Level.icon` + `ResolveIcon(level)` | untouched; the model for the bullet change |
| `Scripts/Gameplay/Combat/BulletLoadout.cs`, `VehicleLoadout.cs` | `Selected`, `SelectedLevel`, `Select`, `IsUnlocked`, `GetLevel`, `SelectionChanged` | untouched |
| `Scripts/Economy/EconomyService.cs` | `TryGet*Price`, `Can*`, `Try*`, `GetMaxLevel` / `GetVehicleMaxLevel`, `GoldChanged` | `TryPurchaseVehicle` also equips |
| `Scripts/Gameplay/Flow/GameFlowController.cs` | states, `shopRoot`, `shopTabs`, `EnterShop`, `GoBack()` | gains `closeShopButton`; `shopRoot` re-pointed |
| `Editor/UiBuilder.cs`, `Editor/UiBuilder.Screens.cs` | scene UI builder; `EnsureRect`, `EnsureLabel`, `Ensure<T>`, `SetIfEmpty`, `EnsureSpriteImage`, `EnsureSpriteButton`, `LoadSprite` | placeholder garage code removed; helpers reused |
| `Prefabs/UI/BulletShop/BulletShopScreen.prefab`, `BulletTypeUpgradeView.prefab` | the prefab the scene's `ShopScreen` instance comes from, and its row | **deleted** once the Garage instance replaces the `ShopScreen` instance |
| `Scene/Gameplay.unity` | Canvas 720×1280 reference, match width; `ShopScreen` instance (no overrides); `flow.shopRoot` / `flow.shopTabs` point into it; wrench on `BottomBar` → `flow.shopButton` → `EnterShop` | `ShopScreen` instance replaced by `GarageScreen`; three flow references |

## 0. Art

All eleven sprites are in `Textures/UI/Garage/`, already imported as *Sprite (2D and UI)* with 9-slice borders set. **Do not change the import settings.** Load them from one table of path constants at the top of the builder.

| Sprite | px | Border (L,B,R,T) | Image type | Used for |
|---|---|---|---|---|
| `UI_Shop_Frame` | 975×1436 | none | Simple | the whole panel: "SHOP" title tab, blue band for the tabs, garage-table preview window, dark list inset — all baked in |
| `Btn_Vehicle_Selected` / `Btn_Vehicle_Unselected` | 428×86 | 100,0,100,0 | Sliced | VEHICLES tab, text baked in — the tab button has **no label child** |
| `Btn_Ammo_Selected` / `Btn_Ammo_Unselected` | 428×85 | 120,0,120,0 | Sliced | AMMO tab, same |
| `UI_Shop_Ammo_Frame` | 796×148 | 140,0,150,0 | Sliced | row background for **both** tabs; the dark icon slot (px 20–135 × 18–131) is baked into the left border so it never stretches |
| `UI_Level_Fill` / `UI_Level_Unfilled` | 81×43 | 20,10,20,10 | Simple | one level pip; fixed size, so no slicing needed |
| `Btn_Buy` | 145×51 | 15,0,15,0 | Sliced | buy/upgrade button; the coin is baked at px 19–45, the price goes to its right |
| `UI_Locked` | 234×55 | none | Simple | replaces the level text on a locked row |
| `Btn_Esc` | 102×104 | none | Simple | close X, top-right |

Preview and list backgrounds are **part of the frame sprite**: there is no separate preview image, no list inset image, and no "GARAGE"/"SHOP" title label.

## 1. Data — `BulletDefinition` gets an icon

Mirror `VehicleDefinition` exactly so the two item views bind the same way:

```csharp
// inside BulletDefinition.Level
[Tooltip("Shown in the shop row and the preview. Left empty, the nearest lower level's icon is "
       + "used, so one sprite per ammunition is enough to ship.")]
public Sprite icon;

// on BulletDefinition
/// <summary>Icon for a level, walking down to lower levels when the slot is empty; null only when no level has one.</summary>
public Sprite ResolveIcon(int level);   // same loop as VehicleDefinition.ResolveIcon
```

Nothing else on the bullet side changes: `BulletLoadout` already exposes `Selected` / `SelectedLevel` / `GetLevel`, which is all the views need. Old assets deserialise with `icon: {fileID: 0}` — no migration.

Icon art does not exist yet. Convention for when it does, so nothing has to be dragged in by hand: `Textures/Items/Bullets/{bulletId}.png` and `Textures/Items/Vehicles/{vehicleId}.png` for level 1, optionally `{id}_L2.png`, `{id}_L3.png` for per-level looks. The builder (§8) fills any **empty** `levels[i].icon` from those paths and never overwrites one that is set. A null icon is handled everywhere: the row's `Icon` image and the preview image are disabled rather than drawn as a white square.

## 2. Screen structure

### 2.1 The prefab replaces `ShopScreen`

New prefab **`Prefabs/UI/Garage/GarageScreen.prefab`**, built from scratch — nothing is inherited from `BulletShopScreen.prefab`, which is in a half-migrated state (two sets of tab buttons, a `BulletShopView` on the root and another on `AmmoPanel`). Item prefabs go in the same folder.

In `Scene/Gameplay.unity` the `ShopScreen` prefab instance under the Canvas is **deleted** and a `GarageScreen` instance takes its place: same anchors `(0, 0.135)–(1, 1)`, inactive at edit time like the other screens, `flow.shopRoot` → the new root, `flow.shopTabs` → its `ShopTabsView`, `flow.closeShopButton` → its `CloseButton`. Deleting a whole instance from the scene is allowed (only *children* of an instance are not), and once it is gone the three references read as null, so the builder's `SetIfEmpty` fills them. Then delete `Prefabs/UI/BulletShop/BulletShopScreen.prefab` and `BulletTypeUpgradeView.prefab`; nothing else references them (checked: the AmmoPick screen's `rowPrefab` is empty and generates its own rows).

The flow is untouched: `GameState.Shop`, `EnterShop`, `EnterShopTab(int)`, the wrench button, the bottom bar's Home button all work exactly as before — the only difference is which prefab `shopRoot` points at. The field keeps its name (`shopRoot`, `[FormerlySerializedAs("bulletShopRoot")]`); renaming it buys nothing and risks the serialized reference.

### 2.2 Hierarchy

```
GarageScreen                    RectTransform (0,0.135)-(1,1)   ShopTabsView
├─ Frame                        Image UI_Shop_Frame, Simple, raycastTarget off
│  ├─ Tabs                      empty rect
│  │  ├─ VehicleTypeTab         Image Btn_Vehicle_Unselected (Sliced) + Button  — no label
│  │  └─ BulletTypeTab          Image Btn_Ammo_Unselected (Sliced) + Button     — no label
│  ├─ VehiclePanel              empty rect, stretch (0,0)-(1,1)   VehicleShopView
│  │  ├─ Preview                empty rect (the garage table is in the frame art)
│  │  │  ├─ PreviewItem         Image, preserveAspect, raycast off; disabled when no sprite
│  │  │  └─ PreviewCaption      TMP, centred, 22, white
│  │  └─ List                   empty rect (the inset is in the frame art) + ScrollRect: vertical only, Clamped, viewport=Viewport, content=Rows
│  │     └─ Viewport            RectMask2D, stretch
│  │        └─ Rows             VerticalLayoutGroup + ContentSizeFitter (vertical: preferred); anchors (0,1)-(1,1), pivot (0.5,1)
│  └─ AmmoPanel                 same three children as VehiclePanel            BulletShopView
├─ MoneyChip                    Image UI_Money (MainMenu), raycast off
│  └─ GoldLabel                 TMP "0", 34, centred, anchors (0.2,0.1)-(0.82,0.9)
└─ CloseButton                  Image Btn_Esc, preserveAspect + Button
```

`ShopTabsView.tabs`: index 0 = `Vehicles` → `VehicleTypeTab` / `VehiclePanel`, index 1 = `Ammunition` → `BulletTypeTab` / `AmmoPanel`, so `GameFlowController.EnterShopTab(0)` is vehicles and `(1)` is ammo, matching left-to-right. `defaultTab = 1` keeps today's behaviour (the shop opens on ammo); it is one number if that should change.

### 2.3 Geometry

Canvas: 720×1280 reference, match width. The mock (`Garage_Ammo.png`, 1217×1922) draws the frame sprite at 1:1, so every fraction below is the sprite's own pixel geometry, and the frame can be any size at the sprite's aspect (975:1436) without anything drifting. **Anchor fractions are given as (xMin, yMin)–(xMax, yMax) of the parent, y measured from the bottom**, ready for `RectTransform.anchorMin/anchorMax` with zero offsets.

Screen-level (children of `GarageScreen`):

| Object | Placement |
|---|---|
| `Frame` | anchorMin = anchorMax = (0.5, 1), pivot (0.5, 1), anchoredPosition (0, −56), **sizeDelta (600, 884)** — the one number to tune; keep the ratio |
| `MoneyChip` | anchors (0.046, 0.926)–(0.268, 0.975) |
| `CloseButton` | anchors (0.867, 0.925)–(0.944, 0.976) |

Inside `Frame`:

| Object | Anchors of `Frame` | From the sprite (px) |
|---|---|---|
| `Tabs` | (0.048, 0.847)–(0.956, 0.907) | x 47–932, y 134–219: the blue band between title and preview |
| `VehicleTypeTab` | (0, 0)–(0.48, 1) of `Tabs` | 428 px wide at 1:1, gap of 29 px between the tabs |
| `BulletTypeTab` | (0.52, 0)–(1, 1) of `Tabs` | |
| `VehiclePanel`, `AmmoPanel` | (0, 0)–(1, 1) | |
| `Preview` | (0.047, 0.508)–(0.952, 0.829) | x 46–928, y 246–707: the light garage window |
| `PreviewItem` | (0.36, 0.31)–(0.64, 0.84) of `Preview` | the ball in the mock: ~250 px, centred over the table, resting just above it |
| `PreviewCaption` | (0.05, 0.03)–(0.95, 0.15) of `Preview` | over the hazard stripe along the table's front edge |
| `List` | (0.047, 0.038)–(0.953, 0.474) | x 46–929, y 755–1382: the dark inset |
| `Rows` layout | padding L 26, R 26, T 20, B 20; spacing 30; childAlignment UpperCenter; control/force-expand all **off** (rows keep their prefab size) | mock: rows 47 px in from the inset, 33 px down, 49 px apart |

At 600×884 the tabs come out at 261×53 (sprite 428×85 — near-native, so the slicing barely stretches), the preview item ~150 px square, and the inset holds exactly three rows (20 + 3×91 + 2×30 + 20 = 373 of 385) like the mock; a fourth scrolls.

## 3. The item row — `ShopItemView`, `BulletTypeViewItem`, `VehicleTypeViewItem`

The two rows are the same picture with different data, so the parts live in one base and the two names the shops and prefabs use are thin subclasses over it. Both prefabs share the geometry table below.

### 3.1 Components — `Scripts/UI/ShopItemView.cs`, `BulletTypeViewItem.cs`, `VehicleTypeViewItem.cs`

```csharp
/// One row of the garage: what the item looks like, what it is called and how far it has been
/// taken, the strip of level pips, and the one button that buys or upgrades it. The shop decides
/// every word and every state; this only knows where to put them.
public abstract class ShopItemView : MonoBehaviour
{
    public readonly struct State
    {
        public readonly Sprite Icon;          // null → icon image disabled
        public readonly string DisplayName;
        public readonly bool Unlocked;
        public readonly int Level;            // 1-based; ignored when locked
        public readonly int MaxLevel;         // number of pips; >= 1
        public readonly bool Equipped;        // full alpha; everything else is dimmed
        public readonly string BuyCaption;    // "4,000", "MAX", "N/A"
        public readonly bool BuyInteractable;
        // constructor with all eight
    }

    [SerializeField] private Image icon;
    [SerializeField] private TMP_Text label;            // "TRUCK · LEVEL 2", or "TRUCK" beside the lock
    [SerializeField] private GameObject locked;         // UI_Locked
    [SerializeField] private UpgradeLevelBarView levels;
    [SerializeField] private Button buyButton;
    [SerializeField] private TMP_Text buyLabel;

    [Tooltip("On the row root. The equipped row is drawn at full strength and the rest are "
           + "knocked back; interactable is never touched, a dimmed row can still be bought.")]
    [SerializeField] private CanvasGroup group;
    [SerializeField, Range(0f, 1f)] private float equippedAlpha = 1f;
    [SerializeField, Range(0f, 1f)] private float otherAlpha = 0.5f;

    public Button BuyButton => buyButton;       // the shop wires the click

    public void Bind(in State state)
    {
        // icon.sprite / icon.enabled = sprite != null
        // locked.SetActive(!state.Unlocked)
        // label.text = Unlocked ? $"{NAME} · LEVEL {Level}" : NAME      (NAME = DisplayName.ToUpperInvariant())
        // levels.Bind(state.MaxLevel, state.Unlocked ? state.Level : 0)
        // buyLabel.text = BuyCaption; buyButton.interactable = BuyInteractable
        // group.alpha = state.Equipped ? equippedAlpha : otherAlpha
    }

    // ResolveMissingReferences() by child name — Icon, Label, Locked, Levels, Buy (+ its TMP);
    // group = GetComponent<CanvasGroup>() on the row itself. Called from Reset, OnValidate and
    // Awake, never overwriting a reference set by hand — same as VehicleShopRowView.
}

public sealed class BulletTypeViewItem : ShopItemView
{
    /// Icon comes from the definition at the shown level; a locked bullet shows its level-1 look.
    public void Bind(BulletDefinition bullet, int level, int maxLevel, bool unlocked, bool equipped,
                     string buyCaption, bool buyInteractable)
        => Bind(new State(bullet.ResolveIcon(unlocked ? level : 1), bullet.DisplayName, unlocked, level, maxLevel,
                          equipped, buyCaption, buyInteractable));
}

public sealed class VehicleTypeViewItem : ShopItemView
{
    public void Bind(VehicleDefinition vehicle, int level, int maxLevel, bool unlocked, bool equipped,
                     string buyCaption, bool buyInteractable)
        => Bind(new State(vehicle.ResolveIcon(unlocked ? level : 1), vehicle.DisplayName, unlocked, level, maxLevel,
                          equipped, buyCaption, buyInteractable));
}
```

If the TMP default font lacks the middle dot (U+00B7), use ` - `.

### 3.2 Row prefabs — `Prefabs/UI/Garage/BulletTypeViewItem.prefab`, `VehicleTypeViewItem.prefab`

Root: `RectTransform` anchorMin = anchorMax = (0.5, 1), **sizeDelta (490, 91)** (796×148 at the frame's 600/975 scale, so the 9-slice stays near 1:1), a `CanvasGroup`, and the item component. No Button on the root. Children, anchors as fractions of the row, y from the bottom (sprite px in the last column, y from the top):

| Child | Component | Anchors | Sprite px |
|---|---|---|---|
| `Frame` | Image `UI_Shop_Ammo_Frame`, Sliced, raycast off | stretch (0,0)–(1,1) | |
| `Icon` | Image, preserveAspect, raycast off | (0.053, 0.25)–(0.143, 0.74) | 72 px square centred in the slot (20–135 × 18–131) |
| `Header` | HorizontalLayoutGroup: spacing 8, MiddleLeft, control width/height on, force-expand off | (0.214, 0.45)–(0.97, 0.80) | text band y 30–82 |
| `Header/Locked` | Image `UI_Locked`, preserveAspect, LayoutElement preferred 144×34; active only when locked | (layout) | 234×55 |
| `Header/Label` | TMP bold, 26, white, left, wrapping off, overflow Ellipsis | (layout) | cap height ~37 px in the mock |
| `Levels` | `UpgradeLevelBarView` + HorizontalLayoutGroup: spacing 3, MiddleLeft, control/expand off | (0.214, 0.06)–(0.754, 0.36) | pips y 98–136, x from 170, 5 px apart; the strip is a little taller than the pips so their shadow edge is not cut |
| `Buy` | Image `Btn_Buy`, Sliced + Button (ColorTint; the default disabled grey is the "MAX"/unaffordable look) | (0.786, 0.074)–(0.969, 0.412) | 145×51 at x 626, y 87 |
| `Buy/Price` | TMP bold, 16, white, centred | (0.30, 0.05)–(0.97, 0.95) of `Buy` | right of the baked coin |

The `Header` is a layout group so that switching `Locked` on pushes the name to the right of the graphic and switching it off gives the name the whole line; inactive children are skipped by Unity's layout, so no anchors move. The header runs to 0.97 on purpose: it sits above the Buy button (y 0.45–0.80 vs 0.07–0.41), so a long name such as `CANNON BALL · LEVEL 2` has the room and never collides.

## 4. Level pips — `UpgradeLevelView` and `UpgradeLevelBarView`

`Prefabs/UI/Garage/UpgradeLevelView.prefab` — root `RectTransform` **50×26** (81×43 at the frame scale) with:

```csharp
/// One level of an item: lit when the player has reached it. Two images rather than a swap so
/// the lit pip can fade in later without the prefab changing.
public sealed class UpgradeLevelView : MonoBehaviour
{
    [SerializeField] private Image unfilled;  // child "Unfilled": UI_Level_Unfilled, Simple, stretch, always drawn
    [SerializeField] private Image fill;      // child "Fill":     UI_Level_Fill,     Simple, stretch, on top

    public bool IsFilled => fill != null && fill.enabled;
    public void SetFilled(bool filled) { if (fill != null) fill.enabled = filled; }
    // ResolveMissingReferences by child name from Reset / OnValidate / Awake
}
```

`Scripts/UI/UpgradeLevelBarView.cs` sits on the row's `Levels` object and owns the count, so both item views share it instead of each spawning pips:

```csharp
public sealed class UpgradeLevelBarView : MonoBehaviour
{
    [SerializeField] private UpgradeLevelView pipPrefab;
    [Tooltip("Pips are spawned under here. Left empty, this transform.")]
    [SerializeField] private RectTransform container;

    private const string PipNamePrefix = "Pip_";
    private readonly List<UpgradeLevelView> pips = new List<UpgradeLevelView>();

    /// total = number of pips (>= 1 in practice, 0 tolerated), current = how many are lit,
    /// clamped to [0, total]. Existing pips are reused and only the difference is spawned or
    /// destroyed; leftovers from before an assembly reload are found again by the Pip_ prefix,
    /// the same trick the shop views use for rows.
    public void Bind(int total, int current);
}
```

`total` is `economy.GetMaxLevel(bullet)` / `GetVehicleMaxLevel(vehicle)` — the same ceiling the old rows print as "Lv n / max", i.e. min(defined levels, priced levels) — so a pip is never drawn for a level that cannot be bought. Two pips for today's Rock and Cannon Ball, three for Truck and Tank; the five in the mock are decorative.

## 5. Tabs — `ShopTabsView`

Add to `Tab`:

```csharp
[Tooltip("Optional. When both are set the tab swaps sprites instead of tinting, and the "
       + "tint colours are ignored for it. The text is part of these sprites.")]
public Sprite selectedSprite;
public Sprite unselectedSprite;
```

In `ApplyTint` (rename `ApplyState`): if the button's `targetGraphic` is an `Image` and both sprites are set → `image.sprite = selected ? selectedSprite : unselectedSprite; image.color = Color.white;` and skip the label tint (there is no label). Otherwise the existing tint path runs unchanged, so any other tab strip keeps working. Set `image.type = Image.Type.Sliced` once in the builder, not here. The `Button`'s own transition stays ColorTint for press feedback.

## 6. The shop views

### 6.1 Row states — the same table for both tabs

`price` is formatted `ToString("N0", CultureInfo.InvariantCulture)` → `4,000`; the coin is already on the button, so no prefix. `Equipped` = `loadout.Selected == item`, which both loadouts always resolve (falling back to the default), so exactly one row per tab is at full alpha.

| State | Level line | Pips | Buy caption / interactable | Alpha |
|---|---|---|---|---|
| locked, priced | `🔒LOCKED  NAME` | 0 of max lit | purchase price / `CanPurchase*` | 0.5 |
| locked, not priced | same | same | `N/A` / off | 0.5 |
| owned, below max, priced | `NAME · LEVEL n` | n of max | upgrade price / `CanUpgrade*` | 1 if equipped else 0.5 |
| owned, below max, unpriced (authoring gap) | same | same | `N/A` / off | same |
| owned, at max | same | all lit | `MAX` / off | same |

### 6.2 `BulletShopView`

- New fields: `[SerializeField] private Image previewImage; [SerializeField] private TMP_Text previewCaption;` (both optional — the view works with neither).
- `OnEnable`/`OnDisable`: also subscribe/unsubscribe `catalogue.SelectionChanged` (→ `Refresh`) and `UserData.Changed` (→ `Refresh`), as `VehicleShopView` already does. The equipped row and the preview follow a choice made on the AmmoPick screen.
- `CreateRow`: check for a `BulletTypeViewItem` **before** the `BulletTypeUpgradeView` check. When present, the `Row` records it (`Item` field) and `Action = item.BuyButton`. The old typed and untyped paths stay exactly as they are; the generated fallback row is unchanged.
- `RefreshRow`: the item path comes first — compute the §6.1 state and call `row.Item.Bind(...)`, then `return`.
- `Refresh`: after the rows, `RefreshPreview()`: `selected = catalogue.Selected`; `previewImage.sprite = selected?.ResolveIcon(catalogue.SelectedLevel)`, `enabled = sprite != null`; `previewCaption.text = selected?.GetLevel(level)?.displayName?.ToUpperInvariant() ?? ""` (e.g. `ROCK II`).

### 6.3 `VehicleShopView`

- Same two preview fields, same `RefreshPreview`, caption `TRUCK II · DAMAGE ×1.20` (`GetDamageMultiplier(level):0.00`) — this is where the multiplier now lives, since the row no longer prints it and the player still has to see what a level is worth.
- `CreateRow`: check for `VehicleTypeViewItem` **first**; when present do **not** add a `VehicleShopRowView`. `Row` records the item; wire `item.BuyButton` → `HandlePrimaryClicked`. The `VehicleShopRowView` path and the generated two-button row remain the fallback when `rowPrefab` is empty; `HandleSelectClicked` stays for that fallback and is not wired to the new row.
- `RefreshRow`: item path first, §6.1 state, `row.Item.Bind(...)`, return.
- Nothing to add for the equip-on-buy: `TryPurchaseVehicle` raises the loadout's `SelectionChanged`, which this view already listens to.

### 6.4 `EconomyService.TryPurchaseVehicle`

Mirror the shape `TryUpgradeVehicle` already has — the loadout's own save is what commits the transaction — so the charge, the unlock and the selection still land in one write:

```csharp
UserData.Vehicles.Unlock(vehicle.Id);

// Buying is the only way a vehicle is equipped: the garage has no equip button. Select saves,
// which commits the charge and the unlock above along with the choice, and raises
// SelectionChanged so the mount swaps the model and the vehicle tab re-dims its rows without
// either being told. False means the record already named this vehicle, in which case nothing
// has saved yet and the plain write below does it.
if (vehicleLoadout == null || !vehicleLoadout.Select(vehicle))
{
    UserData.Save();
}

GoldChanged?.Invoke();
return true;
```

The bullet purchase path is **not** changed — ammo is chosen on the pick screen.

## 7. Flow — `GameFlowController`

`[SerializeField] private Button closeShopButton;` wired in `OnEnable`/`OnDisable` to `GoBack` like the others (`GoBack` already maps `Shop` → `ReturnToMainMenu`). The bottom bar's Home button keeps working; the X is a second way out, not a replacement.

Everything else in the flow stays: wrench → `EnterShop` → `GameState.Shop` → `shopRoot` (now the `GarageScreen` instance) shown with the bottom bar, as today.

## 8. Editor tooling — `Editor/GarageScreenBuilder.cs`

`[MenuItem("Tools/Smashdown/Build Garage Screen")]`, idempotent in the house style: fill what is missing, never overwrite what is set. Steps:

1. Ensure `Prefabs/UI/Garage/`.
2. Build `UpgradeLevelView.prefab`, then `BulletTypeViewItem.prefab` and `VehicleTypeViewItem.prefab` (§3.2, §4) with `PrefabUtility.SaveAsPrefabAsset`. If one already exists, `LoadPrefabContents` and run the same Ensure steps over it instead of replacing it.
3. Build `GarageScreen.prefab` the same way (§2.2/§2.3), then wire with `SetIfEmpty`: on `ShopTabsView` the two entries plus sprites and `defaultTab`; on each shop view `economy` (`LoadFirstAsset<EconomyService>`), `loadout`, `container = Rows`, `rowPrefab`, `goldLabel`, `previewImage`, `previewCaption`. `SaveAsPrefabAsset`, `UnloadPrefabContents`.
4. Icons by convention (§1): for every `BulletDefinition` / `VehicleDefinition` in the project, fill empty `levels[i].icon` from `Textures/Items/...` if the file exists.
5. Scene: under the Canvas, destroy `ShopScreen` if it is still there (whole instance — allowed), then if `GarageScreen` is absent `PrefabUtility.InstantiatePrefab` the prefab, name it `GarageScreen`, anchors `(0, 0.135)–(1, 1)`, inactive. `SetIfEmpty` on the `GameFlowController`: `shopRoot`, `shopTabs`, `closeShopButton`. Mark the scene dirty.
6. Delete `Prefabs/UI/BulletShop/BulletShopScreen.prefab` and `BulletTypeUpgradeView.prefab` (`AssetDatabase.DeleteAsset`) once the scene no longer references them — step 5 first, then this.

The `UiBuilder` helpers it needs that are still `private` (`EnsureSpriteImage`, `EnsureSpriteButton`, `EnsureColorImage`, `LoadSprite`, `LoadFirstAsset`) become `internal`. In `UiBuilder.Screens.cs`, `BuildShop` is reduced to: if `GarageScreen` is absent, instantiate the prefab as in step 5; delete `BuildVehicleSection`, `BuildBulletSection`, `EnsureGaragePage`, `FillTab`, the `RenameIfPresent(... "BulletShopScreen", "ShopScreen")` line, the `DestroyIfNotPrefabInstance(... "VehicleShopScreen")` line and the `Garage*` colours — the prefab is now the only description of the garage, and two descriptions is how the last bug happened. Leave `EnsureShopPanel` (the IAP shop still uses it).

Nothing in `Rebuild`/`Refresh` of the views depends on the builder: a prefab authored by hand to the same names and components works identically.

## 9. Constraints

- No change to the loadouts, the price configs, `UserData`, or `VehicleDefinition`. `BulletDefinition` gains only the icon field and `ResolveIcon`; `EconomyService` gains only the `Select` call in `TryPurchaseVehicle`.
- `EconomyService` stays the only place gold is spent; the views never call `Select` on either loadout.
- Every subscriber unsubscribes in `OnDisable`; rows and pips are rebuilt in `OnEnable`, matched by name prefix after a domain reload (existing pattern).
- Decorative images have `raycastTarget` off; only Buy, the tabs and Close receive taps.
- `CanvasGroup.interactable` and `blocksRaycasts` on rows are never changed — alpha only.
- No LINQ in runtime paths; XML doc comments that explain *why*; `Try*` returns bool.
- Do not touch the sprite import settings, and do not add sprites that are not in the table in §0.

## 10. Acceptance criteria

1. **Build from clean.** On a fresh checkout of `Feature/GarageUI`, `Tools > Smashdown > Build Garage Screen` produces `Prefabs/UI/Garage/GarageScreen.prefab` and the three item prefabs, replaces the scene's `ShopScreen` instance with a `GarageScreen` instance that has **no overrides**, sets `flow.shopRoot`, `shopTabs`, `closeShopButton`, and deletes the two old prefabs. Running it a second time changes nothing (git diff clean after the first run).
2. **Flow unchanged.** The wrench on the bottom bar opens the Garage in `GameState.Shop`; Home and the X both return to the main menu; `EnterShopTab(0)` opens vehicles, `(1)` ammo.
3. **Frame.** The frame art is centred, title tab at the top, gold chip top-left with the current gold, close X top-right. Nothing overlaps the bottom bar at 720×1280 or 720×1560.
4. **Tabs.** The open tab shows its `*_Selected` sprite, the other its `*_Unselected`; tapping swaps sprites and panels; the tab that was open is still open after closing and reopening (existing behaviour, still true).
5. **Ammo rows.** One row per bullet in the loadout. Rock (owned, level 1 of 2, equipped by default): `ROCK · LEVEL 1`, two pips, one lit, full alpha, Buy = the level-2 price formatted `N0`, dimmed when unaffordable. Cannon Ball (locked): alpha 0.5, the LOCKED graphic followed by `CANNON BALL`, two pips unlit, Buy = purchase price. No icon sprite assigned → no white square, just the slot.
6. **Buying and upgrading.** Tapping Buy on the locked row unlocks it (gold drops, LOCKED disappears, one pip lights, caption becomes the upgrade price, alpha stays 0.5 — buying ammo does not equip it). Upgrading to the last level lights every pip and the button reads `MAX`, dimmed. A refused tap (unaffordable) changes nothing. A dimmed row's Buy button still works.
7. **Equipped row follows the pick screen.** Choose Cannon Ball on the AmmoPick screen, open the Garage: its row is at full alpha, Rock's at 0.5, and the preview shows Cannon Ball's sprite (or nothing) with caption `CANNON I`. Rows never change alpha from a tap inside the Garage.
8. **Vehicles.** Truck equipped at full alpha, Tank locked at 0.5. Buying the Tank unlocks **and equips** it in one tap: Tank goes to full alpha, Truck to 0.5, the preview caption reads `TANK I · DAMAGE ×1.30`, and `VehicleMount` swaps the model (or logs its no-model warning) without leaving the screen. Upgrading the equipped vehicle updates the caption's multiplier.
9. **Rows never re-flow.** Every row is 490×91 in every state; a catalogue of four entries scrolls inside the inset and nothing spills over the frame.
10. **Domain reload off.** Entering play mode twice does not duplicate pips, rows or listeners.
11. **No icons, no crash.** With every `icon` empty, all of the above holds with the icon and preview images simply disabled.

## 11. Out of scope

- A 3D preview of the vehicle model (render-texture rig with `VehicleMount`), the "+" on the gold chip opening the IAP shop, the mission chip, the bottom bar's raised "SHOP/GARAGE" slot art.
- Equipping anything from the Garage, a vehicle pick screen, animations, transitions, SFX.
- Creating icon art; balance; deleting `BulletTypeUpgradeView.cs` / `VehicleShopRowView.cs` (the scripts remain as the fallback rows; only the old prefabs go).
