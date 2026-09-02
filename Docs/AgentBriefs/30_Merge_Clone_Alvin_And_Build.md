# Task Brief 30 — Merge `Falcon/Clone_Alvin`, clear the repo litter, and cut a build

Three pieces of work, in order, ending in a shippable build. Branch **`Merge/CloneAlvin`** from `main` for §1–§2;
§3 is settings and a build, on `main`.

Repository: `/Volumes/Supercent/FierceTigerGameJam/FierceTigerGameJam` — paths relative to `Assets/GameJam/`
unless they start with `Assets/`.

## Decisions already made (do not re-open)

- **Where the two branches built the same thing, ours stays and theirs goes.** That is specifically the garage
  model preview: `main` has `ModelPreviewRig` + `ModelPreviewView` (Brief 25); the branch has
  `ShopModelShowcase`. Ours survives; theirs is dropped, not kept alongside.
- **Their multi-round firing survives the arc.** `armedBoostRounds` / `burstPerVehicleLevel` / spend-per-round
  are wanted, and must be reconciled with Brief 26's fixed-apex lob rather than either being reverted.
- **`_port_pack/` does not land**, and **`Map.zip` comes out** of the repo at the same time.

## Verified state (re-derive before trusting — both tips move)

Analysed at `main` = `42f1c629`, `origin/Falcon/Clone_Alvin` = `3987c189`, merge base `24abd632`.
Branch is +2 commits / 208 files / ~492k insertions, and **deletes no files** (the 60k "deletions" are
line-level edits inside modified files).

Test-merged: **5 real conflicts**, `Gameplay.unity` and `BulletShopView.cs` auto-merge.

| File | Both sides did | Resolution |
|---|---|---|
| `Scripts/UI/VehicleShopView.cs` | ours: `preview3D.Show(...)`. theirs: `ShopModelShowcase.Create/Show` | **ours**; drop their showcase call |
| `Scripts/Gameplay/Cannon/GridKnockdownCannonFireController.cs` | ours: fixed-apex lob per shot. theirs: multi-round burst + per-round spend | **both** — see §1.2, the only real design work here |
| `Config/Vehicles/cannon_a.asset`, `_b`, `_c` | ours: Brief 25 icons. theirs: their own edits | **ours** (the icons are wired to the artist's `ICN_*` sprites) |

Seven scripts on the branch do not exist on `main` and are **all genuinely new**: `ReloadBarController`,
`DragHintController`, `ShotBoostIntroController`, `LevelScenery`, `GarageSaveReset`, `MissionBoardScroller`,
`ShopModelShowcase`. Only the last is a duplicate; the other six come along with the merge.

**`MissionBoardScroller` needs a look**, not an assumption: `main` reworked the mission board's layout and
scrolling in Brief 25 (3-column grid, vertical overflow, centred). If their scroller drives the same list,
say so rather than merging two scrollers onto one board.

## 1. The merge

```
git checkout -b Merge/CloneAlvin main
git merge origin/Falcon/Clone_Alvin
```

1. **Resolve the three `cannon_*.asset` conflicts to ours.** Confirm afterwards that each level's `icon` still
   points at an `ICN_Tank_*` sprite and that `cannon_c` is Purple — the icon wiring is easy to lose in a
   three-way asset merge, and `cannon_c` previously held Layer Lab language-flag sprites.
2. **Reconcile the fire controller by hand.** Their burst loop decides *how many rounds* a tap fires and what
   each costs; our lob decides *the velocity of one shot*. They compose: keep their round/spend structure, and
   inside it solve `GetLobVelocity` **per round** from the current muzzle and aim point. Do not solve once and
   reuse the velocity for every round — the muzzle moves with the barrel between rounds. State plainly whether
   their burst was per-frame or spread over time, because that changes where the solve belongs.
3. **Drop `ShopModelShowcase.cs` and its `.meta`**, and remove every reference to it. Check nothing else on
   their branch calls it (a scene or prefab may) before deleting.
4. **Keep `_port_pack/` out.** Simplest honest route: let the merge bring it, then delete the folder in a
   commit of its own and add `_port_pack/` to `.gitignore` so it cannot drift back. Note in the run notes that
   the folder held maps, textures and reference copies of `MissionPanelView.cs` / `MissionEditorWindow.cs`, in
   case the teammate wants anything from it.
5. Compile, and verify the six kept scripts are wired to something — a script nobody references is a merge
   that dropped a scene reference.

## 2. Repo litter

Same branch, its own commit: delete `Map.zip` (463 KB at the repo root) and add it to `.gitignore` beside
`_port_pack/`. Note in the run notes that neither leaves git history — that needs a rewrite nobody has asked
for — so this stops the working tree carrying them, not the clone size.

## 3. The build

Settings that are wrong today and would ship as-is:

| Setting | Now | Wanted |
|---|---|---|
| `companyName` | `DefaultCompany` | the real one |
| `applicationIdentifier.Android` | `com.UnityTechnologies.com.unity.template.urpblank` | a real reverse-DNS id |
| `bundleVersion` | `0.1.0` | agree a jam version |
| Build scenes | `SampleScene` (disabled) + `Gameplay` (enabled) | drop `SampleScene` |

Then: switch to Android, build, and install on device. The build is the acceptance — but three things are
worth checking **before** spending a build on them, because each has been flagged and none has been seen
running:

- **The garage 3D preview's alpha-0 render texture.** URP has historically forced alpha to 1; if it does, the
  preview window shows a black square instead of the frame art. One-line fix, but only visible at runtime.
- **`Cannon_A_URP` has two unresolved material GUIDs** — level 1 of the *starting* vehicle. The preview draws
  the whole model, so it will likely render magenta. First thing anyone sees in the vehicle tab.
- **`EnterNextMap` walks the flat `MapConfig` index, not mission order**, so CONTINUE from mission 1's last map
  walks into locked mission 2's dev maps.

## 4. Acceptance

1. `main` carries the merge with our preview, their six new scripts, and a fire controller that both bursts and
   lobs; no `ShopModelShowcase`, no `_port_pack/`, no `Map.zip`.
2. Compile clean; the garage, the mission board and a full run all behave as they did before the merge.
3. Product identity and scene list corrected; an Android build installs and runs to the main menu.
4. A run is completable on device end to end: tutorial, mission 1 map 1, cleared screen, back to the board.

## Out of scope

Rewriting git history to shrink the repo, the balance retune (Brief 27's findings stand: the concrete gate does
not gate and the report's red rows are expected), porting anything else out of `_port_pack/`, and the three
pre-existing bugs above beyond noting whether they show up in the build.
