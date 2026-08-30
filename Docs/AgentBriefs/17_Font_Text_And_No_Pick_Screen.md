# Task Brief 17 — One game font everywhere, a text-layout pass, and the pick screen retired

Branch **`Fix/FontAndFlow`** from `main`, one-line commits, no body. **Coordinate with `Falcon/UpdateMapData`: merge that branch into `main` first** — the font sweep rewrites every UI prefab (including `MissionScreen.prefab`, which that branch reworks heavily), and running the sweep before the merge manufactures conflicts on every one of them. House rules as always: idempotent editor tooling with `SetIfEmpty`, XML doc comments say *why*, no LINQ in runtime paths.

Repository: `/Volumes/Supercent/FierceTigerGameJam/FierceTigerGameJam` — paths relative to `Assets/GameJam/` unless they start with `Assets/`.

## A. Font — every text becomes `LilitaOne-Regular Outline 64 Bitmap`

The asset exists, exactly: `Assets/Layer Lab/GUI Pro-CasualGame/ResourcesData/Fonts/LilitaOne-Regular Outline 64 Bitmap.asset` (a TMP **bitmap** font from GUI Pro-CasualGame, white glyphs with the dark outline baked into the 64 px atlas).

New menu item **`Tools > Smashdown > Apply Game Font`** (`Editor/GameFontApplier.cs`), idempotent:

1. Load the font asset (path constant at the top; log-and-abort if missing).
2. For every prefab under `Prefabs/` (`AssetDatabase.FindAssets("t:Prefab", …)`): `LoadPrefabContents`, set `font` on every `TMP_Text` in children (inactive included) whose font differs, and set `fontSharedMaterial` to the font's own `material`; save only when something changed (count and log per prefab).
3. Same sweep over the open scene's canvases (some labels are scene-side, e.g. builder-made ones), skipping anything that is part of a prefab instance whose prefab was already converted.
4. Set the project default so future labels are born right: `Assets/TextMesh Pro/Resources/TMP Settings.asset` → `m_defaultFontAsset` = this font (via `SerializedObject`).
5. Builders that create labels (`UiBuilder.EnsureLabel` and any sibling that news up a `TextMeshProUGUI`) assign this font explicitly — one shared helper `GameFonts.Default` (editor-only class) so the path lives in one place.

Bitmap-font realities to respect, not fight: the outline is baked, so **drop `FontStyles.Bold`** wherever the sweep finds it (bold on an outlined bitmap doubles the strokes — the sweep clears the bold flag and logs where); vertex `color` tints outline and all, so strongly coloured labels (the brown mission titles, the gold reward number) will look different — leave the colours in place for the art pass to judge, but list every non-white text in the sweep log so nothing changes silently.

## B. Text layout — anchored and visible, checked rather than hoped

Lilita is wider than LiberationSans, so some labels that fitted will clip. Two tools, one pass:

- **`Tools > Smashdown > Audit Text Layout`** (same file): for the open scene plus every UI prefab, `ForceMeshUpdate()` each `TMP_Text` and log any that is truncated (`textInfo.characterCount < text length` with overflow, or `isTextTruncated`), any with a degenerate rect (width or height < 2), and any whose rect extends outside its root canvas. Output: one line each — path, text, what is wrong. The audit only reports; fixing is by hand.
- Fix pass, in this order of preference: widen/re-anchor the label's rect (the numbers in Briefs 06–15 say where each label lives); then `enableAutoSizing` with a sensible min (never below 16) where the string length varies (prices, names); `Ellipsis` overflow stays the last resort on the garage row header. Known tight spots to check first: garage row header (`NAME · LEVEL n`), Buy captions (`4,000` / `MAX` / `N/A`), mission card titles, the gold labels in the three chips, cleared-screen reward, fail-screen price, bottom-bar slot labels, `0/3` mission chip.

