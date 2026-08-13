# Smash Builder Web

Local browser tool that converts a building GLB into a surface shell assembled
from the project's Brick, Concrete and Glass FBX assets.

## Run

Double-click `Start Smash Builder.command` trong Finder (chỉ mở file này trong
VS Code/Cursor sẽ không chạy server), hoặc chạy:

```bash
python3 serve.py
```

Open <http://127.0.0.1:4173> if the browser does not open automatically.
Do not open `index.html` with a `file:///` URL; browsers block the JavaScript
modules needed by the converter. If `index.html` is opened by mistake, it shows
instructions to run the launcher instead of redirecting to a server that has
not started yet.

`127.0.0.1` / `localhost` means this Mac itself, not a remote server. Keep the
Terminal window opened by the launcher running for the entire session. Closing
that window stops the local server.

The first load needs internet access to fetch the pinned Three.js and FBX
exporter modules. Uploaded models stay inside the browser and are not sent to a
server.

## Output

- `*_Smash.fbx`: Unity-axis binary FBX containing named Brick, Concrete, Glass
  and separated detail groups.
- `*_layers.json`: layer-based grid for Unity. Z is represented by the parent
  `layers[].index`; every cell stores only integer `x`, `y`, `material` and its
  snapped surface `face`. Unity reconstructs world positions using the single
  `grid.originCellCenter` and `grid.cellSize` values.

Automatic texture mapping recognizes orange/terracotta as Brick, neutral gray
as Concrete, and cyan/mint/blue as Glass. Unmatched colors are preserved in the
separate Details geometry. Concrete and Glass use one placement per logical
cell. Brick preserves the authored FBX proportions with one uniform course-fit
scale and is laid
horizontally across each continuous wall region in running bond: alternating
rows are shifted by half a brick. Pieces at wall, door and material boundaries
are shortened so the Brick surface does not protrude outside its mask. The
simple JSON layer map remains cell-based, while `physicalPlacements` stores the
exact staggered placement, edge-cut ratio and instance scale used by the FBX.

FBX cannot store Unity Rigidbody, Collider or MonoBehaviour components. Unity
must add those after importing, based on the exported node names or the layout
JSON.
