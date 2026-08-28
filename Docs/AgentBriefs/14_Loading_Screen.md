# Task Brief 14 — Loading screen: splash art, a fake-progress bar, then the menu

## Goal

Every launch opens on the **Loading screen**: the `SplashScreen` art full-screen, `Loading...` above a bar that fills over a configured time, then the game continues to the main menu. There is nothing real to load yet, so the time is fake and lives in a config: **`LoadingConfig.fakeLoadingSeconds` (default 2.5)**. No tap-to-skip. Brief 15 reroutes "then the main menu" through the tutorial check; this brief lands the screen and the state.

Reference: `Assets/GameJam/RefAI/Splash Screen_ref.png` (1216×2160 — the art is drawn 1:1 in it).

Decisions already made (do not re-open):

- Shows on **every** launch, fills over `fakeLoadingSeconds` of **unscaled** time, no skip, no minimum beyond the configured time.
- **`GameState.Loading` is appended at the end of the enum**, never inserted — `BottomBarView.Slot.state` serializes the enum by value, and inserting would silently re-map every slot.
- The flow's `Start()` enters Loading instead of the menu; when the bar completes the view calls one flow method, `FinishLoading()`, which for now is `ReturnToMainMenu()` — the single seam Brief 15 hooks.
- The screen is the **last child of the Canvas** so nothing draws over it; while `State == Loading` every other root, the bottom bar and the back button are off (the normal `Enter` bookkeeping — Loading is not a menu state).

House rules (unchanged): idempotent builder with `SetIfEmpty`; prefab-authored layout, anchors `(xMin, yMin)–(xMax, yMax)` of the parent with y from the bottom; subscribers/coroutines cleaned up in `OnDisable`; XML doc comments say *why*. Git: branch **`Feature/LoadingScreen`** from `main`, one-line commits, no body.

## Repository

`/Volumes/Supercent/FierceTigerGameJam/FierceTigerGameJam` — paths relative to `Assets/GameJam/`.

| Existing | Role | Touched |
|---|---|---|
| `Scripts/Gameplay/Flow/GameFlowController.cs` | `GameState`, `Start()` → `ReturnToMainMenu()`, `Enter(state)` switches roots, `IsMenuState` | Loading state, `loadingRoot`, `EnterLoading`, `FinishLoading` |
| `Scripts/UI/BottomBarView.cs` | slots keyed by `GameState` | untouched — the append rule protects it |
| `Scripts/Config/LoseConfig.cs` | the shape of a tiny config | pattern for `LoadingConfig` |
| `Editor/GameConfigBuilder.cs` | `EnsureAsset<T>` | ensures `Config/LoadingConfig.asset` |
| `Editor/UiBuilder.cs` helpers | `EnsureRect`, `EnsureSpriteImage`, `SetIfEmpty`, `LoadSprite`, `LoadFirstAsset` | reused |
| `Scene/Gameplay.unity` | Canvas 720×1280 match width | `LoadingScreen` instance added last |

## 0. Art — `Textures/UI/Loading/` (all imported as Sprite)

| Sprite | px | Image type | Used for |
|---|---|---|---|
| `SplashScreen` | 1216×2160 | Simple | full-screen background (logo, truck — all baked) |
| `img_loading` | 218×54 | Simple, preserveAspect | the `Loading...` word art |
| `UI_LoadingBar_Base` | 598×98 | Simple, preserveAspect | the blue tube |
| `UI_LoadingBar_Fill` | 560×64, border 50/0/50/0 | **Filled**, Horizontal, Origin Left | the yellow fill |
| `GameIcon` | 957×718 | — | **not used on this screen** (it is the app-icon source); leave it |

## 1. `LoadingConfig` — `Scripts/Config/LoadingConfig.cs`, asset `Config/LoadingConfig.asset`

```csharp
[CreateAssetMenu(menuName = "GameJam/Loading Config", fileName = "LoadingConfig")]
public sealed class LoadingConfig : ScriptableObject
{
    [Tooltip("How long the fake load takes. There is nothing real behind the bar yet; when there "
           + "is, this becomes the minimum time the splash is shown.")]
    [Min(0f)] public float fakeLoadingSeconds = 2.5f;
}
```

`GameConfigBuilder.CreateGameConfigs`: `EnsureAsset<LoadingConfig>($"{ConfigFolder}/LoadingConfig.asset")`.

## 2. Flow — `GameFlowController`

