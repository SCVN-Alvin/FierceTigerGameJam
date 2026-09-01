# Task Brief 25 — Garage: real icons wired, and a spinning 3D model in the preview

Branch **`Feature/GaragePreview3D`** from `main` **after Brief 24 lands** (it needs the `ICN_*` sprites and the `Tank_Preview_3D` slot). House rules as always.

Repository: `/Volumes/Supercent/FierceTigerGameJam/FierceTigerGameJam` — paths relative to `Assets/GameJam/`.

## A. Icons into the definitions

All 18 sprites live in `Sprites/` (imported as Sprite by the artist). Wire per level with `SetIfEmpty` in the existing builders (`VehicleDefinitionBuilder`, `BulletDefinitionBuilder`), path constants in one table:

| Definition | Level 1 / 2 / 3 |
|---|---|
| `cannon_a` | `ICN_Tank_Blue_1 / _2 / _3` |
| `cannon_b` | `ICN_Tank_Green_1 / _2 / _3` |
| `cannon_c` | `ICN_Tank_Orange_1 / _2 / _3` |
| `rock_type` | `ICN_Boom_1 / _2` (the `_3` waits for a level 3) |
| `cannon_type` | `ICN_Rocket_1 / _2` |

The colour mapping is an assumption to **verify in the editor** before committing: open each `Cannon_X_URP` prefab and match its body colour to the icon (the pack has blue/green/orange/purple materials). If B or C is actually Purple, swap the constant — one line. `ICN_Tank_Purple_*` stays as the spare for a fourth vehicle.

Nothing else changes: the garage rows and the (current, sprite-based) preview already resolve `ResolveIcon(level)`; after this step every row shows its real icon, locked rows show the level-1 icon, and upgrading changes it. The artist's stand-in sprites hard-assigned in the row prefabs are cleared back to None by the builder (the icon comes from `Bind`, and a baked sprite would flash before the first bind — blank-until-bound is the existing rule from `a34d7277`).

## B. The 3D preview — camera → RenderTexture → the artist's slot

The artist marked the spot: **`Tank_Preview_3D`** under each panel's `Preview`. Approach (as you suggested — a rig camera rendering the model to a RenderTexture shown in the UI; it is the right call for a jam: no stacked-camera or RenderObjects tricks, works identically on device):

1. **One rig, shared by both tabs** (only one panel is ever active): scene object `PreviewRig` under `=====SYSTEM=====` at `(0, -50, 0)` — far below the playfield:
   - `Pivot` (empty, models spawn under it),
   - `Camera`: orthographic? No — perspective ~30° FOV at z −4 looking at the pivot, clear flags Solid Colour with **alpha 0** (the garage frame art stays the backdrop), culling mask = new layer **`Preview`** only; everything under the rig is on that layer; the Main Camera's mask drops `Preview`.
   - A `Light` (directional, rig-local, `Preview` layer via culling mask) so the model is lit the same wherever the sun is.
2. **`RT_Preview.renderTexture`** asset (512×512, 16-bit depth), created by the builder; the camera targets it.
3. **`ModelPreviewView`** (`Scripts/UI/ModelPreviewView.cs`) on each `Tank_Preview_3D`:
   - Ensures a `RawImage` (texture = the RT, `raycastTarget` off) sized to the artist's rect; `Color.white`.
   - `Show(GameObject modelPrefab)` / `Clear()`: forwarded to a small scene singleton **`ModelPreviewRig`** (`Scripts/Gameplay/Tool/ModelPreviewRig.cs`) that owns Pivot/Camera: despawn the old model, instantiate the new under `Pivot`, `SetLayerRecursively(Preview)`, strip colliders/rigidbodies/`GridKnockdownCannonProjectile`/`Animator` state machines left running (keep renderers only — the copy is a mannequin), **auto-fit**: uniform-scale so the renderer-bounds' largest dimension fills ~70 % of the frame and centre the bounds on the pivot (same bounds trick as Brief 16's fit tool, at runtime). Camera enabled only while something is shown — a disabled preview costs nothing.
   - **Spin**: the rig rotates `Pivot` at `[SerializeField] float degreesPerSecond = 40` around Y in `Update`. That is the whole "model spins around itself".
4. **Who shows what**: `VehicleShopView.RefreshPreview` — instead of (alongside) the icon sprite: `preview3D.Show(selected.ResolveModelPrefab(level))`. `BulletShopView.RefreshPreview`: `Show(selected.ProjectilePrefab?.gameObject)` and the rig enables the right `…LV{level}` child on the copy (reuse the projectile's suffix-matching rule). When a model resolves, the flat `PreviewItem` image is hidden; when none does (missing art), fall back to the icon sprite exactly as today — the 3D slot never shows an empty spin.
5. **Builder**: `Build Garage Screen` gains the rig steps — RT asset, `PreviewRig` scene object + layer creation (`Preview` added to the tag manager if absent), `ModelPreviewView` on both `Tank_Preview_3D`s, references wired `SetIfEmpty`, Main Camera's culling mask updated. Idempotent.

Considered and rejected, for the record: a second camera stacked over the UI per panel (URP camera-stack cost + ordering headaches on device), and world-space UI with the model parented into the canvas (scale/lighting leaks). The RT rig is the boring, correct one.

## Acceptance

1. Garage vehicle tab: the equipped cannon's actual model spins slowly in the preview window, correctly sized whichever of the nine models is equipped; switching vehicle or upgrading swaps the model without leaks (old copy destroyed).
2. Ammo tab: the equipped bullet's projectile model spins, showing the right LV mesh for its level; the fallback icon appears only when a prefab is missing.
3. Rows and both previews show the mapped `ICN_*` art; locked rows show level 1; colour mapping confirmed against the models.
4. The rig is invisible to gameplay (Main Camera never renders the `Preview` layer, the mannequin has no colliders — shoot a run to prove nothing collides at y −50), and costs nothing while the garage is closed (camera disabled).
5. Builder re-run no-op; domain-reload-off double-run leaves one rig, one RT, no duplicated models.

## Out of scope

The projectile spin removal and the flight arc (Brief 26), Purple's fourth vehicle, preview lighting polish, touch-to-rotate in the preview.
