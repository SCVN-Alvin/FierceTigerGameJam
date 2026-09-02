# SMASH — Kiểm tra nhánh `main` + Kế hoạch đem `Falcon/UpdateMapData` qua

Ngày: 2026-08-31 · Nhánh khảo sát: `main` @ `b7dfbed3` · 127 file C#, ~32.700 dòng
Merge-base với Falcon: `2d7fa643` · Falcon đi trước 2 commit: `9061711d`, `cbbe550e`

---

## PHẦN 1 — NHỮNG GÌ CẦN CHÚ Ý (bẫy có sẵn trong `main`)

### 1.1 Chặn build ngay lập tức

| Vấn đề | Vị trí | Ghi chú |
|---|---|---|
| Bundle id còn mặc định | `ProjectSettings.asset` → `com.UnityTechnologies.com.unity.template.urpblank` | Không lên store được |
| Company/product mặc định | `DefaultCompany` / `FierceTigerGameJam` | |
| Không có keystore / keyalias | `ProjectSettings.asset` | Không ký được APK/AAB |
| Version 0.1.0, versionCode 1 | | Chưa đặt |
| Orientation = auto-rotate (`4`) | | Game dọc mà cho xoay tự do |
| Scene list còn `SampleScene` | Build Settings | Đang disable, nên xoá hẳn |

Android đã đúng: IL2CPP + ARM64.

### 1.2 Game đang CÂM trên `main`

- Code audio nối đủ (bắn, va chạm, vỡ theo vật liệu, click UI, vàng, thắng/thua, `MusicDirector` 2 track).
- **Nhưng `AudioConfig.asset` không tồn tại**, và GUID của `AudioService` không nằm trong scene hay prefab nào.
- Chỉ chạy nếu ai đó bấm `Tools/Smashdown/Set Up Audio` — mà tool đó (`Editor/AudioSetup.cs:72`) trỏ vào đường dẫn cứng `/Users/duongtrinh/Desktop/WhackStack`. Máy khác chạy là gãy.
- **29 file `.meta` của `Assets/GameJam/Audio/` chưa commit** trong khi mp3 đã commit → máy khác import sẽ sinh GUID mới, đứt toàn bộ wiring. Cần commit gấp.

### 1.3 Tiền

- `UI/IapShopView.cs:81-83` — bấm mua = **tặng vàng miễn phí**, không tính tiền. Giá `$0.99/$4.99/$9.99` và gói `500/3000/7500` hardcode trong code.
- Phải thay trước khi có bất kỳ bản build nào ra ngoài.

### 1.4 Code chết còn trong cây

- **Cả một stack demo song song**: `SmashBlock`, `DemoLevelRuntimeBuilder`, `CannonFireController` (copy nguyên logic raycast của `GridKnockdownCannonFireController`), `CannonProjectile`, `RuntimeGlassFracture`, `DemoGameplay.unity`, `FBX/ModelDemo.fbx` (34MB).
- `Gameplay/Wall/StructureMapLoader.cs` — không ai tham chiếu.
- `CannonAimController.cs:8-10,167-201` — toàn bộ đường drag-aim chết, không ai subscribe.
- `UI/SettingsPanelView.cs` — chết hoàn toàn, settings thật do `GameFlowController` lo.
- `UI/CanvasColumnLimiter.cs` — class rỗng.
- `GameState.AmmoPick` — enum zombie, giữ lại chỉ vì `BottomBarView.Slot.state` serialize theo giá trị số.
- `Scene/BuildData.unity` — scene nháp còn sót (`Cube.002 (n)`).

### 1.5 Dữ liệu bẩn

- Map id `"2"` (tên `Lv8_Test03`) — typo đã ăn vào **3 file**: `MapConfig.asset:51`, `MapProgressionConfig.asset`, mission slot 8.
- Mission 2 mặc định trỏ `map_004_level_01`, `map_005_level_02` — bản trùng cũ của map đã có ở Mission 1.
- 4 JSON mồ côi không nằm trong MapConfig: `map_001`, `map_002_footprint_test`, `map_003_wall_groups`, `map_006_level_03`.
- Hai thư mục trùng: `Assets/GameJam/Texture/` và `Assets/GameJam/Textures/`.
- 2123 file `.Png` vs 1481 `.png` — lệch hoa/thường, hỏng trên CI phân biệt hoa thường.

