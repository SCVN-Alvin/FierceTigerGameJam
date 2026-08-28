# Task Brief 15 — Tutorial: first launch drops into a one-block run with a spotlight and "Tap to shoot"

## Goal

On a fresh save, the game goes **straight from the loading screen into a tutorial run**: a dedicated one-block map is built, a dim with a **hole** spotlights the block, and the `UI_Tutorial` panel says **Tap to shoot**. The first shot dismisses the panel and the hole; destroying the block passes the run, **marks the tutorial completed**, and the normal cleared screen takes the player to the main menu. Players who have finished it never see it again; a player who quits or fails mid-tutorial gets it again next launch.

Reference: `Assets/GameJam/RefAI/Tutorial_ref.png`. **Depends on Brief 14** (`GameState.Loading`, `FinishLoading()` seam) — branch from `main` after it is merged.

Decisions already made (do not re-open):

- **A dedicated map**, not level 1: id `tutorial`, one brick block, JSON in §1 (canonical schema). It is **not** listed in `MapConfig` — the mission board and the `n/…` chip never see it. Its rules row (pick limit 3, pass at 80 %, **no reward ids**) goes in `MapProgressionConfig`, so passing pays nothing.
- **The tutorial run reuses the normal run path** — same build, judge, cleared/fail screens. Extra behaviour lives in one `TutorialController`; the flow gets a guard and the `FinishLoading` branch, nothing else.
- **Completion = the block destroyed** (the run judged `Passed`), saved as `UserData.Tutorial.completed` the moment the result lands. First shot only dismisses the overlay. Fail (all three shots missing — possible) shows the normal fail screen; not completing means the tutorial repeats next launch, which is the intended retry.
- **The hole is an artist sprite**: `Textures/UI/Tutorial/UI_Tutorial_Hole.png` (to be supplied) — a full-screen 1216×2160 dim with a soft transparent ellipse where the block stands, like the ref. Until the file exists the builder uses the plain `Filter.png` at reduced alpha (no hole) so the flow is testable; loading is by path, one-line fix when the art lands.
- **The overlay never blocks input**: every image in it has `raycastTarget` off — the player shoots *through* it. The ammo counter and the settings gear of the normal HUD stay visible (the run is a normal run).
- After the cleared screen, CONTINUE and X both lead to the main menu by the machinery that already exists: the tutorial map is not in `MapConfig`, so `EnterNextMap`'s `IndexOf` returns −1 and it falls back to `ReturnToMainMenu` — no special case needed.

House rules (unchanged): idempotent builder with `SetIfEmpty`; subscribers unsubscribe in `OnDisable`; `Try*` returns bool; XML doc comments say *why*; no LINQ in runtime paths; save-record classes follow `UserVehicleData`'s shape. Git: branch **`Feature/Tutorial`** from `main` after Brief 14, one-line commits, no body.

## Repository

`/Volumes/Supercent/FierceTigerGameJam/FierceTigerGameJam` — paths relative to `Assets/GameJam/`.

| Existing | Role | Touched |
|---|---|---|
| `Scripts/Data/UserData.cs` (+ siblings like `UserVehicleData.cs`) | keyed records, `Save/Reload/ResetAll/ResetOnEnterPlayMode`, `Changed` | gains `Tutorial` |
| `Scripts/Gameplay/Flow/GameFlowController.cs` | `FinishLoading()` (Brief 14), `HandleMapSelected` → `EnterAmmoPick`, `ConfirmAmmoPick()` (reset → warm → build → `BeginRun`), `RunFinished` | the branch + one guard |
| `Scripts/Gameplay/Wall/MapSelection.cs` | `Select(MapInfo)` — does **not** require membership of the config, which is what lets a standalone tutorial `MapInfo` ride the whole pipeline (builder JSON via `Selected.MapJson`, map id via `Selected.Id`) | untouched |
| `Scripts/Gameplay/Flow/LevelRunController.cs` | `BeginPick()` reads the selected map's rules; `Judge()` → `RegisterAttempt(mapId)` | untouched — the `tutorial` rules row feeds it |
| `Scripts/Gameplay/Combat/BulletInventory.cs` | `BeginPick(limit)`, `TryPick(id, amount)` | untouched |
| `Scripts/Gameplay/Cannon/GridKnockdownCannonFireController.cs` | `Fire(...)`, `OutOfAmmunition` event | gains a `Fired` event |
| `Maps/` (`map_001.json`, …) | map JSONs, canonical schema | `tutorial.json` added |
| `Config/MapProgressionConfig.asset` | per-map rules | `tutorial` row added by the builder |
| `Editor/GameConfigBuilder.cs`, `Editor/UiBuilder.cs` helpers | `EnsureAsset`, `SetIfEmpty`, `EnsureSpriteImage`, `LoadSprite` | reused |

