# Task Brief 08 — Mission panel: frame art, fixed banner, level cards with retry / play / locked

## Goal

Replace the map-select list with the **Mission screen** from the art: the `UI_Mission_Frame` panel, the fixed `UI_Mission_Banner` clipboard at the top of it, and under that a 3-column grid of level cards. Each card is a **`MissionProgressItemView`** on `UI_Level_Badge` with the level's name and one of three icons: **`Btn_Retry`** on a level the player has already cleared, **`Btn_Play_Small`** on the level they are currently on, **`Btn_Locked`** on a level not yet open. Retry and Play start that level (into the ammo pick, as today); Locked does nothing. Gold chip, mission chip and close X on top; the bottom bar underneath with no slot raised.

Reference: `Assets/GameJam/RefAI/ref_Mission.png`.

Decisions already made (do not re-open):

- **Unlock rule: level N is open once level N−1 is passed** (`UserMapProgressData.IsPassed`, i.e. it reached its `requiredClearPercent`; a full clear is not required). Level 1 is always open. Card state: passed → **Cleared** (retry); open and not passed → **Current** (play); everything else → **Locked**. With sequential play exactly one card is Current; if a save somehow has gaps, several may be, which is harmless.
- **Card title is `LEVEL n`**, n = position in `MapConfig` + 1. `MapInfo.DisplayName` is not shown.
- **Chrome as the ref**: gold chip top-left, mission chip (`3/10`, `MapProgressView`) top-right of the title, close X (`Btn_Esc`) top-right. **The bottom bar is shown** on this screen with nothing raised (Brief 07's `BottomBarView` does that by itself; without Brief 07 the old bar shows, which is fine).
- **The banner is fixed; only the grid scrolls.** Three rows fit; the 10-level plan needs a fourth, so the grid lives in a `ScrollRect`.
- **Tapping a card selects the map through `MapSelection.SelectByIndex`**, exactly what the old buttons did; the flow's `HandleMapSelected` then opens the ammo pick. `EnterMapSelection` already clears the selection on the way in, so replaying the level just played registers as a change.
- The screen root keeps the menu-screen anchors `(0, 0.135)–(1, 1)` and the frame is placed exactly like the Garage's (same 975×1436 frame family), so the two screens line up when switching between them.

House rules (from Brief 06, unchanged): layout authored in a prefab by an idempotent editor menu item; anchor fractions `(xMin, yMin)–(xMax, yMax)` of the parent, y from the bottom; every subscriber unsubscribes in `OnDisable`; rows/cards rebuilt in `OnEnable` and matched by a name prefix; decorative images `raycastTarget` off; XML doc comments say *why*; no LINQ in runtime paths.

## Git

Branch **`Feature/MissionPanel`** from `main`. One-line commits, no body, no trailers. Touches `GameFlowController` in two small places (§4); if Brief 09 is being merged around the same time, take theirs and re-apply these two.

## Repository

`/Volumes/Supercent/FierceTigerGameJam/FierceTigerGameJam` — paths relative to `Assets/GameJam/`.

| Existing | Role | Touched |
|---|---|---|
| `Scripts/Gameplay/Wall/MapListView.cs` + `Prefabs/UI/MapSelect/MapListView.prefab`, `SelectMapButton.prefab` | the old list: one button per map, tinted when selected | **deleted** once the Mission screen replaces the scene instance |
| `Scripts/Gameplay/Wall/MapSelection.cs` | `Config`, `SelectByIndex(int)`, `SelectionChanged`, `Clear()` | untouched |
| `Scripts/Gameplay/Wall/MapConfig.cs` | `Maps`, `Count`, `Get(i)`, `IndexOf`, `MapInfo.Id` | untouched |
| `Scripts/Data/UserMapProgressData.cs`, `UserData.Maps`, `UserData.Changed` | `IsPassed(mapId)` | untouched |
| `Scripts/UI/MapProgressView.cs` | the `3/10` chip (`mapConfig`, `label`, `format`) | reused on the mission chip |
| `Scripts/Gameplay/Flow/GameFlowController.cs` | `mapSelectionRoot`, `ResolveSelectionRoot()` (looks for a `MapListView` in the parents), `IsMenuState`, `backButton` shown on `MapSelection`/`AmmoPick`, `GoBack()` | §4 |
| `Scene/Gameplay.unity` | `MapListView` prefab instance = `flow.mapSelectionRoot`; `flow.backButton` assigned | instance replaced by `MissionScreen`; `mapSelectionRoot` + `closeMissionButton` re-pointed |
| `Prefabs/UI/Garage/GarageScreen.prefab` (Brief 06) | same frame placement, chips, close button | copy its numbers, not its objects |
| `Editor/UiBuilder.cs`, `UiBuilder.Screens.cs` | `EnsureRect`, `EnsureLabel`, `Ensure<T>`, `SetIfEmpty`, `EnsureSpriteImage`, `EnsureSpriteButton`, `LoadSprite`, `LoadFirstAsset` | reused |

## 0. Art — `Textures/UI/SelectMission/` (+ `MainMenu/UI_Money`, `MainMenu/UI_Mission`, `Garage/Btn_Esc`)

All imported as Sprite, no borders. Sizes are what the mock draws at 1:1.

| Sprite | px | Image type | Used for |
|---|---|---|---|
| `UI_Mission_Frame` | 975×1436 | Simple | the panel; "MISSION" title tab baked in; the dark inset is px x 46–929, y 149–1377 |
| `UI_Mission_Banner` | 759×310 | Simple, preserveAspect | the fixed clipboard ("Demolish and rebuild The City." and the CONTRACT stamp are baked in) |
| `UI_Level_Badge` | 232×209 | Simple | card background (clipboard with clip and hazard stripe) |
| `Btn_Retry`, `Btn_Play_Small`, `Btn_Locked` | 85×87 | Simple | the card's one button, sprite swapped by state |
| `UI_Money` + `UI_Mission` (MainMenu), `Btn_Esc` (Garage) | — | Simple | chrome |

## 1. Screen structure — `Prefabs/UI/Mission/MissionScreen.prefab`

```
MissionScreen                 RectTransform (0,0.135)-(1,1)     MissionPanelView
├─ Frame                      Image UI_Mission_Frame, Simple, raycast off
│  └─ Inset                   empty rect
│     ├─ Banner               Image UI_Mission_Banner, preserveAspect, raycast off
│     └─ List                 empty rect + ScrollRect (vertical only, Clamped, viewport=Viewport, content=Grid)
│        └─ Viewport          RectMask2D, stretch
│           └─ Grid           GridLayoutGroup + ContentSizeFitter (vertical: preferred); anchors (0,1)-(1,1), pivot (0.5,1)
├─ MoneyChip                  Image UI_Money, raycast off          (+ GoldView, economy wired)
│  └─ GoldLabel               TMP "0", 34, centred
├─ MissionChip                Image UI_Mission, preserveAspect, raycast off   MapProgressView
│  └─ MissionLabel            TMP "0/0", 34, right-aligned
└─ CloseButton                Image Btn_Esc, preserveAspect + Button
```

`MissionProgressItemView.prefab` (same folder):

```
MissionProgressItemView       RectTransform 143×129, anchorMin=anchorMax (0.5,1)     MissionProgressItemView
├─ Frame                      Image UI_Level_Badge, Simple, stretch, raycast off
├─ Title                      TMP bold 20, #7A4A1E (the mock's brown), centred, "LEVEL 1"
└─ Action                     Image (Btn_Retry / Btn_Play_Small / Btn_Locked), preserveAspect + Button
```

### 1.1 Geometry

Canvas 720×1280, match width. The mock (1216×1922) draws the frame at 1:1, so fractions are the sprite's own pixels; the frame is placed exactly as the Garage's.

Screen level (children of `MissionScreen`, root is 720×1107):

| Object | Placement |
|---|---|
| `Frame` | anchorMin = anchorMax `(0.5, 1)`, pivot `(0.5, 1)`, anchoredPosition `(0, −56)`, **sizeDelta `(600, 884)`** — same as the Garage |
| `MoneyChip` | `(0.046, 0.926)–(0.268, 0.975)`; `GoldLabel` `(0.2, 0.1)–(0.82, 0.9)` of it |
| `MissionChip` | `(0.60, 0.926)–(0.84, 0.975)`, preserveAspect; `MissionLabel` `(0.42, 0.1)–(0.92, 0.9)` of it |
| `CloseButton` | `(0.867, 0.925)–(0.944, 0.976)` |

Inside `Frame` (sprite px → fraction; y from the bottom):

| Object | Anchors | From the sprite / mock |
|---|---|---|
| `Inset` | `(0.047, 0.040)–(0.953, 0.896)` of `Frame` | px x 46–929, y 149–1377 → 544×757 units |
| `Banner` | `(0.071, 0.697)–(0.929, 0.950)` of `Inset` | 759×310 → 467×191 units, centred, 38 units below the inset's top (mock: 61 px) |
| `List` | `(0.02, 0.02)–(0.98, 0.650)` of `Inset` | starts 36 units under the banner; 477 units tall = three rows exactly (3×129 + 2×33 = 453) |
| `Grid` | GridLayoutGroup: cellSize `(143, 129)`, spacing `(17, 33)`, `FixedColumnCount` 3, childAlignment `UpperCenter`, padding `(0, 0, 0, 10)` | mock cards 232×209 on a 260×262 pitch |

Inside a card (fractions of the 143×129 card; sprite px of 232×209):

| Object | Anchors | From the mock |
|---|---|---|
| `Title` | `(0.1, 0.62)–(0.9, 0.82)` | text centre 28 % down from the top; cap height ~28 px |
| `Action` | anchorMin = anchorMax `(0.5, 0.45)`, sizeDelta `(52, 54)` | 85×87 at 0.615, centre 55 % down |

## 2. `MissionProgressItemView` — `Scripts/UI/MissionProgressItemView.cs`

```csharp
public enum MissionItemState { Locked, Current, Cleared }

/// <summary>
/// One level on the mission board: its name and the one thing the player can do with it. The
/// card only shows a state; deciding it (what counts as cleared, what is open) is the panel's.
/// </summary>
public sealed class MissionProgressItemView : MonoBehaviour
{
    [SerializeField] private TMP_Text title;
    [SerializeField] private Button action;
    [SerializeField] private Image actionImage;        // action.targetGraphic
    [SerializeField] private Sprite retrySprite;       // Btn_Retry
    [SerializeField] private Sprite playSprite;        // Btn_Play_Small
    [SerializeField] private Sprite lockedSprite;      // Btn_Locked

    public Button Action => action;                    // the panel wires the click
    public MissionItemState State { get; private set; }

    public void Bind(string titleText, MissionItemState state)
    {
        // title.text = titleText
        // actionImage.sprite = state switch { Cleared → retrySprite, Current → playSprite, _ → lockedSprite }
        // action.interactable = state != Locked   (the Button's transition is None: the lock must not look
        //                                           greyed on top of already being a lock)
        // State = state
    }

    // ResolveMissingReferences() by child name (Title, Action) from Reset / OnValidate / Awake,
    // never overwriting a set reference — as VehicleShopRowView.
}
```

## 3. `MissionPanelView` — `Scripts/UI/MissionPanelView.cs`

Replaces `MapListView`. Same skeleton as `BulletShopView` (rebuild in `OnEnable`, `ItemNamePrefix = "Mission_"`, `ClearItems` by prefix, `DestroyItem` unparents before destroying):

```csharp
[SerializeField] private MapSelection mapSelection;   // the catalogue is mapSelection.Config
[SerializeField] private RectTransform container;     // Grid
[SerializeField] private MissionProgressItemView itemPrefab;

OnEnable:  UserData.Changed += Refresh; Rebuild();
OnDisable: UserData.Changed -= Refresh;

Rebuild(): one item per MapConfig.Get(i); item.Action.onClick → HandleItemClicked(i) (index captured per iteration); Refresh().
Refresh(): for each item i → item.Bind($"LEVEL {i + 1}", ResolveState(i)).

ResolveState(i):
    MapInfo map = config.Get(i);
    if (UserData.Maps.IsPassed(map.Id)) return Cleared;
    bool open = i == 0 || UserData.Maps.IsPassed(config.Get(i - 1).Id);
    return open ? Current : Locked;

HandleItemClicked(i): if (items[i].State == Locked) return; mapSelection.SelectByIndex(i);
```

`SelectByIndex` raising `SelectionChanged` is what moves the flow on (`HandleMapSelected` → `EnterAmmoPick`); the panel never talks to the flow. A locked card's Button is non-interactable anyway; the guard is for a click that arrives between a refresh and a rebuild.

## 4. Flow — `GameFlowController`

- `IsMenuState`: add `GameState.MapSelection`, so the bottom bar shows under the mission screen. `GoBack()` already maps `MapSelection` → `ReturnToMainMenu`.
- `backButton`: shown only on `AmmoPick` now (`state == GameState.AmmoPick`); the mission screen has the X and the bar's Home.
- New `[SerializeField] private Button closeMissionButton;` wired to `GoBack` in `OnEnable`/`OnDisable` like the others.
- `ResolveSelectionRoot()`: drop the `MapListView` lookup — return `mapSelectionRoot` (the `MissionScreen` instance). Delete `MapListView.cs`, its prefab and `SelectMapButton.prefab` in the same commit.

## 5. Editor — `Editor/MissionScreenBuilder.cs`

`[MenuItem("Tools/Smashdown/Build Mission Screen")]`, idempotent:

1. Ensure `Prefabs/UI/Mission/`. Build `MissionProgressItemView.prefab` (§1, sprites wired), then `MissionScreen.prefab`: hierarchy and §1.1 numbers; `MissionPanelView` wired with `mapSelection` (`LoadFirstAsset<MapSelection>`), `container = Grid`, `itemPrefab`; `GoldView` on `MoneyChip` (`economy`, `goldLabel`); `MapProgressView` on `MissionChip` (`mapConfig`, `label`). `SaveAsPrefabAsset`, `UnloadPrefabContents`.
2. Scene: destroy the `MapListView` instance under the Canvas if present (whole instance); if `MissionScreen` is absent, instantiate the prefab, name `MissionScreen`, anchors `(0, 0.135)–(1, 1)`, inactive. On the flow: `mapSelectionRoot` is re-pointed **explicitly** (it is not empty — it names the destroyed instance, which reads as null after the destroy, so `SetIfEmpty` works once step 2's destroy has run first); `SetIfEmpty` `closeMissionButton`. Mark the scene dirty.
3. `AssetDatabase.DeleteAsset` the two old prefabs; delete `MapListView.cs`.

`UiBuilder` has no map-select code of its own (the list was placed by hand), so nothing there changes.

## 6. Acceptance criteria

1. **Build from clean**: the menu item produces both prefabs, replaces the `MapListView` instance with a `MissionScreen` instance (no overrides), sets `flow.mapSelectionRoot` and `closeMissionButton`, deletes the old prefabs and script; a second run changes nothing.
2. **Fresh save**: PLAY on the main menu shows the mission screen: frame, banner, three cards `LEVEL 1` (play icon) `LEVEL 2` and `LEVEL 3` (locks); chips show `0` gold and `0/3`; the bottom bar is visible with no slot raised.
3. **Tapping LEVEL 1** opens the ammo pick for map `1`; a locked card does nothing; the X and the bar's Home return to the main menu.
4. **After passing level 1** (≥ 80 %): reopening the mission screen shows `LEVEL 1` with the retry icon, `LEVEL 2` with play, `LEVEL 3` locked; the mission chip reads `1/3`. Retry on LEVEL 1 opens its ammo pick again (the selection was cleared on entry, so re-picking the same map registers).
5. **Ten maps** in `MapConfig` (temporarily duplicate entries to test): four rows, the banner stays put, the grid scrolls inside the inset and nothing spills over the frame.
6. **Layout**: three cards per row, 143×129 each, titles and icons centred as in the ref; the mission screen's frame, chips and X sit exactly where the Garage's do.
7. **Domain-reload-off**: entering play twice neither duplicates cards nor double-subscribes.

## 7. Out of scope

Level thumbnails or a selected-card highlight, stars/percent per card, a map name under the title, animations, the "+" on the gold chip, and any change to the ammo-pick screen that follows.