### 1.6 Repo phình

- `.git` **678MB**, working tree 4.0GB. Không dùng Git LFS.
- Vendored: Layer Lab **263MB** (chỉ dùng mỗi font), CelerisLab **90MB** (505 wav), Cannon Pack 6.7MB, Toony Colors Pro 3.9MB.
- Binary lớn đã commit: `ModelDemo.fbx` 34MB, `DemoScene_CasualGame.unity` 32MB, JPG nguồn 13–21MB trong `Imported/`.
- 13 file `Map_*_Meshes.asset` ~130MB (lớn nhất `Map_2_Meshes.asset` 22MB) — **Falcon đã xoá hết đám này**, xem phần 3.

---

## PHẦN 2 — BUG THẬT SỰ (xếp theo mức độ)

### P0 — Bắn xuyên qua UI
`CannonInputShooter.Update` đọc `Pointer.current` mà **không kiểm tra trạng thái run và không kiểm tra `IsPointerOverGameObject`**.
→ Bấm nút HUD cũng bắn. `Enter()` không bao giờ được gọi cho `GameState.Result`, nên `gameplayRoot` vẫn active: màn Cleared hiện rồi người chơi vẫn bắn tiếp bằng đạn thừa.

### P0 — Cascade không lan
`ActivateFromSupportRelease` **không gọi tiếp** `ReleaseSupportedBlocksAbove`.
→ Chỉ cột ngay dưới cục bị bắn được thả. Block tựa lên cục vừa rơi nhưng lệch khỏi cột đó thì treo lơ lửng vĩnh viễn → **không clear 100% được**. Chế độ `OneLevel` chỉ thả `supported[0]`, tức một cục chứ không phải cả hàng.

### P1 — Đạn pool giữ đạn cũ
`GridKnockdownCannonProjectile.OnDisable` xoá `damageMultiplier` nhưng **không xoá `bulletOverride`**; `Fire` chỉ gọi `SetAmmunition` khi `ammunition != null`.
→ Có inventory nhưng chưa nối `bulletLoadout` thì viên thứ 2 ăn nguyên bảng damage của viên thứ 1.

### P1 — `StructureRegistry` không reset
`highestRow` không reset giữa các lần chơi lại, và `Register` ghi đè ô của block khác mà không unregister.
→ Chơi lại nhiều lần thì vòng quét cột đọc vào hàng chết, trả về block không còn phủ ô đó.

### P2 — Thanh % nhảy lùi 1 frame
`BreakableWall.BreakUp` sinh `CellCount` con rồi `Destroy(gameObject)` (deferred). `LevelProgressTracker` lấy mẫu đúng khung đó sẽ đếm cả wall lẫn cell.

### P2 — Giải ngắm chạy đúng khung người chơi chạm
Trượt nghiệm giải tích → `TryGetLaunchDirectionBySimulation` chạy 45×250 bước; `GetFireDirection` còn giải **hai lần** để bù muzzle offset. Giật trên mobile đúng lúc chạm.

### P3 — Vặt
- `ShopTabsView.cs:117` dùng `RemoveAllListeners()` — xoá luôn listener của script khác.
- `MissionPanelView.RealMapSequence` dựng lại toàn bộ chuỗi cho **mỗi** card (`:311`).
- `BreakableWall` bỏ qua `impactPoint`/`impactDirection`; không có audio/VFX lúc vỡ (khác `BreakableBlock`).
- `BreakEffectPool.cs:17` còn TODO. `ProjectilePool.HasPrefab` chết. `BreakableBlock.ApplyImpact` không ai gọi.
- Số ma: `RequiredClearPercent=0.8f`, `BulletPickLimit=10` hardcode trong `LevelRunController.ResolveMapRules`.

