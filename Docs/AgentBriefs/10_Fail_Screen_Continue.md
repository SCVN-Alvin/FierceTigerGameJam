# Task Brief 10 — Fail screen: "Continue?", the +5 ammo banner, a priced continue, close to menu

## Goal

When a run **fails** (ammo gone, map not passed), show the **Fail screen** from the art instead of the old `ResultScreen`: the `UI_Continue_img` title, the fixed `UI_Banner_PlusAmmo` panel ("+5 — Add 5 ammo to continue!"), a green price button, and a close X. Paying the price adds ammo of the loaded bullet type and **resumes the same run in place** — the structure stays exactly as it was; running out again brings the screen back. The X returns to the main menu. The price and the ammo amount come from a new `LoseConfig`.

Reference: `Assets/GameJam/RefAI/Fail_ref.png`. **Depends on Brief 09** (`failRoot` / `clearedRoot` split, `RunFinished` for both kinds of result) — branch from `main` after it is merged.

Decisions already made (do not re-open):

- **Unlimited continues at a flat price.** `LoseConfig` holds one `continuePrice` (4,000) and one `continueAmmo` (5). The banner's "+5" is baked into the art, so `continueAmmo` is kept in step with it by hand (a tooltip says so). Escalation and per-run limits are not built.
- **The ammo goes to the loaded type**: `BulletLoadout.Selected` (which already falls back to the default bullet). The cannon's own `ResolveAmmunition` prefers the selected type when it has rounds, so the next shot fires it.
- **Continuing resumes, it does not restart**: no rebuild, no `BeginRun`, no pool warming; the run controller goes `Finished → Playing`, the fail screen goes away, the HUD comes back. The failed attempt that `Judge()` already recorded stays recorded (best percent only; a later pass re-registers on top).
- **Gold moves only through `EconomyService`** (`TryPayContinue`), and it is the last check and the first write: nothing is granted unless the charge went through, and nothing is charged unless everything else was already verified.
- **The old `ResultScreen`, `RunResultView` and the flow's `retryButton` / `resultContinueButton` go** — the cleared screen (Brief 09) and this one cover both outcomes.
- The green price button in the ref is a long blank button with a coin; no such sprite exists. Expected as `Textures/UI/LoseScreen/Btn_Price_Long.png` (same style as `Btn_Continue_Long`, no text); until it exists the builder uses `Btn_Buy` (which has its own coin baked in) scaled up, and the layout does not move when the real one arrives.

House rules (from Brief 06, unchanged): prefab authored by an idempotent editor menu item; anchor fractions `(xMin, yMin)–(xMax, yMax)` of the parent, y from the bottom; subscribers unsubscribe in `OnDisable`; decorative images `raycastTarget` off; `Try*` returns bool; XML doc comments say *why*; no LINQ in runtime paths.

## Git

Branch **`Feature/FailScreen`** from `main` **after Brief 09 is merged**. One-line commits, no body, no trailers; commit per step (config, inventory/run controller, economy, flow, screen, builder).

## Repository

`/Volumes/Supercent/FierceTigerGameJam/FierceTigerGameJam` — paths relative to `Assets/GameJam/`.

| Existing | Role | Touched |
|---|---|---|
| `Scripts/Gameplay/Combat/BulletInventory.cs` | per-type counts; `TryPick` respects `pickLimit`; `TrySpend` raises `Emptied` at zero; `Changed` | gains `Grant` |
| `Scripts/Gameplay/Flow/LevelRunController.cs` | `RunState { Idle, Picking, Playing, Settling, Finished }`, `HandleInventoryEmptied` (only while `Playing`) → `SettleThenJudge` → `Judge()` → `Finished` | gains `CanContinueRun`, `ContinueRun` |
| `Scripts/Economy/EconomyService.cs` | `Gold`, `TrySpendGold(int)`, `GoldChanged`, `Loadout` (the `BulletLoadout`), price configs as serialized fields | gains `loseConfig` + two methods |
| `Scripts/Gameplay/Flow/GameFlowController.cs` | after Brief 09: `failRoot`, `clearedRoot`, `HandleRunFinished`, `hudRoot`, `State`, `StateChanged`, `Wire`/`Unwire`, `ReturnToMainMenu` | gains `economy`, `ContinueRun`, two buttons; loses two |
| `Scripts/UI/RunResultView.cs`, `Prefabs/UI/ResultScreen/ResultScreen.prefab` | the old result (fail-only since Brief 09) | **deleted** |
| `Editor/UiBuilder.cs` → `BuildResult`, `WireFlow` | builds and wires the old result panel | `BuildResult` and the `resultRoot` / `retryButton` / `resultContinueButton` wiring removed |
| `Editor/GameConfigBuilder.cs` | `EnsureAsset<T>(path)`, `SetIfEmpty` wiring into `EconomyService` | gains `LoseConfig` |
| `Scripts/Config/PurchaseVehicleConfig.cs` | the shape of a small config asset | pattern for `LoseConfig` |
| `Scene/Gameplay.unity` | `ResultScreen` instance = `flow.failRoot` | instance replaced by `FailScreen` |

