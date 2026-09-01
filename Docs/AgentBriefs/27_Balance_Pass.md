# Task Brief 27 — Balance pass: bullets vs blocks, tuned so the campaign progresses easily

Branch **`Feature/BalancePass`** from `main`, one-line commits. Mostly data edits plus one small editor tool; run **after Brief 26** (the arc changes where shots land, so playtests before it would mislead).

Repository: `/Volumes/Supercent/FierceTigerGameJam/FierceTigerGameJam` — paths relative to `Assets/GameJam/`.

## The numbers on the table today (verified)

Block HP: **glass 1, brick 3, concrete 6** (`maxHitPoints` in the block prefabs; **verify `brick_2x1`** — its HP was not confirmed, balance below assumes 6; adjust the matrix if it differs). Clear requirement: **50 %** everywhere (tutorial 80 %). Clearing counts blocks **knocked down, dropped or broken** — damage is only part of clearing; toppling does a lot of the work, which is why this pass gates *materials*, not raw output.

Campaign composition (from the mission JSONs) and budgets:

| Map | brick_1x1 | brick_2x1 | glass | concrete | total | budget |
|---|---|---|---|---|---|---|
| m1_map1 | 58 | 19 | — | — | 77 | 10 |
| m1_map2 | 82 | 22 | — | — | 104 | 12 |
| m1_map3 | 151 | 66 | — | — | 217 | 14 |
| m1_map4 | 74 | 32 | 60 | — | 166 | 14 |
| m1_map5 | 112 | 46 | 26 | — | 184 | 16 |
| m1_map6 | 126 | 48 | 48 | — | 222 | 16 |
| m1_map7 | 78 | 30 | 28 | 84 | 220 | 18 |
| m1_map8 | 120 | 28 | 80 | 132 | 360 | 20 |
| m1_map9 | 130 | 44 | 18 | 76 | 268 | 20 |
| m2_map1–3 | 42–319 brick | — | — | — | 57–319 | 12–25 |

Concrete first appears on **map 7** — that is the campaign's gear check: the player must own Cannon Ball (the only concrete-capable type) by then. Current prices: Cannon Ball unlock 500? — read the actual `PurchaseBulletConfig` and make sure cumulative pass rewards from maps 1–6 comfortably cover the unlock (rewards live in `RewardConfig`; raise reward values, not lower the price, if they fall short — spending income is the loop).

## The new damage matrix — `Config/Bullets/Rock.asset`, `Cannon.asset`

Principles: the starter one-shots what its tier is about; splash (share 0.35) should finish glass neighbours; concrete stays Rock-proof (0) — it is the unlock gate, not a grind; a max vehicle (×2.6) may trivialise everything, that is what it was bought for.

| | glass (1) | brick (3) | brick_2x1 (6?) | concrete (6) |
|---|---|---|---|---|
| **Rock I** | 3 *(splash 1.05 ≥ 1: neighbours die)* | 3 *(1 shot)* | 3 *(2 shots / topple)* | 0 |
| **Rock II** | 4 | 6 *(2x1 in one)* | 6 | 0 |
| **Cannon I** | 6 | 6 | 6 | 3 *(2 shots; ×2 vehicle → 1)* |
| **Cannon II** | 8 | 8 | 8 | 6 *(1 shot)* |

(`wallDamage` is dead since Brief 23 — leave whatever sits in the fields.) Vehicle multipliers (A 1.0/1.2/1.4, B 1.3/1.6/2.0, C 1.5/2.0/2.6) stay; they now visibly change concrete STK, which is the upsell.

## The tool that keeps this honest — `Tools > Smashdown > Balance Report`

`Editor/BalanceReport.cs`: for every map in `MapConfig`, using the live configs (definitions, HP from the block prefabs, budgets, requirement), print one row: composition, budget, and **worst-case shots to the requirement** = cheapest-first damage-only accounting (`ceil(HP/dmg)` per block, fill to 50 % of the count, no topple credit, splash credited only against glass) for two loadouts — *Rock-only at the level expected by that map* and *Cannon-only*. Flag red anything over **70 % of budget** (the 30 % head-room is the topple credit, deliberately unmodelled). The tool is the acceptance gate and the thing to re-run after any map or config edit; it must come out all-green for: Rock I on maps 1–3, Rock II on 4–6, Cannon I on 7–9 (Cannon II green-only on 8), mission 2 with Rock II.

## Playtest confirmation (the part the model cannot see)

With the matrix in and the report green, play maps 1, 4, 7 and 9 with exactly the expected loadout: each passed with **≥ 30 % of the budget unspent**, no map needing pixel-perfect aim, map 7 failing with Rock-only (the gate works, and the fail screen's continue is the honest out). Record shots-used per map in the run notes; tune `RewardConfig` so the map-6 cumulative gold covers Cannon Ball + one vehicle level with slack.

## Acceptance

1. The matrix above is in the assets (via the definitions builder's tables so a rebuild reproduces it); `Balance Report` exists, is idempotent, and prints all-green for the ladder above; `brick_2x1` HP verified and the matrix adjusted if it isn't 6.
2. Playtest checklist passed and recorded; economy check done (Cannon Ball affordable by map 7 from pass rewards alone).
3. Tutorial untouched (3 shots, one brick block — Rock I still one-shots it).
4. No code changes outside the editor tool; damage values live only in the two bullet assets and the builder tables.

## Out of scope

Vehicle price/multiplier retunes, per-map required-percent changes, mission 2's dev maps beyond the report row, difficulty modes, and modelling topple physics in the report (the 30 % head-room stands in for it).