---

## PHẦN 3 — NHỮNG GÌ THIẾU (để ship được)

| Hạng mục | Trạng thái trên `main` |
|---|---|
| Cấu hình build Android | **Chưa có** (mục 1.1) |
| Âm thanh chạy được | **Chưa có** — thiếu `AudioConfig.asset` + service trong scene |
| IAP thật | **Chưa có** — đang là stub cho không |
| Màn Settings | **Chưa có** — view chết, không có setting nào |
| Lives / hearts | **Chưa có** — `sfx_heartgain`/`sfx_heartspend` import rồi mà không nối |
| Save migration | Có `version = 1` nhưng **không đọc, không có code migrate** |
| Mission 3 | **Chưa có** (nằm ở Falcon) |
| Mission 2 hoàn chỉnh | **Chưa có** (nằm ở Falcon) |
| Nội dung hiện tại | 9 level chơi được, 17 JSON, 12 trong MapConfig, 2 mission |
| Analytics / crash report | Không thấy |
| Test tự động | Không có |

**Điểm mạnh không phải sửa**: lõi phá huỷ (JSON → block → registry ô lưới → cascade), damage 2 trục theo vật liệu, `BreakableWall` gộp body rồi vỡ ra cell kế thừa vận tốc, pool debris có cap, ngắm ballistic giải tích, save tách record chống hỏng, shop chạy theo config. Phần này là kỹ thuật thật, không phải code jam.

**Đánh giá tổng**: chất lượng game jam đang tiến về ship được. Lõi làm chắc; lớp phát hành (build config, IAP, audio wiring, vệ sinh repo) chưa có.

---

## PHẦN 4 — ĐEM TINH TUÝ TỪ `Falcon/UpdateMapData` QUA

Falcon = `main` + 2 commit trên nền `2d7fa643`. Tổng 138 file, +878.059 / −77.554 dòng.

### 4.1 Nên đem qua

**Nội dung — 18 map mới**
- 9 map Mission 2 (biển): `map_m2_001_beach_hut_row` … `map_m2_009_boardwalk_resort` (JSON + prefab)
- 9 map Mission 3 (vũ trụ): `map_m3_001_relay_dish` … `map_m3_009_spaceport` (JSON + prefab)

**Code — 4 file**
- `Scripts/Gameplay/Playfield/LevelScenery.cs` (+874) — BG/floor theo mission, preview trong edit mode, khung camera trong Scene view
- `Editor/MissionEditorWindow.cs` (+818) — công cụ sửa mission/map
- `Editor/LevelSceneryInspector.cs` (+86)
- `Scripts/UI/MissionPanelView.cs` (±212) — BG theo mission, mở đúng mission xa nhất, `MissionOf`/`SlotOf`/`TryGetMissionScenery`
- `Scripts/Gameplay/Wall/KnockdownLayoutMapAuthoring.cs` (+174) — canh giữa map theo ô có thật
- `Editor/MapPrefabBaker.cs` (±17), `Scripts/Gameplay/Wall/MapConfig.cs` (+1)

**Ảnh — 8 texture**: `BG_Beach_01`, `BG_New_01`, `BG_New_09`, `BG_Universe_01`, `Floor_New_01`, `Floor_New_09`, `Floor_Sand_01`, `Floor_Space_01`

**Tối ưu — xoá `*_Meshes.asset`** (commit `9061711d`): bỏ toàn bộ 13 file mesh baked, ~130MB. Đây là thứ đáng đem qua nhất về mặt dung lượng.

### 4.2 CHÚ Ý — 5 file cả hai nhánh cùng sửa, sẽ conflict

```
Assets/GameJam/Config/MapProgressionConfig.asset
Assets/GameJam/Materials/M_Brick.mat
Assets/GameJam/Materials/M_concrete.mat
Assets/GameJam/Prefabs/UI/Mission/MissionScreen.prefab
Assets/GameJam/Scene/Gameplay.unity
```

