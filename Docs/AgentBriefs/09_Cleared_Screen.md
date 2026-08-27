# Task Brief 09 — Cleared screen: CLEAR! banner, reward, map picture, REPLAY / CONTINUE

## Goal

When a run **passes** its map, show the **Cleared screen** from the art instead of the plain `ResultScreen`: the `UI_Clear_Badge` banner, the reward as `UI_Coin` + `+200`, a picture of the map just cleared (a new sprite field on `MapConfig`), and two buttons — **REPLAY** (the same map's ammo pick, as today's RETRY) and **CONTINUE** (the next map's ammo pick). A close X returns to the main menu. A failed run keeps showing the old `ResultScreen` until Brief 10 replaces it.

Reference: `Assets/GameJam/RefAI/Win_ref.png`.

Decisions already made (do not re-open):

- **CONTINUE = the next map in `MapConfig` order**, straight into its ammo pick (`MapSelection.SelectByIndex(next)`, which the flow already answers with `EnterAmmoPick`). On the last map it returns to the main menu. The next map is open by construction (Brief 08's rule: passing N opens N+1).
- **The result split lives in the flow**: `resultRoot` becomes `failRoot` (`FormerlySerializedAs`, still the old `ResultScreen` for now) and `clearedRoot` is new; `HandleRunFinished` picks one by `result.Passed`. `RunFinished` keeps firing for both; each view shows only its own kind of result.
- **Map picture from `MapInfo.ClearedImage`** (new `Sprite` on the config entry). Empty → the image is disabled, nothing else changes. Convention for art: `Textures/Maps/{mapId}.png`, filled into empty slots by the builder, never overwriting one that is set.
- **Reward row hidden when the run paid nothing** (a repeat pass), as `RunResultView` does today; the count-up moves over with it. A perfect clear uses the same screen — there is one banner.
- **The structure stays standing behind a dim** (`Tutorial/Filter.png`), as the current result does; the confetti in the ref is not built.
- The three new buttons are wired by the flow through serialized fields, the pattern every other screen uses; the view never calls the flow.

House rules (from Brief 06, unchanged): prefab authored by an idempotent editor menu item; anchor fractions `(xMin, yMin)–(xMax, yMax)` of the parent, y from the bottom; subscribers unsubscribe in `OnDisable`; decorative images `raycastTarget` off; XML doc comments say *why*; no LINQ in runtime paths.

## Git

Branch **`Feature/ClearedScreen`** from `main`. One-line commits, no body, no trailers. **Brief 10 builds on this branch's flow changes — merge this first.** Brief 08 also touches `GameFlowController` (two small places); resolve in favour of both.

## Repository

`/Volumes/Supercent/FierceTigerGameJam/FierceTigerGameJam` — paths relative to `Assets/GameJam/`.

| Existing | Role | Touched |
|---|---|---|
| `Scripts/Gameplay/Flow/GameFlowController.cs` | `resultRoot`, `HandleRunFinished` (activates the root **before** raising `RunFinished`, which is what lets a view subscribe in `OnEnable` and still get the event), `RetryMap`, `ReturnToMainMenu`, `retryButton`, `resultContinueButton`, `mapSelection`, `HandleMapSelected` → `EnterAmmoPick` | §3 |
| `Scripts/Gameplay/Flow/LevelRunController.cs` | `RunResult { MapId, ClearPercent, Passed, FullyCleared, GoldAwarded }` | untouched |
| `Scripts/UI/RunResultView.cs` + `Prefabs/UI/ResultScreen/ResultScreen.prefab` | the plain result, with the reward count-up | stays as the fail screen until Brief 10; the count-up is copied, not moved |
| `Scripts/Gameplay/Wall/MapConfig.cs` → `MapInfo` | `id`, `displayName`, `mapJson` | gains `clearedImage` |
| `Scene/Gameplay.unity` | `ResultScreen` instance = `flow.resultRoot`; `retryButton`, `resultContinueButton` assigned | `ClearedScreen` instance added; four new flow references |
| `Editor/UiBuilder.cs` → `BuildResult`, `WireFlow` | builds the old result panel by hand | left alone; the old screen is a prefab instance now and the builder skips it |
| `Editor/UiBuilder.*` helpers | `EnsureRect`, `EnsureLabel`, `Ensure<T>`, `SetIfEmpty`, `EnsureSpriteImage`, `EnsureSpriteButton`, `LoadSprite`, `LoadFirstAsset` | reused |

## 0. Art

| Sprite | px | Image type | Used for |
|---|---|---|---|
| `Textures/UI/WinScreen/UI_Clear_Badge` | 814×610 (opaque rows 94–439) | Simple, preserveAspect | the banner; the sprite carries big transparent margins, the numbers below account for them |
| `Textures/UI/Common/UI_Coin` | 84×89 | Simple | reward coin |
| `Textures/UI/WinScreen/Btn_Retry_Long` | 418×162 | Simple, preserveAspect | REPLAY (text baked in) |
| `Textures/UI/WinScreen/Btn_Continue_Long` | 417×162 | Simple, preserveAspect | CONTINUE (text baked in) |
| `Textures/UI/Garage/Btn_Esc` | 102×104 | Simple | close X |
| `Textures/UI/Tutorial/Filter` | 1216×1920 | Simple | the dim over the scene |
| `Textures/Maps/{mapId}.png` | — | Simple, preserveAspect | the cleared map picture; does not exist yet |

The `20` ammo chip at the top-left of the ref is the in-run counter and is not part of this screen (the HUD is hidden on results).

## 1. Data — `MapInfo.clearedImage`

```csharp
[Tooltip("Shown on the cleared screen. Left empty, the screen shows the banner and reward alone.")]
[SerializeField] private Sprite clearedImage;
public Sprite ClearedImage => clearedImage;
```

Old `MapConfig.asset` entries deserialise with `clearedImage: {fileID: 0}` — no migration. `OnValidate` does not warn about an empty picture: it is expected until art lands.

## 2. Screen — `Prefabs/UI/Result/ClearedScreen.prefab`

```
ClearedScreen                 RectTransform (0,0)-(1,1)          ClearedScreenView
├─ Dim                        Image Filter, stretch, raycastTarget ON (swallows taps meant for the cannon)
├─ Badge                      Image UI_Clear_Badge, preserveAspect, raycast off
├─ Reward                     HorizontalLayoutGroup (spacing 12, MiddleCenter, control on, expand off); hidden when nothing was paid
│  ├─ Coin                    Image UI_Coin, LayoutElement preferred 50×53, raycast off
│  └─ Amount                  TMP bold 44, #FFC61A, "+200"
├─ MapImage                   Image, preserveAspect, raycast off; disabled when no sprite
├─ ReplayButton               Image Btn_Retry_Long, preserveAspect + Button
├─ ContinueButton             Image Btn_Continue_Long, preserveAspect + Button
└─ CloseButton                Image Btn_Esc, preserveAspect + Button
```

### 2.1 Geometry (root 720×1280; mock 1216×1920 → ×0.592)

| Object | Anchors / placement | From the mock |
|---|---|---|
| `Badge` | anchorMin = anchorMax `(0.5, 1)`, pivot `(0.5, 1)`, anchoredPosition `(0, −80)`, sizeDelta `(482, 361)` | sprite at 1:1 with its top at y 136; the ribbon lands at y 290–545 |
| `Reward` | `(0.30, 0.696)–(0.70, 0.743)` | coin + text centred on y 606, coin 84 px, digits ~60 px tall |
| `MapImage` | `(0.226, 0.229)–(0.777, 0.635)` | picture x 275–945, y 700–1480 |
| `ReplayButton` | `(0.132, 0.085)–(0.473, 0.168)` | x 160–575, y 1597–1757 (418×162 at 1:1) |
| `ContinueButton` | `(0.527, 0.085)–(0.868, 0.168)` | x 640–1055, same band |
| `CloseButton` | `(0.789, 0.906)–(0.865, 0.953)` | x 959–1052, y 90–181 |

## 3. Flow — `GameFlowController`

```csharp
[Tooltip("Shown when the run failed. Until Brief 10 this is the old ResultScreen.")]
[FormerlySerializedAs("resultRoot")]
[SerializeField] private GameObject failRoot;

[Tooltip("Shown when the run passed its map.")]
[SerializeField] private GameObject clearedRoot;

[Header("Cleared Screen Buttons")]
[SerializeField] private Button clearedReplayButton;     // → RetryMap
[SerializeField] private Button clearedContinueButton;   // → EnterNextMap
[SerializeField] private Button clearedCloseButton;      // → ReturnToMainMenu
```

- `OnEnable`/`OnDisable`: `Wire`/`Unwire` the three like the others. `retryButton` and `resultContinueButton` stay wired as they are (they belong to the old screen, which is now the fail screen).
- `HandleRunFinished`: `SetRootActive(result.Passed ? clearedRoot : failRoot, true)` in place of `SetRootActive(resultRoot, true)`; the rest unchanged (HUD off, `State = Result`, then `RunFinished?.Invoke(result)` — the order matters, see the Repository table).
- `Enter`: hide both roots where it hid `resultRoot`.
- New:

```csharp
/// <summary>
/// The next map in the catalogue, straight into its ammunition pick. Selecting raises
/// SelectionChanged, and HandleMapSelected does the rest, which is the same road a tap on the
/// mission board takes; there is no second way into a run. Past the last map there is nothing
/// to continue to, so the menu it is.
/// </summary>
[ContextMenu("Next Map")]
public void EnterNextMap()
{
    MapConfig config = mapSelection != null ? mapSelection.Config : null;
    int next = config != null && mapSelection.HasSelection ? config.IndexOf(mapSelection.Selected) + 1 : -1;

    if (config == null || next <= 0 || next >= config.Count)
    {
        ReturnToMainMenu();
        return;
    }

    mapSelection.SelectByIndex(next);
}
```

`HasSelection` is still true on the result screen (the selection is only cleared on the way *into* the mission screen), and `SelectByIndex` raises because the index differs. `EnterAmmoPick` → `Enter(AmmoPick)` tears the finished run down, as it does for RETRY.

## 4. `ClearedScreenView` — `Scripts/UI/ClearedScreenView.cs`

```csharp
/// <summary>
/// What a passed run came to: the map it cleared and what it paid. Only the passed half of the
/// result lives here; a failed run has its own screen with a different question to ask.
/// </summary>
public sealed class ClearedScreenView : MonoBehaviour
{
    [SerializeField] private GameFlowController flow;
    [SerializeField] private MapConfig mapConfig;
    [SerializeField] private Image mapImage;
    [SerializeField] private GameObject rewardRoot;
    [SerializeField] private TMP_Text rewardLabel;
    [SerializeField] private float rewardCountUpSeconds = 0.55f;

    OnEnable:  flow.RunFinished += Show;
    OnDisable: flow.RunFinished -= Show; StopCountUp();

    public void Show(LevelRunController.RunResult result)
    {
        if (!result.Passed) return;                       // the fail screen's business
        Sprite picture = mapConfig != null && mapConfig.TryGet(result.MapId, out MapInfo map) ? map.ClearedImage : null;
        mapImage.sprite = picture; mapImage.enabled = picture != null;
        ShowReward(result.GoldAwarded);                   // rewardRoot off when 0; count-up as RunResultView, unscaled time
    }
}
```

The count-up (`ShowReward`, `CountUp`, `StopCountUp`) is copied from `RunResultView` verbatim, formatting `+{gold}`; `RunResultView` itself is left untouched for the fail path.

## 5. Editor — `Editor/ClearedScreenBuilder.cs`

`[MenuItem("Tools/Smashdown/Build Cleared Screen")]`, idempotent:

1. Ensure `Prefabs/UI/Result/`; build `ClearedScreen.prefab` (§2, §2.1); `ClearedScreenView` wired: `mapConfig` = `LoadFirstAsset<MapConfig>()`, `mapImage`, `rewardRoot`, `rewardLabel`. `SaveAsPrefabAsset`, `UnloadPrefabContents`.
2. Scene: if the Canvas has no `ClearedScreen`, instantiate the prefab there (anchors `(0,0)–(1,1)`, inactive). `SetIfEmpty` on the flow: `clearedRoot`, `clearedReplayButton`, `clearedContinueButton`, `clearedCloseButton`; `SetIfEmpty` `flow` on the view (the flow is a scene object, so it is wired on the instance, not in the prefab). `failRoot` needs nothing: `FormerlySerializedAs` carries the old `resultRoot` value. Mark the scene dirty.
3. For every `MapInfo` in `MapConfig` with an empty `clearedImage`, assign `Textures/Maps/{id}.png` if that sprite exists (via `SerializedObject` on the config: `maps.Array.data[i].clearedImage`).

## 6. Acceptance criteria

1. **Build from clean**: the menu item produces the prefab, adds a `ClearedScreen` instance (no overrides) and wires the four flow fields plus the view's `flow`; the scene's old `ResultScreen` instance is still there and is what `failRoot` names. A second run changes nothing.
2. **Pass a map**: the cleared screen appears over the dimmed, still-standing structure: banner, `+<gold>` counting up beside the coin, the map picture (or nothing when unassigned), REPLAY, CONTINUE, X. The HUD is hidden.
3. **Fail a map**: the old `ResultScreen` appears exactly as before (`OUT OF MOVES`, RETRY, MAIN MENU).
4. **REPLAY** opens the same map's ammo pick. **CONTINUE** on map `1` opens map `2`'s ammo pick with map `2` selected; on the last map it returns to the main menu. **X** returns to the main menu. All three tear the finished run down (no debris left, structure cleared) — same as today's RETRY / MAIN MENU.
5. **Repeat pass** (already-passed map, nothing new earned): no coin row. **Perfect clear**: same screen; the amount is the run's `GoldAwarded` (pass + clear rewards when both are new).
6. **Taps do not reach the cannon** through the screen (the dim is a raycast target).
7. **Domain-reload-off**: entering play twice does not double-subscribe to `RunFinished`.

## 7. Out of scope

Confetti, a stars/percent readout, "next map locked" messaging (cannot happen under Brief 08's rule), the ammo chip, sound, the fail screen (Brief 10).
