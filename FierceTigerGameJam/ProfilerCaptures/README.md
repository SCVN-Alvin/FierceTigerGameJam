# Profiler captures — Task Brief 03

## Device

| | |
|---|---|
| Model | OPPO CPH2591 |
| SoC | MediaTek Helio G85 (MT6769V/CZ), 8 cores, ARM64 |
| GPU | Mali-G52 MC2, Vulkan (driver r49p1) |
| Memory | 3.7 GB |
| OS | Android 15 (SDK 35) |
| Screen | 720 x 1612 |
| Unity | 6000.3.15f1, development build, IL2CPP |
| Quality level | Mobile (`Mobile_RPAsset`) |

Test map is `test_03.json`, 471 blocks.

## How these were taken

Not with the Profiler window. `RuntimeProfileLogger` prints the same figures to the device log
as `FTPROF|` lines, which are read over adb:

    adb logcat -s Unity:* | grep FTPROF

This is why there are no `.data` files here. The trade is deliberate: the numbers can be
re-taken after every change without anyone sitting in front of the editor, at the cost of the
Profiler's timeline view. If a deep dive is ever needed, capture `.data` alongside.

## Results

Frame times are the worst frame in the one-second window containing the event. `elapsed_ms` is
the `BuildMap` call itself. "Cold" is the first build of a session and includes first-time
prefab and shader loading; "warm" is any later build.

| Step | Build cold | Build warm | `BuildMap` itself | Build-window GC | AddComponent per build |
|---|---|---|---|---|---|
| Baseline (after Brief 02) | 696 ms | 149 ms | *not captured* | 845 KB | 942 |
| After P0-2 | 398 ms | 116 ms | 184 / 109 ms | 860 KB | **0** |
| After P1-2 + pool warm bump | 547 ms | *not captured* | **137 ms** | 998 KB | 0 |

Physics, during cascades:

| Step | Physics per second | Worst frame in physics | Peak awake bodies | ms per body-second |
|---|---|---|---|---|
| After P0-2 | 393.9 ms | 37.8 ms | 444 | 1.214 |
| After P1-2 | 301.4 ms | 33.0 ms | 320 | **0.920** |

Debris pool, across the same runs:

| Step | Built during play | Built at run start | Peak sessions / chunks |
|---|---|---|---|
| After P0-2 | 49 | ~0 | 12 / 96 |
| After P1-2 (`warmPerType` 4 → 12) | **1** | 36 | 12 / 96 |

## Reading these carefully

- **The two runs are not controlled.** Cascade size differed (444 versus 320 awake bodies), so
  the raw physics peaks flatter P1-2. The per-body-second column is the fair comparison, and it
  rests on five windows from one manual run.
- **The P1-2 row's build got worse on purpose.** Warming twelve debris instances per type
  instead of four moved 36 instantiations into the build window. `BuildMap` itself got faster
  (184 → 137 ms); the extra 150 ms is the pool. Mid-cascade instantiation went from 49 to 1 in
  exchange. Revisit if the sub-100 ms build target matters more than cascade smoothness.
- **Startup frames are excluded.** Frames of 2319 ms and 1129 ms appear seconds after
  `Initialize engine`, and 2499 ms after an `onResume`. These are engine startup and app resume,
  not gameplay.
- **Everything here sits under a 30 fps cap.** `Application.targetFrameRate` is unset, so
  Android's default applies and no frame can measure better than 33.3 ms. P2-3 lifts this, and
  will make every number above incomparable — re-baseline when it lands.

## Still open

- **P0-1** (does selecting a map build it twice?) is unresolved. No two builds of the same map
  ever landed close enough together to tell. To settle it: select `test_03` and wait before
  pressing Start Run — a `FTPROF|phase_begin` on the tap alone confirms it.
- **Physics solver load** is the largest remaining cost and is not in any brief. See
  `Docs/Findings/Physics_Solver_Load.md`.