- `Gameplay.unity` là nặng nhất: `main` đã sửa scene ở các merge Audio / DragCollapse / WorldFloor / UI backdrop; Falcon thêm object `Level Scenery` + đổi backdrop. **Không merge tay file YAML này** — mở scene bản `main`, rồi thêm lại object `Level Scenery` bằng tay (hoặc bằng tool).
- `MissionScreen.prefab` (±43 dòng) — Falcon thêm trường BG cho mission. Merge được nhưng phải mở Unity kiểm tra.
- 2 file `.mat` chỉ đổi 1 dòng, chọn bản `main`.

### 4.2b UI/UX — LẤY của `main`, không đè bằng Falcon

Nền là `main`, nên toàn bộ UI/UX của `main` **giữ nguyên và lấy hết**, gồm:

- Loading page (`UI/LoadingScreenView.cs`, `Config/LoadingConfig.cs`, `Editor/LoadingScreenBuilder.cs`)
- Tutorial (`Editor/TutorialBuilder.cs`, `Maps/tutorial.json`, cờ tutorial trong `UserData`)
- Backdrop toàn canvas + khung mission rộng hơn (`60fecee7`, `6e5463c6`, `a0556ea8`)
- Garage / shop / bottom bar / cleared / fail như `main` đang có

Falcon phân nhánh **trước** đợt làm UI này nên UI bên Falcon là bản cũ hơn — **không được đem qua đè**. Từ `MissionPanelView.cs` của Falcon chỉ lấy phần logic tra cứu: `MissionOf`, `SlotOf`, `TryGetMissionScenery` (`LevelScenery` cần), còn phần dựng UI thì giữ của `main`.

`MissionScreen.prefab` cũng lấy bản `main`; chỉ thêm trường BG mission nếu thiếu.

Dev bên `main` còn đang làm tiếp UI/UX → sau khi họ xong thì quay lại lấy đợt nữa.

### 4.2c Camera / nòng súng — GIỮ của Falcon

Giữ cách hiển thị của Falcon hiện tại: **camera chỉ thấy nòng súng**, không dựng cả chiếc xe lên.

`main` đã đổi hướng này sau merge-base, nằm ở:

- `Scripts/Gameplay/Cannon/VehicleMount.cs` — thêm `fallbackModel`, `barrelReference`, `barrelNodeName = "Cannon.A"`, `barrelRestPitchDegrees = 31.44`, `mountedController`; mục đích là gắn nguyên model xe thay vì "nòng súng bay lơ lửng"
- `Scripts/Gameplay/Cannon/CannonAimMirror.cs` (file mới)
- `Editor/VehicleDefinitionBuilder.cs` + `LunaSmashdown/Prefabs/SmashFest/Slingshot.prefab` (`885d4327` — dựng xe đứng thẳng trên nền)

→ **Không lấy 3 mục trên.** Falcon không sửa gì trong `Cameras/` và `Cannon/`, nên bản Falcon = bản merge-base `2d7fa643`.

Lưu ý đánh đổi: `main` gộp chung trong đó một sửa lỗi thật — trước đây cả chiếc xe thừa hưởng góc nghiêng 31.44° làm bánh sau lún xuyên sàn. Bỏ qua bản `main` thì lỗi đó không xuất hiện (vì không dựng model xe), nhưng nếu sau này quyết định hiện cả xe thì phải lấy lại `VehicleMount.cs` của `main`.

Các file Cannon khác `main` sửa **thì vẫn nên lấy** vì là sửa lỗi/hiệu năng, không liên quan hiển thị: `CannonBallisticAimMath.cs`, `CannonShotPresenter.cs`, `GridKnockdownCannonFireController.cs`, `GridKnockdownCannonProjectile.cs`, `ProjectilePool.cs`.

### 4.3 Thứ tự đề nghị

