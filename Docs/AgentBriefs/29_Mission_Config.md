> **Completed and merged.** Originally numbered 25; renumbered when a later set of briefs
> reused that number. Kept for the record - the work described here is already on `main`.

# Task Brief 25 — Missions become data: a MissionConfig, a scrolling map row, and ids that mean something

Branch **`Feature/MissionConfig`** from `main`, one-line commits, no body, one commit per numbered section so the
rename does not drown the design change. House rules as always.

Repository: `/Volumes/Supercent/FierceTigerGameJam/FierceTigerGameJam` — paths relative to `Assets/GameJam/`.

## Why

Missions are currently a hand-authored array on `MissionPanelView`: `missions[]`, each entry a `locked` bool and
nine `slotMapIds` strings. Three things follow from that and all three are problems.

- **The lock is a static flag.** `IsMissionLocked` reads `missions[n].locked` and nothing else, so mission 2 stays
  locked whatever the player clears. There is no unlock path in the game at all.
- **Slots are loose strings**, resolved against `MapConfig` at draw time. A typo shows "NO MAP YET!" rather than
  failing anywhere a human would see it.
- **The ids they hold are inconsistent** (§3), so reading the array tells you nothing about what order things
  come in.

## Verified ground truth (read before trusting anything below)

| Thing | State |
|---|---|
| `MissionPanelView.missions[]` | authored on `Prefabs/UI/Mission/MissionScreen.prefab`: mission 1 = the nine campaign ids, mission 2 = `locked: 1` with three `Dev_` maps and six empty slots |
| `ResolveSlots` | returns the authored slot ids; with no authored missions it derives one page from `MapConfig` |
| List layout | `GridLayoutGroup`, `FixedColumnCount` 3, cell 143×129, spacing 17×33, inside a **vertical** `ScrollRect` (`horizontal = false`), `RectMask2D` viewport, `ContentSizeFitter` vertical-preferred |
| `MapConfig` | flat `MapInfo[]`; `MapInfo` = id, displayName, mapJson, mapPrefab, clearedImage |
| Save | `UserMapProgressData` keys per-map progress by the **id string**, with separate `passed` and `fullyCleared` flags |
| `MapProgressionConfig` | rows keyed by `mapId`: requiredClearPercent, passMapRewardId, clearMapRewardId, bulletPickLimit |
| Map 8 | **does not exist.** The campaign runs 001–007 then 009; slot 8 is `test_03.json`, whose JSON declares `"id": "2"` |

## Decisions already made (do not re-open)

- **MissionConfig groups; MapConfig keeps the maps.** Missions hold ordered references to `MapInfo` entries that
  stay in `MapConfig`, so `MapSelection`, `EnterNextMap`, the progress chip and the tutorial keep working off the
  flat registry they already use.
- **Unlock = every map in the previous mission passed.** `passed` is the existing per-map bar
  (`requiredClearPercent`, usually 0.8). Mission 1 is always unlocked.
- **The rename resets saves.** `UserMapProgressData` is keyed by id, so renaming orphans existing records and
  players start the campaign again. Accepted deliberately, the same call Brief 11 made when it retired the truck
  and the tank. No migration.

## 1. `MissionConfig`

New `Scripts/Config/MissionConfig.cs`, asset `Config/MissionConfig.asset`:

```csharp
[Serializable]
public sealed class Mission
{
    public string id;                 // mission_1, mission_2 - stable, never renamed once saved
    public string displayName;        // "MISSION 1"
    public MapInfo[] maps;            // ordered; the row is drawn in this order
}
```

plus `Missions`, `Count`, `TryGet(id)`, and `IsUnlocked(int index)` — index 0 always true, otherwise every map of
mission `index - 1` `passed` in `UserData`. Put the unlock rule **here**, not in the view: it is a fact about
progress, and the cleared screen will want to ask the same question when it decides what comes next.

## 2. The screen

- `MissionPanelView` loses `missions[]`, `slotMapIds`, `locked` and `IsMissionLocked`, and reads `MissionConfig`
  instead. Paging, the title, the chevrons and the locked dim all stay — only where the answer comes from
  changes. `ResolveSlots`'s `MapConfig` fallback goes with it.
- **The row scrolls horizontally.** `ScrollRect.horizontal = true`, `vertical = false`; the `GridLayoutGroup`
  becomes a `HorizontalLayoutGroup` (or a grid constrained to `FixedRowCount` 1 — say which you chose and why),
  and the `ContentSizeFitter` flips to horizontal-preferred. Cards keep their authored 143×129 size.
- A mission with more maps than fit simply scrolls; a mission with fewer draws fewer cards. **No empty slots** —
  the six blanks in mission 2 exist only because the array was fixed-width.
- `Editor/MissionScreenBuilder.cs` builds the above and ensures `Config/MissionConfig.asset`, seeded from the
  current authored arrays so nothing is lost.

## 3. The rename

Every campaign map becomes `mission{X}_map{Y}`, X and Y both 1-based, in mission order. That means the id, the
JSON's own `"id"` field, the JSON filename, the `MapConfig` entry, its `MapProgressionConfig` row and its baked
prefab name all move together. New menu item `Tools > Smashdown > Rename Maps To Mission Order`, idempotent,
logging every old → new pair.

Leave alone: `tutorial` (not a campaign map, and Brief 15's save flag keys off it) and the reward ids
(`pass_map_4` and friends live in `RewardConfig` and are a separate naming problem).

**Map 8 is the one to stop and think about.** There is no eighth map; the slot holds `test_03`. Renaming it
`mission1_map8` makes a placeholder look like a real level. Do not decide silently: rename it and flag it in the
run notes, or leave the slot out and flag that instead — but say which and why.

## 4. Unused parts

Report, do not delete unless it is unambiguous:

- `Maps/` holds **17 JSON files for 12 config entries**. `map_001.json`, `map_002_footprint_test.json` (its id is
  `models3d_rcreshryueasj1vmutscx5bmqgc3_...`), `map_003_wall_groups.json` and `map_006_level_03.json` are
  referenced by nothing.
- The three `Dev_` maps are campaign-prefixed but are not campaign maps, and two of them collide with real ids
  (`map_004_*`, `map_005_*` each appear twice with different content).
- Anything `MissionPanelView` stops using once the authored array is gone.

## 5. Acceptance

1. `MissionConfig.asset` exists with the campaign in mission 1 and the dev maps in mission 2; the board draws
   from it; a second builder run is a no-op.
2. The map row scrolls horizontally, cards keep their size, and a short mission draws no blank cards.
3. Mission 2 is locked on a fresh save and unlocks the moment every map in mission 1 is passed — verified by
   passing them, not by editing the asset.
4. Every campaign map is `mission{X}_map{Y}` and still loads, builds, pays its rewards and records progress; the
   tutorial is untouched.
5. A fresh save plays the campaign end to end. An old save loses its progress, as decided.
6. Compile clean with no reference to `slotMapIds`, `IsMissionLocked` or the removed fallback.

## Out of scope

Reward id naming, per-mission rules or budgets, mission art, an unlock animation or notification, deleting the
orphan JSONs (report them), Brief 23's wall removal, and any balance retune.
