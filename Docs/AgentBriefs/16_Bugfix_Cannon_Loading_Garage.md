# Task Brief 16 — Bug-fix round: cannon model fit, barrel aim, idle/shot animation; loading logo; garage row cleanup

Three unrelated fixes, one branch: **`Fix/CannonModelPolish`** from `main`, one-line commits, no body, one commit per section at least. House rules as always: idempotent editor tooling with `SetIfEmpty`, subscribers unsubscribe in `OnDisable`, XML doc comments say *why*, no LINQ in runtime paths, no per-frame allocation.

Repository: `/Volumes/Supercent/FierceTigerGameJam/FierceTigerGameJam` — paths relative to `Assets/GameJam/` unless they start with `Assets/`.

Ground truth, verified against the current code (read these first):

- `VehicleMount` sits on the `Cannon` object inside `Imported/LunaSmashdown/Prefabs/SmashFest/Slingshot.prefab`, `mountPoint` = that same `Cannon` transform, `modelLocalScale (1,1,1)`, `fallbackModel` = `CannonTank_Default_Red`, exposes `CurrentAnimator`, strips colliders on spawn.
- **The aim never rotates `Cannon`**: `CannonAimController.aimPivot` is the **`CannonA`** transform (the old, inactive barrel model, child of `Cannon`, parent of `MuzzlePoint`). So the mounted pack model stands still while the invisible `CannonA` — and with it the muzzle and the fire direction — aims. That is bug 2: the vehicle's barrel must visually follow what `CannonA` does. (An inactive object's transform still receives and holds rotations, so reading it works.)
- The pack models are FBX prefab-variants: root → `Cannone_Pase` (the base; the pack's own typo), wheels, an armature whose bone chain is **`Cannon` → `Cannon.A` → `Cannon.B` → `Cannon.C`**, and a SkinnedMeshRenderer. Their Animator runs the pack's `Models/Cannon.controller`, whose **only state is `Armature|Shoting` and whose clip has `loopTime: 1`** — the cannon plays its firing animation on loop forever. That is bug 3.
- `CannonShotPresenter.PlayShot()` currently `Play()`s the state `Armature|Shoting` on `mount.CurrentAnimator` when a model is mounted, else the legacy `Cannon_Shot` on `_cannonAnimator`; smoke always.
- Garage rows (`Prefabs/UI/Garage/BulletTypeViewItem.prefab`, `VehicleTypeViewItem.prefab`): root carries a `CanvasGroup` + the item component (no Button on the root); children `Frame`, `Icon`, `Header(Locked,Label)`, `Levels`, `Buy`, `Select` (Button), `Equipped` (chip, off). `ShopItemView` still drives `group.alpha` from `equippedAlpha`/`otherAlpha` and its `ResolveMissingReferences` still falls back to `selectButton = GetComponent<Button>()` on the root.
- `Prefabs/UI/Loading/LoadingScreen.prefab` children: `Background` (SplashScreen art — the version **without** the logo), `LoadingLabel`, `Bar/Base+Fill`. No logo anywhere. `GameIcon.png` (957×718, imported as Sprite) is the "Smash City" logo that belongs at the top, per `RefAI/Splash Screen_ref.png`.

---

## A. Cannon model representation

### A1. Size — fit the pack models to `CannonA`

The mounted models are drawn at prefab scale 1 and are far bigger than the game's cannon. The fix is measured, not eyeballed, and baked into the config:

- `VehicleDefinition.Level` gains:

```csharp
[Tooltip("Uniform scale applied to modelPrefab when mounted. 0 = not fitted yet, treated as 1. "
       + "Written by Tools > Smashdown > Fit Vehicle Models; hand-tune after fitting if a model "
       + "still reads wrong.")]
[Min(0f)] public float modelScale;
```

  and `public float ResolveModelScale(int level)` → the clamped level's `modelScale`, or `1f` when it is 0 (no fallback walk — the tool fills every level that has a model).
- `VehicleMount.Refresh()` sets `current.transform.localScale = modelLocalScale * vehicle.ResolveModelScale(level);` (`modelLocalScale` stays the mount-wide hand tweak, default 1).
- New menu item **`Tools > Smashdown > Fit Vehicle Models`** (in `Editor/VehicleDefinitionBuilder.cs`):
  1. Measure the reference: instantiate `Slingshot.prefab` into a temporary scene (or `LoadPrefabContents`), activate `CannonA`, take the union of its `Renderer.bounds`, record `bounds.size.y` as the target height, discard the instance.
  2. For every `VehicleDefinition` level with a `modelPrefab`: instantiate the prefab the same way at scale 1, union its renderer bounds, compute `scale = targetHeight / bounds.size.y`, clamp to `[0.05, 2]`, write it into `modelScale` (overwrite — this tool's whole job is writing it; log old → new per model, plus the raw sizes so a suspicious ratio is visible).
  3. Save assets. The tool is safe to re-run any time art changes.
- The three pack families are close in size, so the nine numbers will be close; if the fitted model still looks off in play, the knobs are `modelScale` per level and the mount's `modelLocalScale` — say so in the run notes rather than inventing an offset field.

### A2. Rotation — the barrel follows `CannonA`

Only the barrel of the vehicle should aim; the base and wheels stay planted — exactly what the old art does (`CannonA` is the barrel, its parent `Cannon` never rotates). `VehicleMount` gains the follow:

```csharp
[Tooltip("The transform the aim actually rotates - CannonA, the old barrel. The mounted "
       + "model's barrel bone mirrors its rotation, so the vehicle aims without the aim "
       + "controller learning about vehicles.")]
[SerializeField] private Transform barrelReference;

[Tooltip("Name of the barrel node inside the pack models: the root bone of the armature "
       + "chain Cannon > Cannon.A/B/C. The base and wheels are siblings and stay still.")]
[SerializeField] private string barrelNodeName = "Cannon";
```

- On spawn, find the node by exact name under the spawned model (breadth-first walk, no LINQ; also grab the fallback: none found → warn once, model simply does not aim visually). Cache: `barrelNode`, `barrelRestLocalRotation`, and capture `referenceRestLocalRotation = barrelReference.localRotation` **once in `Awake`** (scene load, before any input; the flow's `ResetAim` restores the same pose between runs — note the assumption in a comment).
- `LateUpdate()` (after the Animator, so the follow wins on this one node while the shot clip animates the child bones `Cannon.A/B/C`):

```csharp
if (barrelNode != null && barrelReference != null)
{
    // The aim delta in the parent's space, replayed onto the barrel's own rest pose. Both
    // parents share orientation (the model mounts at identity under Cannon, CannonA is a child
    // of Cannon), so the delta transfers without any axis juggling.
    barrelNode.localRotation =
        barrelRestLocalRotation * (Quaternion.Inverse(referenceRestLocalRotation) * barrelReference.localRotation);
}
```

- Wiring: the `Wire Vehicle Mount` step in the definitions builder `SetIfEmpty`s `barrelReference` to the `CannonA` transform inside the Slingshot prefab.
- Nothing changes in `CannonAimController`, the muzzle, or the fire direction — `MuzzlePoint` is `CannonA`'s child and already aims.

### A3. Animation — idle by default, shoot only on a shot

The pack controller loops its one state, so the mounted cannon fires forever. Replace it at mount time with a project-owned controller:

- New asset `Animations/Vehicles/VehicleCannon.controller` (create the folder), built by an editor step (part of `Create Default Vehicle Definitions`, idempotent — only created when absent):
  - State `Idle` — **empty (no clip), the default state**. The pack ships no idle take (each FBX holds only `Armature|Shoting`), so idle is the rest pose; if an idle clip ever arrives it drops into this state.
  - State `Shot` — clip = the `Armature|Shoting` `AnimationClip` sub-asset of `Assets/…/Models/Cannon_A.fbx` (find it via `AssetDatabase.LoadAllAssetsAtPath`). The armature names are identical across the pack, so the A clip animates B and C models too.
  - Trigger parameter **`Shot`**; transitions `Idle → Shot` (condition: the trigger, duration 0, no exit time) and `Shot → Idle` (exit time 1, duration 0, no conditions) — the clip's baked `loopTime` never matters because the state exits after one pass.
- `VehicleMount` gains `[SerializeField] private RuntimeAnimatorController mountedController;` — applied to `CurrentAnimator.runtimeAnimatorController` on every spawn (when set). Wired by the builder via `SetIfEmpty`.
- `CannonShotPresenter`: the mounted path becomes `SetTrigger` — replace `mountedShotState` with `[SerializeField] private string mountedShotTrigger = "Shot";` (hash cached as before), and call `mountedAnimator.SetTrigger(hash)`. The legacy `_cannonAnimator.Play(Cannon_Shot)` fallback and the smoke stay exactly as they are. `ResetPresentation` additionally `ResetTrigger`s the mounted animator so a queued shot cannot fire the animation after a run ends.

### A acceptance

1. `Fit Vehicle Models` logs nine sensible ratios and fills every `modelScale`; the mounted Cannon A at level 1 stands about as tall as `CannonA` did (toggle `CannonA` active in the editor to compare); re-running the tool is stable.
2. In play the vehicle base and wheels never move; dragging the aim tilts/turns the vehicle's barrel exactly as far as the invisible `CannonA` (enable it once to verify they track), and shots still leave from `MuzzlePoint` in the aimed direction.
3. On entering play the mounted cannon is **still** (idle pose, no looping fire animation). Each shot plays `Armature|Shoting` once — together with the smoke — and returns to rest; holding fire across ten quick shots restarts it cleanly. The tank fallback still uses the legacy `Cannon_Shot`.
4. Upgrading/switching vehicles re-applies scale, barrel follow, and the controller on the new model; domain-reload-off double-run stays clean.

---

## B. Loading screen — the missing logo

`Prefabs/UI/Loading/LoadingScreen.prefab` gets one child, and `Editor/LoadingScreenBuilder.cs` ensures it (idempotent, like the rest of that builder):

| Child | Component | Anchors of the root | From `Splash Screen_ref.png` (1216×2160, drawn 1:1) |
|---|---|---|---|
| `GameIcon` | Image `Textures/UI/Loading/GameIcon.png`, Simple, preserveAspect, raycastTarget off | `(0.169, 0.634)–(0.830, 0.919)` | logo x 205–1010, y 175–790 |

Sibling order: directly **after `Background`** (under the label and bar, which don't overlap it anyway). Nothing else on the screen moves; the view code is untouched.

### B acceptance

Launch: splash background, the Smash City logo at the top as in the ref, `Loading...` and the bar below, bar behaviour unchanged; builder re-run is a no-op.

---

## C. Garage rows — drop the row-level button hook and the CanvasGroup dim

The SELECT/EQUIPPED button is now the one and only equip affordance and the one state cue. The older mechanisms go:

- **`ShopItemView`**: delete the `group`, `equippedAlpha`, `otherAlpha` fields, the `group.alpha = …` line in `Bind`, and the `group = GetComponent<CanvasGroup>()` resolve. Delete the root-button fallback in `ResolveMissingReferences` — `selectButton` resolves **only** from the child named `Select`; a row is never itself a button again. Everything else in `Bind` (icon, lock, label, pips, buy caption, Select/Equipped toggling) stays.
- **Both row prefabs**: remove the `CanvasGroup` component from the root (during the builder's prefab pass: `Object.DestroyImmediate(component, true)` inside `LoadPrefabContents`). There is no root Button today — assert that stays true rather than adding removal code for it.
- **`Editor/GarageScreenBuilder.cs`**: stop ensuring a `CanvasGroup` on the row prefabs; add the removal above so an already-built project is cleaned by a re-run.
- The shop views need nothing: they bind through `ShopItemView.State` (whose `Equipped` flag still drives the chip) and wire `SelectButton` clicks as before.

### C acceptance

1. `Build Garage Screen` re-run removes the CanvasGroups from both prefabs; the scene instance shows every row at full alpha in every state; second run no-op; no console warnings from `ShopItemView` about missing groups.
2. Locked, owned, equipped rows are told apart by the LOCKED graphic, the SELECT button and the EQUIPPED chip alone; tapping anywhere on a row outside its two buttons does nothing.
3. Selecting, buying, upgrading, and the preview all behave exactly as before (the removal touched presentation only).

---

## Out of scope

Per-model muzzle points, an authored idle clip, wheel motion, aim smoothing, any garage layout change beyond the removals, resizing `CannonA` itself, and the pack's D family.