## 1. The map — `Maps/tutorial.json`

Canonical schema, one brick block on the ground row (exact content):

```json
{
  "schemaVersion": 1,
  "id": "tutorial",
  "grid": { "width": 1, "height": 1, "cellSize": 0.25, "layerDepth": 0.25 },
  "layers": [
    {
      "level": 0,
      "blocks": [
        { "id": "f0_c0_r0", "type": "brick_1x1", "position": { "x": 0, "y": 0 }, "rotation": 0 }
      ]
    }
  ]
}
```

`MapProgressionConfig` row (added by `GameConfigBuilder` only if the id is absent): `mapId "tutorial"`, `requiredClearPercent 0.8`, empty `passMapRewardId` / `clearMapRewardId`, `bulletPickLimit 3`. One block means any hit that destroys it is 100 % ≥ 80 % → passed.

## 2. Save — `Scripts/Data/UserTutorialData.cs` + `UserData`

```csharp
[Serializable]
public sealed class UserTutorialData
{
    public int version = 1;

    [Tooltip-less; doc comment:]
    /// <summary>Set when the tutorial block is destroyed, never on the first shot: quitting
    /// mid-tutorial should repeat it, and firing once proves nothing was learned.</summary>
    public bool completed;
}
```

`UserData`: `TutorialKey = "user.tutorial"`, `public static UserTutorialData Tutorial`, included in `Save()`, `Reload()`, `ResetAll()`, `ResetOnEnterPlayMode()` exactly as the other records. Old saves without the key deserialise to `completed == false` — which is wrong for an existing player, accepted: they will clear one block once.

## 3. Fire event — `GridKnockdownCannonFireController`

```csharp
/// <summary>Raised once per shot actually fired (after it is paid for), for one-shot UI like
/// the tutorial's prompt. Refused shots raise OutOfAmmunition instead, never this.</summary>
public event System.Action Fired;
```

Raised at the end of `Fire(...)`. Cleared nowhere — it is a plain event on a scene component, and every subscriber unsubscribes in `OnDisable` as usual.

## 4. `TutorialController` — `Scripts/Gameplay/Flow/TutorialController.cs`

