# Task Brief 07 — Bottom bar: flat bar art, raised slot on the selected tab

## Goal

Re-skin the bottom tab bar with the supplied art. The bar is `UI_Bottom` (a flat blue strip with three slots); the slot for the screen the player is on is **raised** by switching on a `UI_Bottom_Btn` behind it and showing its label; the other two slots show only their icon. Which slot is raised follows `GameFlowController.State`: **SHOP** (left, store icon) for `IapShop`, **HOME** (middle) for `MainMenu`, **GARAGE** (right, wrench) for `Shop`. On any other state where the bar is visible nothing is raised.

References: `Assets/GameJam/RefAI/ref_botBar_home_selected.png`, `ref_botBar_shop_selected.png`, `ref_botBar_garage_selected.png` — the same bar in its three states.

Decisions already made (do not re-open):

- **Labels are TMP text**, shown only on the raised slot. `Btn_Home.png` currently has "HOME" baked in; the icon is used **without** the text (§0). `Btn_Shop` and `Btn_Setting_Vehicle` are icon-only already.
- **The bar is purely visual.** The three Buttons stay the ones `GameFlowController` already references (`iapShopButton`, `homeButton`, `shopButton`); nothing in the flow changes. A new `BottomBarView` listens to `flow.StateChanged` and only moves pixels.
- **Slot buttons are the whole third of the bar**, including the area the raised slot grows into, so the tap target does not change size with the state.
- The bar root keeps its anchors `(0, 0)–(1, 0.135)`; every menu screen is already laid out above that line. The art inside is bottom-anchored at its own aspect and is shorter than the root; the difference is where the raised slot sticks up.

House rules (from Brief 06, unchanged): the layout is authored in the prefab by an idempotent editor menu item — fill what is missing, never overwrite what is set; anchor fractions are `(xMin, yMin)–(xMax, yMax)` of the parent, y from the bottom; XML doc comments say *why*; every subscriber unsubscribes in `OnDisable`; no LINQ in runtime paths.

## Git

Branch **`Feature/BottomBar`** from `main`. One-line commit messages, no body, no trailers, in the repo's voice. Commit per logical step.

## Repository

`/Volumes/Supercent/FierceTigerGameJam/FierceTigerGameJam` — paths relative to `Assets/GameJam/`.

