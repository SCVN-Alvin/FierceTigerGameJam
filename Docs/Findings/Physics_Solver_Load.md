# Finding — physics solver load is the dominant runtime cost

Recorded 2026-08-25 while working Task Brief 03. Not covered by any brief; parked here so it
can be picked up deliberately rather than rediscovered.

## What was measured

OPPO CPH2591 (Mali-G52 MC2, Helio G85, 8 cores, 3.7 GB, Android 15), development build,
`test_03.json` (471 blocks), numbers from `RuntimeProfileLogger` in logcat.

During a cascade, with Brief 02 and Task Brief 03's P0-2 and P1-2 already applied:

| | Value |
|---|---|
| Physics time per one-second window | up to **301 ms** (~30% of wall clock) |
| Worst single frame inside physics | **33.0 ms** |
| Peak awake rigidbodies | **320–444** |
| Frame budget at the current 30 fps cap | 33.3 ms |

A single frame spent essentially the whole budget inside physics alone. This is sustained
across the part of the game the player actually watches, unlike the map-build spike, which is
one hiccup behind a UI transition.

## What has already been done about it

- **Brief 02 step 1** — blocks dropped to `Discrete` collision detection and no interpolation.
- **Brief 03 P1-2** — the support cascade no longer scans every sibling per activation.
  Normalised across windows with comparable body counts (180–340 awake), physics cost fell from
  **1.214 to 0.920 ms per body-second**, about 24%.

Both helped. Neither addresses the underlying quantity: several hundred rigidbodies awake at
once, all solving contacts against each other.

## Why the measurement is soft

The before/after comparison came from two manual runs whose cascades were not the same size
(444 bodies versus 320). The per-body normalisation above is the fairer read, but it rests on
five windows from one run. Anything quantitative here wants a repeatable cascade first — a
scripted shot at a fixed position and impulse — before a number is put in a report.

## Where to look next, roughly in order

1. **Sleep settled blocks sooner.** `m_SleepThreshold` is 0.005, which is very low; a block that
   has come to rest keeps being re-woken by neighbours settling around it. This is the cheapest
   lever and the most likely to pay.
2. **Solver iteration count.** `m_DefaultSolverIterations: 6` with velocity 1. Six is generous
   for blocks that only have to topple convincingly.
3. **Cap simultaneously awake bodies**, the way debris is capped at twelve bursts and
   ninety-six chunks. A cascade beyond some size is not more readable, only more expensive.
4. **Contact offset / bounce threshold** — 0.01 and 2 today, untouched and unmeasured.

## What not to do

Do not raise the fixed timestep to buy frame time. At 0.02 the structure already settles
visibly slowly; a coarser step makes stacked blocks jitter and sink, which is the one thing a
knockdown game cannot afford.
