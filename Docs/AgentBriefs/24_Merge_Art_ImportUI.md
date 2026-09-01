# Task Brief 24 — Merge `Art/Import_UI` into `main`

Bring the artist's icon-and-preview branch in. **Run before Briefs 25–27** (25 wires the icons and fills the preview slot this branch adds).

Analysed at `origin/Art/Import_UI` = `042ad54f` ("[Art] Create shop 3D review", on top of "[Art] import UI"), branched from `f8a10003`; `main` at `fe6b9eac`. Fetch first and re-derive if either tip moved (same commands as Brief 19).

## What the branch contains

- **18 icon sprites** in a new `Assets/GameJam/Sprites/` folder, imported as Sprite: `ICN_Boom_1..3`, `ICN_Rocket_1..3`, `ICN_Tank_{Blue,Green,Orange,Purple}_1..3` (four tank colours × three levels — one colour more than we have vehicles).
- **`GarageScreen.prefab`**: two new placeholder objects named **`Tank_Preview_3D`** (one per panel) marking where the 3D model preview goes, and `ICN_Tank_Blue_1` dropped into the vehicle `PreviewItem` as a stand-in. No RawImage/RenderTexture — that machinery is ours (Brief 25).
- **Row prefabs** (`BulletTypeViewItem`, `VehicleTypeViewItem`): icon-slot rect tweaks and `ICN_Boom_1` as a stand-in sprite.
- Cannon-pack **material tweaks** (a dozen `*_URP.mat`), `Texture/` → `Textures/` renames (R100), and one stray file at the **repo root**: `FierceTigerGameJam/ICN_Boom_2.png`.

## Merge

No file was changed on both sides since the base (verified — the garage prefabs and builders on `main` are untouched since `f8a10003`), so expect a clean auto-merge:

```
git checkout -b Merge/ArtImportUI main
git merge origin/Art/Import_UI
```

If a conflict appears anyway (tips moved), resolve prefabs in the artist's favour and code in `main`'s, then re-check in the editor.

## Post-merge verification and housekeeping

1. Open the garage: both tabs render, the artist's stand-in sprites show in `PreviewItem` and the row icons, `Tank_Preview_3D` exists under both panels, nothing regressed in rows/tabs/buttons.
2. Re-run `Tools > Smashdown > Build Garage Screen` and diff: it must be a no-op apart from possibly re-asserting its own values — it must **not** remove `Tank_Preview_3D` or overwrite the artist's sprites (the builders only ensure and `SetIfEmpty`; verify that held).
3. The stray root `FierceTigerGameJam/ICN_Boom_2.png`: confirm byte-identical to `Assets/GameJam/Sprites/ICN_Boom_2.png` and delete it (it is outside `Assets/`, so Unity ignores it — pure repo litter). Note it to the artist.
4. Pack material edits: mounted cannons still look right in play (the artist tuned colours); play one shot for the animation/materials.
5. Land on `main` (fast-forward from the integration branch), push, delete the branch. Nothing in these two commits touches code, so no compile risk.

## Out of scope

Wiring the icons into the definitions, the RenderTexture preview, the fourth tank colour — all Brief 25. The other art branches (`Art/Setup_Scene`) are not part of this merge.