## 0. Art

| Sprite | px | Image type | Used for |
|---|---|---|---|
| `Textures/UI/LoseScreen/UI_Continue_img` | 793×264 (opaque x 25–766, y 53–196) | Simple, preserveAspect | "Continue?" title |
| `Textures/UI/LoseScreen/UI_Banner_PlusAmmo` | 815×695 | Simple, preserveAspect | the fixed "+5" panel, text baked in |
| `Textures/UI/LoseScreen/Btn_Price_Long` (**to be supplied**) | ~418×162 | Sliced or Simple | blank green button; fallback `Textures/UI/Garage/Btn_Buy` (145×51, coin baked at px 19–45) drawn Simple + preserveAspect |
| `Textures/UI/Common/UI_Coin` | 84×89 | Simple | coin on the button — only with the blank sprite |
| `Textures/UI/Garage/Btn_Esc` | 102×104 | Simple | close X |
| `Textures/UI/Tutorial/Filter` | 1216×1920 | Simple | dim over the scene |

The `20` ammo chip top-left in the ref is the in-run counter; the HUD is hidden on results, so it is not part of this screen.

## 1. `LoseConfig` — `Scripts/Config/LoseConfig.cs`, asset `Config/LoseConfig.asset`

```csharp
[CreateAssetMenu(menuName = "GameJam/Lose Config", fileName = "LoseConfig")]
public sealed class LoseConfig : ScriptableObject
{
    [Tooltip("Gold taken for one continue. Flat: the same every time in a run.")]
    [Min(0)] public int continuePrice = 4000;

    [Tooltip("Rounds added by one continue, of the loaded ammunition. The fail screen's banner art "
           + "says +5; change the art with this number.")]
    [Min(1)] public int continueAmmo = 5;
}
```

`GameConfigBuilder.CreateGameConfigs`: `EnsureAsset<LoseConfig>($"{ConfigFolder}/LoseConfig.asset")` and `SetIfEmpty(economy, "loseConfig", ...)`, next to the other price configs. Never overwrites values already set.

## 2. Inventory and run controller

`BulletInventory`:

```csharp
/// <summary>
/// Adds rounds outside the pick. The pick limit is the rule for what may be carried in, not
/// for what may be bought mid-run, so this ignores it; a continue is the only caller.
/// </summary>
public void Grant(string bulletId, int amount)
{
    if (string.IsNullOrEmpty(bulletId) || amount <= 0) return;
    counts[bulletId] = GetCount(bulletId) + amount;
    Changed?.Invoke();
}
```

`LevelRunController`:

```csharp
/// <summary>Only a judged run can be continued; anything else has nothing to come back from.</summary>
public bool CanContinueRun() => State == RunState.Finished && bulletInventory != null;

/// <summary>
/// Picks the run back up where it stopped. The structure is left as it is and the tracker keeps
/// counting, so a continue is worth exactly the rounds it adds. The attempt Judge recorded stays
/// recorded; when these rounds run out the run is judged again, on top of it.
/// </summary>
public bool ContinueRun(string bulletId, int amount)
{
    if (!CanContinueRun() || string.IsNullOrEmpty(bulletId) || amount <= 0) return false;
    bulletInventory.Grant(bulletId, amount);
    SetState(RunState.Playing);   // HandleInventoryEmptied and the full-clear path both require Playing
    return true;
}
```

`Emptied` fires again when the granted rounds hit zero (`TrySpend` checks `IsEmpty` on every spend), so the settle-then-judge path re-arms itself.

## 3. `EconomyService`

