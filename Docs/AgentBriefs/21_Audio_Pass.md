# Task Brief 21 — Sound: the WhackStack pack wired into the whole game

Branch **`Feature/Audio`** from `main`, one-line commits, no body. House rules as always (idempotent builders, `SetIfEmpty`, subscribers unsubscribe in `OnDisable`, no LINQ in runtime paths, XML doc comments say *why*).

Repository: `/Volumes/Supercent/FierceTigerGameJam/FierceTigerGameJam` — paths relative to `Assets/GameJam/`. Source audio: **`/Users/duongtrinh/Desktop/WhackStack/`** — 29 mp3 (verified list below). The game currently has **zero audio code** — no `AudioSource` anywhere in `Scripts/` — so this brief builds the base and the hookups in one pass.

## 0. Import

Copy every mp3 into `Assets/GameJam/Audio/` (keep the file names; the unused ones come along — they cost little and the next event may want them). Import settings for all: Force To Mono, Vorbis; short SFX **Decompress On Load**, the two `bgm_*` **Streaming**. Applied by the builder via `AudioImporter` so re-imports stay consistent.

## 1. The two assets

**`AudioConfig`** (`Scripts/Audio/AudioConfig.cs`, asset `Config/AudioConfig.asset`) — every slot is an `AudioClip[]` (a random entry plays, so one-clip slots still work and variants can be dropped in later) plus `[Range(0,1)]` volumes:

| Slot | Clip(s) | Played when |
|---|---|---|
| `fire` | sfx_canonexplose | a shot leaves the cannon |
| `ballImpact` | sfx_ballimpact | the ball hits a block |
| `ballFall` | sfx_ballfall | the ball lands on the floor (Brief 20's contact) |
| `hitBrick` / `hitConcrete` / `hitGlass` | sfx_hitbrick / sfx_hitcement / **sfx_hitice** | a block is damaged but survives (ice is the closest clink the pack has for glass) |
| `breakBrick` / `breakConcrete` / `breakGlass` | sfx_crackedbrick / sfx_crackedcement / sfx_crackedglass | a block shatters |
| `uiClick` | sfx_buttonclick | any UI button |
| `denied` | sfx_denied | a refused purchase/upgrade/continue |
| `coin` | sfx_coinreward | gold is granted or spent successfully |
| `stageClear` / `stageFailed` | sfx_stageclear / sfx_stagefailed | the cleared / fail screen opens |
| `musicTitle` / `musicGame` | bgm_title / bgm_game | menu states / a run |

Plus `musicVolume`, `sfxVolume`. Unused pack files (`aluminum`, `steel`, `wood`, `blackhole`, `heart*`, `nexthousecomplete`, the remaining `ice` breaks) stay imported and unwired — say so in a comment on the config.

**`AudioService`** (`Scripts/Audio/AudioService.cs`) — a scene MonoBehaviour under `=====SYSTEM=====` (single scene, no DontDestroyOnLoad): one looping music `AudioSource` + a small round-robin of 8 SFX sources (`PlayOneShot` overlaps break down when a collapse fires twenty breaks in a frame — the round-robin also lets a per-frame cap of ~4 identical clips stop the noise wall). API: `PlaySfx(slot)`, `PlayMaterialHit(materialId)`, `PlayMaterialBreak(materialId)` (brick/glass/concrete mapped ordinal, unknown → brick), `PlayMusic(slot)` (no-op when that clip is already looping — state changes must not restart the track). A static `AudioService.Instance` set in `OnEnable`, null-tolerant callers everywhere: **no sound is ever load-bearing**.

## 2. Hookups (each one line-ish, at the source of truth)

| Event | Where |
|---|---|
| fire | `GridKnockdownCannonFireController.Fire` — beside `shotPresenter.PlayShot()` |
| ballImpact | `GridKnockdownCannonProjectile` — where the block hit is accepted (`hasHit` set on a block) |
| ballFall | the floor-contact branch (Brief 20) — only on the first floor contact of a flight |
| hit / break per material | `BreakableBlock.ApplyDamage` — survived → `PlayMaterialHit`, broke → `PlayMaterialBreak` (in `Break`, so floor-shatters sound too). Debris impacts stay silent |
| uiClick | new `Scripts/UI/ButtonClickSound.cs` (`[RequireComponent(typeof(Button))]`, plays on click). A sweep in the builder adds it to every `Button` in every UI prefab and the scene, and `UiBuilder.EnsureButton` / `EnsureSpriteButton` add it to buttons they create — new buttons are born clicking |
| denied | the garage/fail views where a `Try*` returns false after a tap (the existing "refused → Refresh" spots) |
| coin | `EconomyService` — after a successful spend or grant, next to `GoldChanged?.Invoke()` (service is a ScriptableObject: route through `AudioService.Instance`, null-checked) |
| stageClear / stageFailed | `ClearedScreenView.Show` (passed results only) / `FailScreenView.OnEnable` |
| music | a small `MusicDirector` on the AudioService object listening to `flow.StateChanged`: menu-ish states (MainMenu, IapShop, Shop, MapSelection, Loading) → `musicTitle`; Playing/Result → `musicGame`. Same-group transitions never restart |

## 3. Builder

`Tools > Smashdown > Set Up Audio` (`Editor/AudioSetup.cs`): copies/imports §0 if the files are present at the source path (skip + log otherwise), creates `AudioConfig.asset` with the §1 wiring (`SetIfEmpty` per slot), ensures the `AudioService` + `MusicDirector` scene object, wires `flow`, runs the `ButtonClickSound` sweep. Idempotent; the sweep never doubles a component.

## 4. Acceptance

1. Menu boots with bgm_title; entering a run cross-switches to bgm_game and back out again; reopening the garage does not restart the title track.
2. A full shot tells its story: canonexplose on fire, ballimpact on the block, material-correct hit/cracked sounds as damage lands, ballfall when a miss rolls onto the floor; a big collapse stays listenable (cap works, no one-frame roar).
3. Every button in every screen clicks; a refused buy plays denied; a successful buy plays coin; the cleared screen plays stageclear exactly once, the fail screen stagefailed.
4. Tutorial run is fully scored too (it is the normal path). With the `AudioService` object disabled the game runs silent with zero errors.
5. Builder re-run is a no-op; domain-reload-off double-run leaves single listeners and one music source.

## Out of scope

Volume/mute UI in the settings panel (natural next step — the panel still says "Nothing to configure yet"; note it, don't build it), positional 3D audio, audio ducking, the unused pack slots, replacing the glass-hit stand-in when a real clip arrives.