1. Clone `main` sạch, commit ngay 29 file `.meta` của Audio (mục 1.2).
2. Cherry-pick / copy **map JSON + prefab** trước (không conflict).
3. Copy **texture** (không conflict).
4. Copy **4 file code + 2 file editor** (không conflict).
5. Merge `MapConfig.asset` + `MapProgressionConfig.asset` bằng tay, nhân tiện **sửa luôn typo id `"2"`**.
6. Dựng lại object `Level Scenery` trong `Gameplay.unity` bản `main` — đừng lấy YAML của Falcon.
7. Chạy `Tools/Smashdown/Bake Map Prefabs` cho map Mission 1/2.
8. Áp `9061711d` (xoá `*_Meshes.asset`) sau cùng, khi đã chắc map chạy.

### 4.4 Việc còn dở của Falcon (chưa xong khi rời nhánh)

- Mission BG Setting: lưu transform backdrop theo từng mission/level. Code có nhưng **chưa nghiệm thu** — thông số gốc của object `Backdrop` (y = −4.5) từng bị ghi sai thành y = 0, làm mission chưa chỉnh bị kéo cả dải lên.
- Mission 3 chưa gán BG (`BG_Universe_01`) và floor (`Floor_Space_01`, tiling 8) trong Mission Editor.
- `BG_Beach_01` cần vẽ lại ở 2000×1670, cụm công trình đặt ở **26–40%** và **60–74%** bề ngang (theo khung camera an toàn), và bù lại các phương tiện bị mất.
- `Docs/_to_delete/` (~120MB) chờ xoá.

### 4.5 Khung camera an toàn (số đo, dùng khi vẽ BG)

FOV dọc 22.5°, camera tại `(0, 1.01, −8.74)` chúi xuống 1.282°, mặt phẳng backdrop z = 19.43 → khoảng cách 28.17 → nhìn thấy **11.21 × 6.30** đơn vị thế giới của sprite 20 × 16.7.
→ Chỉ **34–66% bề ngang** và **28–95% chiều cao tính từ đỉnh** là lọt khung. Mọi thứ ngoài dải đó người chơi không thấy.


---

## PHẦN 5 — BUG GHI NHẬN KHI TEST TRÊN `Falcon/Clone_Alvin` (2026-08-31)

1. **Không xoay được khi mật độ khối đang rơi cao.** `SpinOnAxis` bản `main` chỉ quét-mang
   các body kinematic; khối đã Activate (dynamic) bị loại khỏi phép xoay. Nhiều khối rơi
   cùng lúc thì phần lớn map không còn được mang theo. Hướng sửa đề xuất: mang theo cả
   body dynamic đang ngủ/đứng yên, hoặc chặn xoay có chủ đích khi structure đang sập
   (kèm phản hồi UI) thay vì im lặng.
2. **Xoay đang áp vật lý vào block.** Cơ chế MovePosition sinh vận tốc bề mặt thật (chủ ý,
   để khối rời được ma sát kéo theo) nhưng tác dụng phụ là khối bị xô/đẩy khi kéo xoay.
   Đã vá phần tự vỡ (bỏ qua damage khi cả hai bên kinematic — BreakableBlock/BreakableWall/
   KnockdownBlock); phần xô đẩy vẫn còn, cần cân nhắc kẹp vận tốc hoặc tách layer khi xoay.
3. **Súng lỗi UI/UX.** Sàn mới của `main` (z=20, scale 10) phủ cả khu vực súng nên không thể
   giấu thân xe bằng cách hạ Y — hạ xuống là xe lún vào sàn. Đang xử lý bằng cách giữ Y=0 và
   kéo súng lại gần camera (Slingshot z: −4.33 → −5) để mép khung hình tự cắt phần thân/bánh.
   Cần chỉnh mắt lại sau; số đo khung: mép dưới màn hình 9:16 chạm sàn ở z ≈ −4.2.

Trạng thái bisect cùng ngày: `KnockdownLayoutMapAuthoring.cs` + `MapPrefabBaker.cs` đang tạm
để bản `main` (bản Falcon cất ở `_patch_backup/`) trong lúc khoanh vùng lỗi xoay sau khi bắn.
Đừng bake map prefab trong lúc này — bake bằng bản `main` sẽ sinh lại mesh nặng (~130MB).