```csharp
[SerializeField] private LoseConfig loseConfig;

public int ContinuePrice => loseConfig != null ? loseConfig.continuePrice : 0;
public int ContinueAmmo  => loseConfig != null ? loseConfig.continueAmmo : 0;

/// <summary>Whether the player could pay for a continue this instant. False with no config: nothing is sold unpriced.</summary>
public bool CanContinueRun() => loseConfig != null && loseConfig.continueAmmo > 0 && Gold >= loseConfig.continuePrice;

/// <summary>Charges for a continue. Goes through TrySpendGold so the save and GoldChanged happen exactly as for any other spend.</summary>
public bool TryPayContinue() => CanContinueRun() && TrySpendGold(loseConfig.continuePrice);
```

## 4. Flow — `GameFlowController`

```csharp
[Tooltip("Charges the continue; also where the loaded ammunition is read from.")]
[SerializeField] private EconomyService economy;

[Header("Fail Screen Buttons")]
[SerializeField] private Button failContinueButton;   // → ContinueRun
[SerializeField] private Button failCloseButton;      // → ReturnToMainMenu
```

Remove `retryButton` and `resultContinueButton` and their `Wire`/`Unwire` lines (the buttons are gone with the old screen; `RetryMap` stays — Brief 09's REPLAY uses it). Then:

```csharp
/// <summary>
/// Pays for more rounds and picks the failed run back up. Checks come first, the charge last
/// and only once everything it pays for is certain to happen; nothing here rebuilds the map or
/// re-enters a state, because Enter tears a run down and this one is being kept.
/// </summary>
public void ContinueRun()
{
    if (State != GameState.Result || runController == null || !runController.CanContinueRun()) return;
    if (economy == null || !economy.CanContinueRun()) return;

    string bulletId = ResolveContinueBulletId();
    if (string.IsNullOrEmpty(bulletId)) return;

    if (!economy.TryPayContinue()) return;               // GoldChanged fires here; the fail screen re-reads its button

    runController.ContinueRun(bulletId, economy.ContinueAmmo);

    SetRootActive(failRoot, false);
    SetRootActive(hudRoot, true);
    State = GameState.Playing;
    StateChanged?.Invoke(State);
}

/// <summary>The loaded ammunition; Selected already falls back to the starter, so this is only null in an unwired scene.</summary>
private string ResolveContinueBulletId()
{
    BulletLoadout loadout = economy != null ? economy.Loadout : null;
    BulletDefinition bullet = loadout != null ? loadout.Selected : null;
    return bullet != null ? bullet.Id : null;
}
```

`IsInRun` is unchanged (`Playing` is a play state), `gameplayRoot` never went inactive on the result, and the aim controller was never reset, so the player is looking at exactly what they left.

## 5. Screen — `Prefabs/UI/Result/FailScreen.prefab`

```
FailScreen                    RectTransform (0,0)-(1,1)          FailScreenView
├─ Dim                        Image Filter, stretch, raycastTarget ON
├─ Title                      Image UI_Continue_img, preserveAspect, raycast off
├─ Banner                     Image UI_Banner_PlusAmmo, preserveAspect, raycast off
├─ ContinueButton             Image (Btn_Price_Long, Sliced | fallback Btn_Buy, Simple + preserveAspect) + Button (ColorTint)
│  ├─ Coin                    Image UI_Coin, preserveAspect, raycast off — active only with Btn_Price_Long
│  └─ Price                   TMP bold 40, white, centred, "4,000"
└─ CloseButton                Image Btn_Esc, preserveAspect + Button
```

### 5.1 Geometry (root 720×1280; mock 1216×1920 → ×0.592)

| Object | Anchors | From the mock |
|---|---|---|
| `Title` | `(0.12, 0.74)–(0.88, 0.88)` | text spans x 252–945, y 328–429; the sprite's margins fill the rest |
| `Banner` | `(0.171, 0.365)–(0.837, 0.72)` | panel x 208–1018, y 538–1220 (815×695 at 1:1) |
| `ContinueButton` | `(0.303, 0.219)–(0.693, 0.304)` | x 369–843, y 1337–1500 |
| `Coin` | `(0.08, 0.15)–(0.32, 0.85)` of the button | coin x 432–515 (84 px at 1:1) |
| `Price` | `(0.34, 0.05)–(0.96, 0.95)` of the button | digits right of the coin, ~60 px tall |
| `CloseButton` | `(0.789, 0.906)–(0.865, 0.953)` | x 959–1052, y 90–181 |

## 6. `FailScreenView` — `Scripts/UI/FailScreenView.cs`

```csharp
/// <summary>
/// The failed run's one question. It shows the price and whether the player can pay it; the
/// paying, and everything that follows, is the flow's, so this view never touches the run.
/// </summary>
public sealed class FailScreenView : MonoBehaviour
{
    [SerializeField] private EconomyService economy;
    [SerializeField] private Button continueButton;
    [SerializeField] private TMP_Text priceLabel;

    OnEnable:  economy.GoldChanged += Refresh; Refresh();
    OnDisable: economy.GoldChanged -= Refresh;

    private void Refresh()
    {
        // priceLabel.text = economy.ContinuePrice.ToString("N0", CultureInfo.InvariantCulture)
        // continueButton.interactable = economy.CanContinueRun()   (dimmed when short of gold)
    }
}
```

The screen is switched on by `HandleRunFinished` only for a failed result, so it does not need `RunFinished`; `OnEnable` is its "show".

## 7. Editor — `Editor/FailScreenBuilder.cs`

`[MenuItem("Tools/Smashdown/Build Fail Screen")]`, idempotent:

1. `LoseConfig` via `GameConfigBuilder` (§1) — call its `EnsureAsset` path or run `Create Game Configs` first; wire `loseConfig` into `EconomyService` with `SetIfEmpty`.
2. Build `FailScreen.prefab` (§5, §5.1); the button's sprite from `Btn_Price_Long` if it exists else the `Btn_Buy` fallback (and `Coin` inactive in that case); `FailScreenView` wired with `economy`, `continueButton`, `priceLabel`. `SaveAsPrefabAsset`, `UnloadPrefabContents`.
3. Scene: destroy the `ResultScreen` instance under the Canvas if present (whole instance); if `FailScreen` is absent, instantiate the prefab there (anchors `(0,0)–(1,1)`, inactive). On the flow: `SetIfEmpty` `failRoot` (null once the old instance is gone), `failContinueButton`, `failCloseButton`, `economy` (`LoadFirstAsset<EconomyService>`). Mark the scene dirty.
4. `AssetDatabase.DeleteAsset` `Prefabs/UI/ResultScreen/ResultScreen.prefab`; delete `Scripts/UI/RunResultView.cs`; remove `BuildResult` and the three result lines in `WireFlow` from `UiBuilder.cs` (and the `ResultName` constant).

## 8. Acceptance criteria

1. **Build from clean** (after Brief 09): `Config/LoseConfig.asset` exists with 4000 / 5 and is wired into `EconomyService`; `FailScreen.prefab` and its instance replace `ResultScreen`; `flow.failRoot`, `failContinueButton`, `failCloseButton`, `economy` are set; `RunResultView.cs` and the old prefab are gone; the project compiles; a second run changes nothing.
2. **Fail a run** with < 4,000 gold: dim over the standing structure, "Continue?", the +5 banner, the price button reading `4,000` **dimmed**, the X. Tapping the dimmed button does nothing.
3. **Fail a run** with ≥ 4,000 gold and tap the button: gold drops by 4,000 (the main-menu chip agrees afterwards), the fail screen closes, the HUD returns with the counter at `5`, the structure is untouched (no rebuild, debris where it was), the next shot fires the loaded bullet type, and emptying those 5 brings the fail screen back — as many times as the player can pay.
4. **Pass after a continue**: the cleared screen appears with the normal rewards; `UserData.Maps` shows the map passed.
5. **X** returns to the main menu and tears the run down; nothing is charged.
6. **Loaded type**: with two types picked and the cannon on the second, the 5 rounds land on the second (`BulletInventory.Counts`), not the first.
7. **Guards**: `ContinueRun` is a no-op outside `Result` / `Finished` (call it from the context menu on the main menu: nothing happens, no gold moves); `BulletInventory.Grant` ignores the pick limit; `EconomyService` is still the only class that writes `UserData.Inventory.gold`.
8. **Domain-reload-off**: entering play twice does not double-subscribe to `GoldChanged`.

## 9. Out of scope

Price escalation, a per-run cap, a retry option on the fail screen, ads-for-continue, the "20" ammo chip, sound, a shortcut to the IAP shop when short of gold.