- `GameState.Loading` appended **last** with a comment saying why it must stay last.
- `[SerializeField] private GameObject loadingRoot;`
- `Enter`: `SetRootActive(loadingRoot, state == GameState.Loading);` alongside the others (`IsMenuState` unchanged — Loading is not one, so the bar hides itself).
- `Start()` → `EnterLoading();`

```csharp
public void EnterLoading() => Enter(GameState.Loading);

/// <summary>
/// Where the game goes once the splash is done. One seam on purpose: the tutorial check
/// (Brief 15) replaces this body rather than teaching the loading view about tutorials.
/// </summary>
public void FinishLoading() => ReturnToMainMenu();
```

## 3. Screen — `Prefabs/UI/Loading/LoadingScreen.prefab`

```
LoadingScreen                RectTransform (0,0)-(1,1)          LoadingScreenView
├─ Background                Image SplashScreen, stretch (0,0)-(1,1) + AspectRatioFitter (EnvelopeParent, 0.5630) — crops, never stretches, on odd aspects
├─ LoadingLabel              Image img_loading, preserveAspect, raycast off
└─ Bar
   ├─ Base                   Image UI_LoadingBar_Base, preserveAspect, raycast off
   └─ Fill                   Image UI_LoadingBar_Fill, Filled/Horizontal/Left, raycast off
```

Geometry (the ref is 1:1 with the screen, so fractions are read straight off it):

| Object | Anchors |
|---|---|
| `LoadingLabel` | `(0.40, 0.171)–(0.60, 0.201)` |
| `Bar` | `(0.247, 0.116)–(0.757, 0.162)` — 367×59 units (598×98 at ×0.592) |
| `Base` | stretch `(0,0)–(1,1)` of `Bar` |
| `Fill` | stretch with offsets `min (19, 17)`, `max (−19, −17)` — the fill sits inside the tube (598−560=38, 98−64=34 px in art) |

`Background` keeps `raycastTarget` **on**: the splash should swallow every tap while it is up.

## 4. `LoadingScreenView` — `Scripts/UI/LoadingScreenView.cs`

```csharp
/// <summary>
/// Fills the bar over the configured fake time and then hands the flow on. The time is unscaled:
/// nothing that pauses the game should be able to freeze the splash.
/// </summary>
public sealed class LoadingScreenView : MonoBehaviour
{
    [SerializeField] private GameFlowController flow;
    [SerializeField] private LoadingConfig config;
    [SerializeField] private Image fill;

    OnEnable:  fill.fillAmount = 0; start the coroutine.
    OnDisable: stop the coroutine.

    coroutine: float seconds = config != null ? config.fakeLoadingSeconds : 0f;
               t += Time.unscaledDeltaTime; fill.fillAmount = seconds > 0 ? Mathf.Clamp01(t / seconds) : 1f;
               when done → fill.fillAmount = 1; flow.FinishLoading();
               flow == null → log a warning once and leave the bar full (a test scene shows the splash and stops).
}
```

Re-entering Loading later (nothing does today) simply replays the bar — `OnEnable` owns the reset.

## 5. Builder — `Editor/LoadingScreenBuilder.cs`

`[MenuItem("Tools/Smashdown/Build Loading Screen")]`: build the prefab (§3) with `config` (`LoadFirstAsset<LoadingConfig>` — run `Create Game Configs` first or ensure it here) and `fill` wired; in the scene, instantiate under the Canvas **as the last sibling** if absent, `SetIfEmpty` the view's `flow` and the flow's `loadingRoot`, mark dirty. Idempotent throughout.

## 6. Acceptance criteria

1. Clean build: config asset (2.5), prefab, instance last under the Canvas, `flow.loadingRoot` and the view wired; second run no-op.
2. Press Play: the splash is the first and only thing visible — no menu, no bottom bar, no HUD behind it; the bar fills left-to-right over ~2.5 s of unscaled time and the main menu appears the moment it completes.
3. `fakeLoadingSeconds = 0` → the menu appears on the first frame after the splash (no divide-by-zero, no stuck bar).
4. Taps during the splash do nothing (background swallows them), and there is no way to skip.
5. The bottom bar's slots still map to the right states (the enum append rule held — check `BottomBarView` still raises HOME on the menu).
6. Domain-reload-off: entering play twice replays the splash cleanly, one coroutine at a time.

## 7. Out of scope

Real async loading behind the bar, a version label, fade-in/out, tap-to-skip, the tutorial branch (Brief 15).