A scene component (the builder puts it on the flow controller's own GameObject):

```csharp
[SerializeField] private GameFlowController flow;
[SerializeField] private LevelRunController runController;
[SerializeField] private MapSelection mapSelection;
[SerializeField] private BulletInventory bulletInventory;
[SerializeField] private BulletLoadout bulletLoadout;      // for the starter bullet's id
[SerializeField] private GridKnockdownCannonFireController fireController;
[Tooltip("Standalone entry, not listed in MapConfig; filled by the builder (id, name, Maps/tutorial.json).")]
[SerializeField] private MapInfo tutorialMap;
[SerializeField, Min(1)] private int tutorialAmmo = 3;
[SerializeField] private GameObject overlayRoot;           // the whole overlay
[SerializeField] private GameObject panel;                 // UI_Tutorial
[SerializeField] private GameObject hole;                  // UI_Tutorial_Hole / Filter fallback

public bool ShouldRun => !UserData.Tutorial.completed;

/// <summary>True only while StartTutorial is steering the flow, so HandleMapSelected knows to
/// stand still instead of opening the ammo pick it would normally answer a selection with.</summary>
public bool IsStarting { get; private set; }

public void StartTutorial()
{
    IsStarting = true;
    mapSelection.Select(tutorialMap);          // the guard keeps the flow quiet
    runController.BeginPick();                 // reads the tutorial rules row: limit 3, pass 0.8
    bulletInventory.TryPick(StarterBulletId(), tutorialAmmo);   // the starter: DefaultBullet
    flow.ConfirmAmmoPick();                    // reset → warm → build the one block → BeginRun
    IsStarting = false;

    overlayRoot.SetActive(true);
    panel.SetActive(true);
    hole.SetActive(true);
    running = true;                            // private field: this run is the tutorial
}

OnEnable:  fireController.Fired += HandleFired; flow.RunFinished += HandleRunFinished; flow.StateChanged += HandleStateChanged;
OnDisable: the three unsubscribes.

HandleFired():                 // the first shot answers the prompt; later shots find it gone
    panel.SetActive(false); hole.SetActive(false);

HandleRunFinished(result):
    if (!running) return;
    if (result.Passed) { UserData.Tutorial.completed = true; UserData.Save(); }
    running = false; overlayRoot.SetActive(false);

HandleStateChanged(state):     // safety net: leaving the run any other way clears the overlay
    if (running && state != GameState.Playing && state != GameState.Result) { running = false; overlayRoot.SetActive(false); }
```

`StarterBulletId()` = `bulletLoadout.DefaultBullet` (null-guarded; with no loadout the pick simply fails and the run starts empty — the same misconfiguration behaviour `BeginRun` already handles by judging immediately).

## 5. Flow — `GameFlowController`

```csharp
[SerializeField] private TutorialController tutorial;

public void FinishLoading()
{
    if (tutorial != null && tutorial.ShouldRun) { tutorial.StartTutorial(); return; }
    ReturnToMainMenu();
}
```

And one guard at the top of `HandleMapSelected`: `if (tutorial != null && tutorial.IsStarting) return;` — commented with why (the tutorial selects its map as part of a scripted entry; answering it with the ammo pick would flash a screen the player is meant to skip).

## 6. Overlay — `Prefabs/UI/Tutorial/TutorialOverlay.prefab`

```
TutorialOverlay              RectTransform (0,0)-(1,1), inactive by default
├─ Hole                      Image UI_Tutorial_Hole (fallback: Filter at color (1,1,1,0.6)), stretch, raycast OFF
└─ Panel                     Image UI_Tutorial, Simple, preserveAspect, raycast OFF
```

Geometry (ref 1216×2160, drawn 1:1 → fractions of the screen): `Panel` anchors `(0.28, 0.759)–(0.73, 0.861)` — the ref's panel at x 340–890, y 300–520, above the spotlighted block. `Hole` is full-screen; the ellipse position is baked in the art (that is the deal with the artist sprite — if the block ever moves, the art moves).

Scene placement: under the Canvas **after `RunHud`** and before the result screens, so the counter and gear stay visible through it and the cleared screen covers it. Every image raycast-off; the overlay blocks nothing.

## 7. Builder — `Editor/TutorialBuilder.cs`

`[MenuItem("Tools/Smashdown/Build Tutorial")]`, idempotent:

1. Write `Maps/tutorial.json` (§1) if absent; add the `MapProgressionConfig` row if absent.
2. Build `TutorialOverlay.prefab` (§6; hole sprite by path with the Filter fallback and a log line saying which was used).
3. Scene: instantiate the overlay under the Canvas at the §6 sibling position if absent; `Ensure<TutorialController>` on the flow's GameObject; `SetIfEmpty` all its references (assets via `LoadFirstAsset`, scene objects found by type/name, `tutorialMap` fields via `SerializedObject` — `tutorialMap.id = "tutorial"`, `displayName = "Tutorial"`, `mapJson = Maps/tutorial.json`); `SetIfEmpty` `flow.tutorial`. Mark dirty.

## 8. Acceptance criteria

1. **Clean build**: menu item creates the JSON, the rules row, the overlay prefab + instance, the wired controller; second run no-op.
2. **Fresh save** (`UserData.ResetAll`): launch → splash (Brief 14) → the one-block run appears directly — no menu, no mission board, no ammo pick flash. The block, the dim/hole, `Tap to shoot`, the ammo counter reading 3, the gear — all as the ref.
3. **First tap** fires and the panel and hole disappear; the counter drops to 2; nothing else changes.
4. **Destroying the block** ends the run as a pass: the normal cleared screen shows (no reward row — the tutorial pays nothing); CONTINUE and X both land on the main menu; `UserData.Tutorial.completed` is true; the mission chip still reads `0/3` (the tutorial is not a campaign map).
5. **Relaunch** goes splash → main menu. `ResetAll` brings the tutorial back.
6. **Missing all three shots** shows the normal fail screen; going to the menu and relaunching repeats the tutorial (not completed).
7. **Quit mid-tutorial** (stop play mode after the first shot): next launch repeats it.
8. **The overlay blocks nothing**: aiming and firing work with it up; settings can be opened over it (the gear is the HUD's, unchanged).
9. **Existing player save** (maps passed, gold, vehicles): first launch after this update shows the tutorial once (accepted §Decisions), completing it changes none of their progress, gold or equipment.
10. **Domain-reload-off**: two play sessions — no double subscriptions, the overlay state resets.

## 9. Out of scope

More steps (aim hints, garage tour), localisation of the panel text (it is baked art), skipping the tutorial from settings, rewarding the tutorial, remembering partial progress inside it, and the hole tracking the block at runtime (the ellipse is baked into the artist's sprite).