| Existing | Role | Touched |
|---|---|---|
| `Prefabs/UI/MainMenu/BottomBar.prefab` | root `(0,0)–(1,0.135)`; children `Panel` (Image `UI_MainMenu_BottomPanel`), `IapShopButton` (Image `Btn_Shop` + Button), `HomeButton` (transparent Image + Button), `BulletShopButton` (Image `Btn_Setting_Vehicle` + Button) | rebuilt in place — **the three Button components are kept** (the scene's flow references them) |
| `Scene/Gameplay.unity` | `BottomBar` prefab instance; `flow.bottomBarRoot`, `iapShopButton`, `homeButton`, `shopButton` point into it | no new references |
| `Scripts/Gameplay/Flow/GameFlowController.cs` | `public event Action<GameState> StateChanged`, `public GameState State`, nested `public enum GameState` | untouched |
| `Scripts/UI/ShopTabsView.cs` | the tab-strip pattern (index-captured listeners, `OnEnable` apply) | untouched; pattern to mirror |
| `Editor/UiBuilder.Screens.cs` → `BuildBottomBar` | builds the old bar (sprite `UI_MainMenu_BottomPanel`, invisible hit areas, `RenameIfPresent BulletShopButton → ShopButton`) | reduced to "ensure the prefab instance exists" |
| `Editor/UiBuilder.cs` | `EnsureRect`, `Ensure<T>`, `SetIfEmpty`, `EnsureLabel`; `EnsureSpriteImage`/`LoadSprite` (made `internal` by Brief 06) | reused |

## 0. Art — `Textures/UI/MainMenu/`

| Sprite | px | Import | Used for |
|---|---|---|---|
| `UI_Bottom` | 1216×224 | Sprite, no border, Simple | the bar; full width, bottom-anchored |
| `UI_Bottom_Btn` | 486×286 | Sprite, Simple | the raised slot; one per slot, inactive unless selected |
| `Btn_Shop` | 130×127 | Sprite | left slot icon (IAP shop) |
| `Btn_Home` | 131×195 | **currently Default texture (textureType 0) — must become Sprite (2D and UI)** | middle slot icon. The PNG is the house (rows 0–122) + a gap + the word HOME (rows 153–194). Use the house only, see below |
| `Btn_Setting_Vehicle` | 111×126 | Sprite | right slot icon (garage) |
| `UI_MainMenu_BottomPanel` | 1216×286 | Sprite | **no longer used** by the bar; leave the file |

`Btn_Home` icon-only, in order of preference: (a) the file is re-exported as the house alone (131×123) — then it is used as a single sprite; (b) otherwise the builder slices it: `TextureImporter` → `textureType = Sprite`, `spriteImportMode = Multiple`, two sprites `Btn_Home_Icon` rect `(x 0, y 72, w 131, h 123)` and `Btn_Home_Label` rect `(0, 0, 131, 42)` (Unity rects are bottom-left origin), `SaveAndReimport`, then load the `Btn_Home_Icon` sub-sprite. Do this only when no sub-sprite named `Btn_Home_Icon` exists yet and the texture is still 131×195. The legacy `TextureImporter.spritesheet` API is fine here (editor-only, wrap in `#pragma warning disable 618`), or use `ISpriteEditorDataProvider` if the 2D Sprite package is present. Whichever path, also set `Btn_Home` to Sprite (2D and UI) — it is the only UI texture in the project still imported as Default.

## 1. Geometry

Canvas 720×1280 reference, match width. The bar root is 720×173 (`0.135` of 1280). Mock scale: 1216 px → 720 units (×0.592).

| Object | Placement | Notes |
|---|---|---|
| `Bar` | anchors `(0,0)–(1,0)`, pivot `(0.5,0)`, sizeDelta `(0, 133)` | 1216×224 at 720 wide is 132.6 tall; stretches with width, keeps height |
| `ShopSlot`, `HomeSlot`, `GarageSlot` | anchors `(i/3, 0)–((i+1)/3, 1)` of the root, i = 0,1,2 | each 240×173; transparent Image (`color (1,1,1,0)`, raycastTarget **on**) + the existing Button |
| `Raised` (child of each slot) | anchorMin = anchorMax `(0.5, 0)`, pivot `(0.5, 0)`, anchoredPosition `(0,0)`, sizeDelta `(288, 169)` | 486×286 ×0.592; wider than its slot on purpose — the mock's raised slot spans ~39 % of the width. Image `UI_Bottom_Btn`, raycast off, **inactive by default** |
| `Icon` (child of each slot) | anchorMin = anchorMax `(0.5, 0)`, pivot `(0.5, 0.5)`, sizeDelta `(64, 64)`, anchoredPosition `(0, 66)` at rest / `(0, 106)` raised | Image, preserveAspect, raycast off |
| `Label` (child of each slot) | anchorMin = anchorMax `(0.5, 0)`, pivot `(0.5, 0.5)`, sizeDelta `(220, 28)`, anchoredPosition `(0, 46)` | TMP bold 22, white, centred, text SHOP / HOME / GARAGE; raycast off; **inactive by default** |

Draw order inside a slot: `Raised`, then `Icon`, then `Label`. Slots after the bar, so a raised slot paints over the bar's top edge like the mock.

Rename the existing objects rather than recreating them, so the Button components (and the scene's references to them) survive: `IapShopButton` → `ShopSlot`, `HomeButton` → `HomeSlot`, `BulletShopButton` → `GarageSlot`, `Panel` → `Bar`. The old sprites on the slot images are cleared (the icon moves to the `Icon` child) and the slot image is made transparent.

## 2. `BottomBarView` — `Scripts/UI/BottomBarView.cs`

```csharp
/// <summary>
/// Raises the slot of the screen the player is on. Purely visual: the buttons and what they do
/// belong to <see cref="GameFlowController"/>, and this only follows its state, so a screen that
/// is reached some other way (a debug menu, a deep link) still lights the right slot.
/// </summary>
[DisallowMultipleComponent]
public sealed class BottomBarView : MonoBehaviour
{
    [Serializable]
    public sealed class Slot
    {
        [Tooltip("Raised while the flow is in this state.")]
        public GameFlowController.GameState state;
        public GameObject raised;          // UI_Bottom_Btn
        public RectTransform icon;
        public GameObject label;
    }

    [SerializeField] private GameFlowController flow;
    [SerializeField] private Slot[] slots = Array.Empty<Slot>();
    [SerializeField] private float iconRestY = 66f;
    [SerializeField] private float iconRaisedY = 106f;

    private void OnEnable()  { if (flow != null) { flow.StateChanged += Apply; Apply(flow.State); } else { Apply(null); } }
    private void OnDisable() { if (flow != null) flow.StateChanged -= Apply; }

    /// <summary>Exactly the matching slot is raised; with no match, none is (the mission screen).</summary>
    private void Apply(GameFlowController.GameState state) => Apply((GameFlowController.GameState?)state);

    private void Apply(GameFlowController.GameState? state)
    {
        for (int i = 0; i < slots.Length; i++)
        {
            Slot slot = slots[i];
            if (slot == null) continue;
            bool selected = state.HasValue && slot.state == state.Value;
            if (slot.raised != null && slot.raised.activeSelf != selected) slot.raised.SetActive(selected);
            if (slot.label != null && slot.label.activeSelf != selected) slot.label.SetActive(selected);
            if (slot.icon != null) slot.icon.anchoredPosition = new Vector2(0f, selected ? iconRaisedY : iconRestY);
        }
    }
}
```

`flow` is found with `SetIfEmpty` from the scene's `GameFlowController` by the builder; when it is null (a test scene) every slot stays flat.

## 3. Editor — `Editor/BottomBarBuilder.cs`

`[MenuItem("Tools/Smashdown/Build Bottom Bar")]`:

1. §0 (b) slicing of `Btn_Home` if needed.
2. `LoadPrefabContents(Prefabs/UI/MainMenu/BottomBar.prefab)`; rename the four objects (§1); on `Bar` set the sprite to `UI_Bottom`, `Image.Type.Simple`, and the anchors from §1 (set them explicitly — `EnsureRect` leaves an existing rect alone); on each slot set anchors explicitly, `Image.sprite = null`, `color = (1,1,1,0)`, `raycastTarget = true`; ensure `Raised`, `Icon`, `Label` children with the §1 values and sprites (`Btn_Shop`, `Btn_Home_Icon` or the re-exported `Btn_Home`, `Btn_Setting_Vehicle`); `Ensure<BottomBarView>` on the root and fill `slots` (3 entries: `IapShop`/`ShopSlot`, `MainMenu`/`HomeSlot`, `Shop`/`GarageSlot`) only when the array is empty. `SaveAsPrefabAsset`, `UnloadPrefabContents`.
3. Scene: if the Canvas has no `BottomBar`, instantiate the prefab there (anchors `(0,0)–(1,0.135)`); `SetIfEmpty` `flow` on the instance's `BottomBarView`; `SetIfEmpty` `bottomBarRoot`, `iapShopButton`, `homeButton`, `shopButton` on the flow from the instance's slots (they are already set in the shipped scene, so this only matters for a fresh scene). Mark the scene dirty.

`UiBuilder.BuildBottomBar` is cut down to step 3's "instantiate if absent" — its sprite and hit-area code and `RenameIfPresent(root, "BulletShopButton", "ShopButton")` go, since the prefab is now the description of the bar.

## 4. Acceptance criteria

1. On `main` menu the middle slot is raised with the house icon and HOME; opening the IAP shop raises the left slot (store icon, SHOP) and lowers the middle; the wrench raises the right slot (GARAGE). Icons of lowered slots sit centred in the bar with no label.
2. The raised slot pokes above the bar's top edge like the mock, and the whole third of the bar is tappable in both states.
3. `BottomBarView` has no scene references other than `flow`; the three Buttons are the same components the flow referenced before (check `flow.homeButton` still resolves after the rebuild — the GUID/fileID pairs in the scene must be untouched).
4. On a screen that is not a tab (mission panel, Brief 08) the bar shows with nothing raised.
5. `Btn_Home` is imported as Sprite; no texture in `Textures/UI` is still `textureType 0`.
6. Re-running the builder changes nothing; a fresh scene gets a working bar from the prefab alone.
7. Domain-reload-off: entering play mode twice does not double-subscribe to `StateChanged`.

## 5. Out of scope

Tap animation on the raised slot, badges/notification dots on slots, the bar hiding during transitions, any change to which states show the bar (Brief 08 adds the mission screen to that list).