Acceptance for A+B: the audit reports zero findings on the main scene and every UI prefab; every screen (menu, mission board, garage both tabs, cleared, fail, loading, tutorial overlay, HUD) is screenshotted in the run notes with the new font and no clipped, overlapping or off-screen text; re-running both tools is a no-op.

## C. Retire the ammunition pick screen

PLAY now goes mission board → **straight into the run**. The pick is automatic: the bullet type equipped in the Garage (`economy.Loadout.Selected`, which already falls back to the starter), filled to the map's **full budget** — `bulletPickLimit` from `MapProgressionConfig`, the number `LevelRunController.BeginPick()` already reads into `BulletPickLimit`.

Ground truth: flow `HandleMapSelected` → `EnterAmmoPick()` (line ~376) → `Enter(GameState.AmmoPick)`; `ConfirmAmmoPick()` does reset → warm → build → `BeginRun`; the tutorial's `TutorialController.StartTutorial` already runs its own scripted pick (`BeginPick` + `TryPick(starter, 3)` + `ConfirmAmmoPick`) behind the `IsStarting` guard.

- `GameFlowController`:

```csharp
/// <summary>
/// Straight from choosing a map to shooting it. The pick screen is gone: the garage is where
/// ammunition is chosen now, so the run takes the equipped type and fills the map's whole
/// budget with it. BeginPick still runs first - it is what reads the map's rules.
/// </summary>
private void StartSelectedMap()
{
    if (runController != null) runController.BeginPick();
    AutoPickAmmunition();
    ConfirmAmmoPick();   // keeps its name; it is the build-and-begin step and the tutorial calls it too
}

private void AutoPickAmmunition()
{
    BulletLoadout loadout = economy != null ? economy.Loadout : null;
    BulletDefinition bullet = loadout != null ? loadout.Selected : null;
    if (bullet == null || bulletInventory == null) return;   // BeginRun already judges an empty run
    bulletInventory.TryPick(bullet.Id, runController != null ? runController.BulletPickLimit : 0);
}
```

  (`bulletInventory` is a new serialized field on the flow if it does not already have one — wire with `SetIfEmpty` from the existing asset.)
- `HandleMapSelected` calls `StartSelectedMap()` (tutorial guard stays); `RetryMap()` calls it too (it currently re-enters the pick).
- **`GameState.AmmoPick` stays in the enum, never entered** — a comment says it is kept so serialized `GameState` values (`BottomBarView` slots, anything else) keep their meaning. Everything else goes: `ammoPickRoot`, `startRunButton`, `EnterAmmoPick`, the `GoBack` AmmoPick case, the `backButton` field plus its Wire/Unwire and `Enter`'s back-button line (the pick screen was its last remaining home — the mission board uses the X and Home), and `Enter`'s `ammoPickRoot` / AmmoPick lines.
- Delete `Scripts/UI/AmmoPickView.cs`, `Prefabs/UI/AmmoPickScreen/AmmoPickScreen.prefab`, the scene's `AmmoPickScreen` instance and Back button object, and `UiBuilder.BuildAmmoPick` with its wiring lines (`ammoPickRoot`, `startRunButton`). `BulletInventory`'s pick API stays — the tutorial and `AutoPickAmmunition` are its callers now.
- Untouched on purpose: `RunState.Picking` (still the state between `BeginPick` and `BeginRun`, however brief), the fail-screen continue, and the tutorial (3 rounds, not the budget — its scripted pick already runs before `ConfirmAmmoPick`).

Acceptance: PLAY → level card → the structure builds and the HUD shows the full budget (e.g. 10) of the equipped type, no pick screen ever appears; switching the equipped ammo in the garage changes what the next run fires; RETRY/REPLAY relaunch with the same auto-pick; the tutorial still starts with exactly 3; a fresh clone compiles with no reference to `AmmoPickView`; domain-reload-off double-run clean.

## Out of scope

Per-type mixed loadouts (one type fills the budget by design now), a font pass over 3D/world text (there is none), recolouring texts the bitmap outline makes look different (logged, art's call), localisation.
