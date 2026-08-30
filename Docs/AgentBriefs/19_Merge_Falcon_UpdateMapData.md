# Task Brief 19 — Merge `Falcon/UpdateMapData` into `main`

Bring the teammate's map/mission branch into `main` and leave the game working on the other side. **Run this before Brief 17** (the font sweep rewrites every UI prefab, including the mission screen this branch reworks) and before Brief 18. Brief 16's work is already on `main`.

This brief was written against `origin/Falcon/UpdateMapData` at **`2d7fa643` ("Fix map")** — 4 commits (`e677b207`, `9a3c0971`, `364bbae1`, `2d7fa643`) branched from `52617da1` — and `main` at **`b4f78918`**. The sandbox that produced it could not reach the remote, so the very first step is a fresh fetch; if either side has moved, re-derive the conflict list (§2, first command) before trusting the resolutions below.

## 0. What the branch contains (so the merge is reviewed, not just resolved)

Nine campaign maps as JSON (`map_001_hollow_brick_hut` … `map_009_hollow_concrete_tower`) plus three `Dev_` test maps, wired into `MapConfig` / `MapProgressionConfig` / `RewardConfig`; a JSON→prefab bake pipeline (`MapInfo.mapPrefab`, `Editor/MapPrefabBaker.cs`, `KnockdownLayoutMapAuthoring.BuildFromPrefab`, large baked mesh assets); a wall-armor system (`wall.bare` opt-out in the JSON, `BreakableWall.IsArmored`, bare walls take `blockDamage`); the paged mission board (MISSION n title, `Btn_Mission_Prev/Next`, per-mission slots, "NO MAP YET!" notice, reworked `MissionScreen.prefab` and frame art); adaptive-canvas components (`AdaptiveCanvasScaler` — flips the scene's CanvasScaler to match-height — `CanvasColumnLimiter`, `CornerWidgetPinner`, plus editor setup tools); JSON spec/handbook docs under `Docs/`; the Unity Recorder package; and a `Map.zip` at the repo root.

## 1. Preconditions

- Working tree clean apart from the two ProjectSettings files that are always dirty (stash nothing that matters; commit or drop stray work first). Unity closed, or at least idle — the merge rewrites `Gameplay.unity` and prefabs under it.
- `git fetch origin` and confirm the branch tip; note it in the run notes.
- Configure Unity's Smart Merge so scene/prefab conflicts are mergeable (editor version is `6000.3.15f1`):

```
git config merge.unityyamlmerge.name "Unity SmartMerge"
git config merge.unityyamlmerge.driver '"/Applications/Unity/Hub/Editor/6000.3.15f1/Unity.app/Contents/Tools/UnityYAMLMerge" merge -p %O %B %A %A'
```

  (Find the exact Hub path with `ls /Applications/Unity/Hub/Editor` if the version folder differs.) Do **not** commit `.gitattributes` changes for this; the config is local. If the driver misbehaves on a file, fall back to resolving that file by hand per §2.

## 2. The merge

Work on an integration branch, the pattern the history already uses, and land on `main` only after §3 passes:

```
git checkout -b Merge/FalconMapData main
git merge origin/Falcon/UpdateMapData
```

Re-derive the conflict surface if either tip moved: `comm -12 <(git diff --name-only $(git merge-base main origin/Falcon/UpdateMapData) main | sort) <(git diff --name-only $(git merge-base main origin/Falcon/UpdateMapData) origin/Falcon/UpdateMapData | sort)`.

Expected conflicts at the analysed tips, and the intended resolution for each:

| File | Both sides did | Resolution |
|---|---|---|
| `Assets/GameJam/Scene/Gameplay.unity` | ours: rebuilt cannon pivot/frames, loading screen (last Canvas child), tutorial overlay. theirs: `AdaptiveCanvasScaler` + friends on the Canvas, CanvasScaler match 0→1, pinned-widget offsets | UnityYAMLMerge, then open the scene and verify **both** sets of objects exist (§3.1). This is the file to spend the time on |
| `Assets/GameJam/Config/MapProgressionConfig.asset` | ours: added the `tutorial` row (pick limit 3, no rewards). theirs: rows for the 9+3 new maps | **Union.** Every row from both sides; the `tutorial` row must survive — losing it breaks the first-launch flow (`BeginPick` would read a stale limit) |
| `Assets/GameJam/Scripts/Gameplay/Cannon/GridKnockdownCannonProjectile.cs` | ours: per-type prefab + level-look work. theirs: one line — `ResolveDamage(materialId, wall != null && wall.IsArmored, direct, falloff)` | Keep our file, apply their one-line change (their `BreakableWall.IsArmored` arrives in the same merge, so it compiles) |
| `Assets/GameJam/Editor/GarageScreenBuilder.cs` | ours: select/equipped button, CanvasGroup removal, frame seeding. theirs: `FrameOffset (0,-56)` → `(0,-96)` | Keep ours, take their `FrameOffset` value |
| `Assets/GameJam/Prefabs/Blocks/Brick/brick_wall_Panel.prefab` | art tweaks on both sides | Take **theirs**; §3.4 eyeballs bricks in play |

`Packages/manifest.json` / `packages-lock.json` are additions on their side (`com.unity.recorder`) and should merge clean; if not, keep both sides' packages.

Commit the merge with git's default one-line merge message. Any file the driver auto-merged still counts as unreviewed — skim `git diff main...HEAD` on the five files above before moving on.

## 3. Post-merge verification (in the editor, before touching `main`)

1. **Scene integrity** — open `Gameplay.unity`: the Canvas carries the adaptive-canvas components *and* still has `LoadingScreen` as its **last** child, the `TutorialOverlay` between `RunHud` and the result screens, and the rebuilt cannon under `World/Slingshot/Cannon` (mount, barrel follow, `MuzzlePoint`) intact. Enter play: splash → menu, no console errors.
2. **Tutorial** — reset the save (`UserData.ResetAll` via its usual entry point): the one-block tutorial still builds from JSON (it has no baked prefab, so this also proves the JSON path survived), completes, and never returns.
3. **Mission board** — both mission pages render, chevrons page, locked page dims, cards launch their maps, the unlock chain still runs off passes. Flag to the teammate, do not fix silently: the `MapConfig` entry `id: 2 / Lv8_Test03` looks like a leftover pointing at the old test map JSON.
4. **Maps and damage** — play one baked-prefab map (`map_001_hollow_brick_hut`) and one JSON-built map end to end: blocks break, walls behave (an armored wall chips by `wallDamage`; a `bare` wall breaks by `blockDamage`), our per-type projectiles with LV meshes still fly, clear/fail screens appear, rewards pay.
5. **Layout at other aspects** — their scaler flips the canvas to match-height: check garage, mission board, cleared/fail, bottom bar and chips at 720×1280 plus one taller (9:19.5) and one shorter (3:2) resolution. Anything clipped gets a per-screen anchor fix, not a revert of their scaler.
6. **Housekeeping to raise, not decide** — `Map.zip` at the repo root probably should not live in git; the Unity Recorder package arrived (dev tool, fine but worth a mention). Put both in the run notes for the team.

Then: `git checkout main && git merge Merge/FalconMapData` (fast-forward), push, delete the integration branch. If §3 fails in a way that needs their input, stop at the integration branch and report — do not land a broken `main`.

## Out of scope

Fixing the teammate's map content or ids beyond flagging, re-baking prefabs, tuning the adaptive scaler, deleting `Map.zip` unilaterally, and everything Briefs 17/18 cover (they run after this).
