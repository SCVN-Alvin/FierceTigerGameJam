# Demolition Contracts — Kế hoạch kỹ thuật (24/08/2026)

Tài liệu này đối chiếu **Game Feel v0.1** với code hiện tại trong `FierceTigerGameJam`, đề xuất cách sửa `KnockdownLayoutMapAuthoring.BuildMap()` để chỉ merge wall khi block có thuộc tính `wall`, tóm tắt cách `luna_smashdown` làm destroy / shatter / debris, và đưa ra thứ tự ưu tiên tối ưu cho bản Android.

Tất cả nhận xét đều dựa trên code đọc trực tiếp (commit `217b28a SFX`, scene `Gameplay.unity` đang có thay đổi chưa commit). Những chỗ ghi *(cần kiểm tra trong scene)* là thứ chỉ xác nhận được khi mở Unity, vì wiring nằm trong scene/prefab chứ không nằm trong script.

---

## 1. Đối chiếu Game Feel v0.1 với code hiện tại

### 1.1 Vòng lặp Scan → Rotate → Select → Fire → Read → Assess

| Moment | Spec v0.1 | Trạng thái | Ghi chú theo code |
|---|---|---|---|
| Rotate | Swipe → yaw, 180° trong ~1.2 s, có damping / ease-out khi thả | **Một phần** | `CannonInputShooter` → `StructureRotateController.RotateFromScreenDelta` chỉ lấy **dấu** của delta X rồi set tốc độ cố định `dragRotationSpeed = 90°/s`; thả tay là dừng ngay. Không có ease-out, không map theo quãng swipe. `SpinOnAxis` xoay quanh `Structure Center` — đúng "building rotates, camera fixed". |
| Camera fixed | Camera & cannon cố định | **Đạt** | Không có code orbit camera; `GameJamCameraSizeController` chỉ fit ortho size. |
| Select ammo | Stone / Explosive, mỗi loại cho kết quả khác nhau | **Đạt về data** | `BulletDefinition` author damage theo `materialId` (`blockDamage` / `wallDamage` / `splashShare`). Chưa có khái niệm "Stone chỉ **đẩy**": xem mâu thuẫn ở §1.5. |
| Fire | Counter giảm ngay khi bắn, muzzle feedback ngay khi tap | **Đạt** | `GridKnockdownCannonFireController.Fire()` gọi `bulletInventory.TrySpend` **trước** khi Instantiate; `CannonShotPresenter.PlayShot()` chạy animator `Cannon_Shot` + smoke ngay tại frame bắn. |
| Read | Phản ứng theo vật liệu | **Đạt 3/4** | Brick / Glass / Concrete có HP, ngưỡng va chạm, prefab `_Shattered` riêng (`BlockPrefabBuilder.Specs`). **Steel chưa tồn tại** ở bất kỳ đâu (không spec, không prefab, không "crack state"). |
| Assess | Meter cập nhật sau khi cluster settle, ≤ 0.15 s; ngưỡng 70 % | **Một phần** | `LevelProgressTracker.sampleInterval = 0.25 s` (spec ≤ 0.15). Điểm tính theo structural unit, wall tính bằng `CellCount`, `ShatteredBlock` không tính — đúng spec. Ngưỡng mặc định `RequiredClearPercent = 0.8` (spec 70 %) → chỉnh trong `MapProgressionConfig`. |

### 1.2 Shot feedback stack (mục 3 của doc)

| Lớp | Spec | Trạng thái |
|---|---|---|
| Tap: recoil, muzzle flash, smoke | **Đạt** — animator + `ParticleSystem` trong `CannonShotPresenter` | |
| Tap: click-to-boom audio, haptic tick | **Thiếu** — không có `AudioSource`/`Handheld.Vibrate` nào trong `Assets/GameJam`. Commit `SFX` mới chỉ import gói UI SFX (CelerisLab), chưa có wiring. | |
| Flight: projectile trail | **Thiếu** — không có `TrailRenderer`/`LineRenderer`. | |
| Impact: hit sound theo vật liệu | **Thiếu** | |
| Impact: spark / dust / shard burst | **Có slot, chưa chắc có asset** — `BreakableBlock.breakEffectPrefab` là optional *(cần kiểm tra prefab block đã gán chưa)*. Shard burst = `ShatteredBlock` **đạt**. | |
| Impact: camera shake cục bộ | **Thiếu** — không có code shake. | |
| Aftershock: secondary collision, settle, progress | **Đạt** — `KnockdownBlock.OnCollisionEnter` cascade, `BreakableBlock.OnCollisionEnter` nhận damage khi block khác đập vào, `FallBreakZone` vỡ khi chạm sàn, `ShatteredBlock` tự shrink sau `lifetime = 2 s`. | |

### 1.3 Cannon & projectile (mục 3–4 của doc)

- **Flight time 0.30–0.40 s**: `projectileSpeed = 105 m/s` → 0.35 s tương đương ~37 m. Phụ thuộc khoảng cách cannon↔building trong scene *(cần đo)*. Với tốc độ này ball gần như bắn thẳng, đúng "near-straight with slight arc".
- **Projectile lifetime 0.25–0.60 s sau va chạm đầu tiên**: hiện `destroyOnImpact = true` → ball **biến mất ngay** ở va chạm đầu. Không có secondary collision từ chính viên đạn, chỉ có từ block. Muốn theo spec thì tắt `destroyOnImpact` và thêm timer sau hit (luna làm: 1 s chờ + 0.4 s fade).
- **Cooldown**: không có → đúng "near-zero". Nhưng vì `TryFireAtScreenPoint` không chặn spam, cần xem `Minimum shot spacing 0.15–0.25 s` khi thêm SFX để không chồng âm.
- **Explosive có AoE không?** (open question trong doc) — **đã có**: `impactRadius = 0.65`, `neighborImpulseMultiplier = 0.65`, damage lan = `blockDamage × splashShare × falloff`. Câu hỏi thực ra là tune `splashShare` per level, không phải làm mới.

### 1.4 Collapse choreography (mục 4 của doc)

Toàn bộ timing hiện tại là **physics-driven**, không có lớp choreography riêng. Glass vỡ tức thì khi HP về 0 (HP = 1, `MinimumImpactSpeed = 1.2`), brick cần ~1–2 hit (HP 3, `MaxDamagePerImpact = 3`), concrete HP 6. Không có:

- Steel "crack rồi mới break" (2 hit, có visual crack state).
- Sự kiện "cluster settled" để bắn haptic/SFX/meter theo cụm. `LevelRunController.SettleThenJudge` chỉ chạy **cuối run** (poll 0.25 s, `stillnessSeconds = 0.75`), không dùng được cho mid-run.
- Giới hạn debris (open question trong doc) — hiện không cap, mỗi block vỡ spawn 8–12 rigidbody con.

### 1.5 Mâu thuẫn thiết kế cần quyết định

`GridKnockdownCannonProjectile.TryAffect()` return sớm khi `damage <= 0` → đạn **không đẩy** block mà nó không làm hư được ("rock không cào được concrete thì cũng không xô được"). Doc lại yêu cầu *"Concrete: Stone only nudges the block"* và *"Steel: Stone causes tiny displacement"*. Hai hướng:

1. Giữ code (unlock feel): sửa doc.
2. Theo doc: tách `knock` khỏi `damage` — luôn `Knock()` với lực giảm theo hệ số vật liệu (ví dụ `nudgeScale` trong `MaterialDamage`), chỉ `ApplyDamage` khi damage > 0.

Cá nhân tôi nghiêng về (2) vì "shot phải đọc được ngay" là north star; một viên đạn dội ra mà không có phản ứng nào sẽ đọc như bug.

### 1.6 Checklist handoff (mục 7) — tóm tắt

| Owner | Đã có | Thiếu |
|---|---|---|
| Tech art | brick/glass/concrete prefab + `_Shattered` + wall panel; pivot (`Structure Center`) | Steel block + crack state + shattered; Glass_Wall thiếu texture (mượn material block) |
| Engineering | fixed camera, damage theo material, destruction score, retry/best % | yaw có damping, projectile lifetime sau hit, cluster-settle event, steel 2-hit, nudge-without-damage (§1.5) |
| VFX | cannon smoke | muzzle flash, trail, glass shard burst FX, brick dust, concrete chips, steel spark, settle dust |
| Audio | asset UI SFX (chưa wire) | 4 impact family, collapse rumble, steel cue, threshold sting, result stingers |
| UI | meter, ammo pick, gold result, retry/continue | FIRE button (hiện tap-to-fire), energy cost hiển thị |

Ngoài ra còn một **đường code legacy song song**: `SmashBlock`, `RuntimeGlassFracture`, `DemoLevelRuntimeBuilder`, `CannonProjectile`, `CannonFireController` (không phải bản `Grid*`). `RuntimeGlassFracture` tạo Mesh mới cho từng mảnh **lúc runtime** và `Destroy` sau 8 s — nếu path này không còn dùng trong `Gameplay.unity` thì nên xoá để khỏi ai vô tình bật lại trên mobile.

---

## 2. BuildMap: chỉ merge wall khi block có thuộc tính `wall`

### 2.1 Luồng hiện tại

`BuildMap()` → `TryPlaceBlock` từng block (validate type, rotation, reserve cell) → `BuildPlacedBlocks()`:

1. `CollectNamedWalls` — gom theo `block.wall.wall_id`; wall có < 2 block bị trả về loose.
2. `CollectDetectedWalls` — **tự động** quét mọi block chưa có wall_id, gom hình chữ nhật cùng type trong cùng layer (`WallGrouping.Find`), area ≥ `minimumWallCells (3)`.
3. `TryBuildWall` cho từng wall (panel mesh nếu là hình chữ nhật đơn vật liệu, không thì weld mesh); phần còn lại `SpawnPlacedBlock`.

Bước 2 chính là thứ anh muốn bỏ: block không khai `wall` vẫn bị gộp.

### 2.2 Một điểm quan trọng về JSON

`KnockdownMapDefinition` parse bằng `JsonUtility`. Với field kiểu class `[Serializable]` (`KnockdownMapWallRef wall`), Unity **luôn tạo instance** kể cả khi key `"wall"` không có trong JSON. Nghĩa là không phân biệt được "có key `wall` nhưng rỗng" với "không có key". Cách duy nhất đáng tin để biết block "có thuộc tính wall" là **`wall_id` khác rỗng** — đúng như property `KnockdownMapBlock.WallId` đang làm. Vì vậy rule nên phát biểu là:

> Block được merge **khi và chỉ khi** `wall.wall_id` không rỗng. Các block cùng `wall_id` thành một wall. Block không có `wall_id` là block đơn, không bao giờ bị gộp.

Nếu muốn giữ khả năng "auto-gộp" cho một số map cũ (`map_001`, `test_03`, `map_002` đều chưa có `wall_id`), làm nó thành tuỳ chọn tắt mặc định thay vì xoá hẳn.

### 2.3 Thay đổi đề xuất (nhỏ, không đụng phần build mesh)

**`KnockdownLayoutMapAuthoring.cs`**

```csharp
public enum WallGroupingMode
{
    /// Không bao giờ gộp: mỗi block là một body.
    None,
    /// Chỉ gộp block có wall_id trong JSON (mặc định mới).
    NamedOnly,
    /// Như cũ: wall_id + tự dò hình chữ nhật cùng type.
    NamedAndDetected,
}

[Header("Wall Grouping")]
[Tooltip("NamedOnly: chỉ block khai \"wall\": {\"wall_id\": ...} trong JSON mới được gộp; "
       + "block không khai là block đơn.")]
[SerializeField] private WallGroupingMode wallGrouping = WallGroupingMode.NamedOnly;

// Giữ lại để map cũ không mất setting; sẽ migrate trong OnValidate.
[SerializeField, HideInInspector] private bool groupBlocksIntoWalls = true;
```

```csharp
private int BuildPlacedBlocks(List<PlacedBlock> placed, Transform generatedRoot)
{
    if (wallGrouping == WallGroupingMode.None)
    {
        for (int i = 0; i < placed.Count; i++) SpawnPlacedBlock(placed[i], generatedRoot);
        return 0;
    }

    List<WallBuild> walls = new List<WallBuild>();
    List<PlacedBlock> loose = new List<PlacedBlock>();
    List<PlacedBlock> unassigned = new List<PlacedBlock>();

    CollectNamedWalls(placed, walls, loose, unassigned);

    if (wallGrouping == WallGroupingMode.NamedAndDetected)
        CollectDetectedWalls(unassigned, walls, loose);
    else
        loose.AddRange(unassigned);   // <-- block không có wall_id là block đơn

    // ...phần TryBuildWall / SpawnPlacedBlock giữ nguyên
}
```

Migration một lần trong `OnValidate()` (hoặc `ISerializationCallbackReceiver`): nếu `groupBlocksIntoWalls == false` → `wallGrouping = None`. `minimumWallCells` chỉ còn ý nghĩa với `NamedAndDetected`.

**`KnockdownLayoutMapAuthoringEditor.cs`** — đang đọc `groupBlocksIntoWalls` (bool) và `minimumWallCells` để vẽ preview và đếm wall (`DrawMapSummary`, `ResolveWalls`, `CountGrouping`). Cần:

- Đổi sang `PropertyField("wallGrouping")`; chỉ hiện `minimumWallCells` khi mode = `NamedAndDetected`.
- Trong `ResolveWalls`/`CountGrouping`: với `NamedOnly` chỉ tô màu cell theo `wall_id`, bỏ nhánh `WallGrouping.Find`. Không làm thì preview trong inspector sẽ nói dối (hiện wall mà runtime không gộp).

**Converter (Smash Builder / BuildingConverterWeb)** — vì merge giờ hoàn toàn do JSON quyết định, exporter phải ghi `"wall": {"wall_id": "..."}` cho từng wall đã merge (mỗi wall panel liên thông một id). Nếu exporter chưa ghi field này thì sau khi đổi mode, toàn bộ tường brick sẽ thành block đơn và block count tăng vọt — đây là thứ cần kiểm tra đầu tiên sau khi đổi.

### 2.4 Tác động cần lường trước

- `CollectNamedWalls` không kiểm tra wall có **liền kề** nhau hay không. Một `wall_id` gán cho hai cụm rời sẽ thành một body có mesh weld rời rạc (collider `BoxCollider` bao cả hai). Nên thêm warning: nếu các cell của một wall không liên thông (flood-fill 6-neighbour trên `GridPosition`) thì tách thành nhiều wall hoặc log lỗi.
- `IsRectangle` (điều kiện dùng panel mesh) yêu cầu đơn vật liệu, 1 layer, đầy hình chữ nhật. Wall khai bằng `wall_id` nhưng có lỗ → rơi về `BuildWeldedMesh` — tốn vertex như block rời, nhưng vẫn giảm rigidbody. Nên hiển thị trong log build: bao nhiêu wall dùng panel, bao nhiêu weld.
- `LevelProgressTracker` đếm `wall.CellCount` nên tổng điểm không đổi dù có gộp hay không — không cần sửa.
- Test: `map_003_wall_groups.json` (có các wall_id như `front_concrete`, `front_brick`, `deep_pillar` — cái cuối xuyên 2 layer) là case mẫu; thêm một map chỉ có glass không `wall_id` để xác nhận glass không bị gộp nữa.

---

## 3. luna_smashdown làm destroy / shattered / flying debris như thế nào

Path: `luna_smashdown/unity_project/luna_smashdown/Assets/Supercent/SmashDown/Main/Scripts/`

### 3.1 Vòng đời block (`BlockPhysicsController`)

- Chỉ có 2 state: `Prepared` (kinematic, `useGravity=false`, `interpolation=None`, `sleepThreshold=0.005`) và `Simulating`. Comment trong code nói rõ: để non-kinematic + tắt gravity thì contact reaction làm block "bay lên trời", nên dùng kinematic.
- **Không có HP.** Block không "vỡ khi bị bắn"; nó bị đẩy, rồi **vỡ khi chạm sàn** hoặc rơi khỏi bàn. Jam/character block là ngoại lệ (vỡ khi projectile chạm).
- Hit detection nằm ở **projectile** (`ProjectileMover.OnCollisionEnter` → `ReleaseForProjectileImpact`), block chỉ nhận. Impulse = `speed × 0.85`, cap 42; lần chạm đầu (đang kinematic) ratio 1.0, block đang simulate chỉ 0.2. Sau impulse còn **clamp vận tốc** (ngang 8.5, dọc 1.35) để block không bay quá xa — đây là thứ làm collapse "đọc được".
- Khi released: `collisionDetectionMode = Discrete`, `interpolation = None`, `solverIterations = 6`, đổi `PhysicMaterial` sang bộ "released" (friction 0.42/0.5).

### 3.2 Wake / support cascade (`BlockStackWakeController`)

- Không dùng grid, không raycast: giữ mảng block **sort theo world Y**, "block ở trên" = quét tiếp từ index+1. Điều kiện đỡ: khoảng cách dọc trong `[-0.08, 0.16]` và tỉ lệ chồng footprint ≥ 0.08.
- Một cú bắn chạy 3 pass: cluster quanh điểm chạm (gap ≤ 0.3 mỗi trục), cột phía sau theo hướng vận tốc, rồi cascade support depth 2. Tất cả chạy đồng bộ trong collision callback, không budget per-frame.
- Khối lượng hiệu dụng = base + 6 % tổng khối lượng đè lên, cap 1.35× — tường đáy nặng hơn tường đỉnh một chút.

### 3.3 Shattered / debris (`BlockBreakDebrisController`, `BlockDebrisFragment`, `BlockDebrisHierarchyBuilder`)

- Mỗi block prefab **mang sẵn `DebrisRoot`** chứa model debris (lấy từ `CannonKnockdownDebrisModelRegistry` theo type + size), fragment ở trạng thái kinematic + collider tắt + `SetActive(false)`. Khi vỡ: ẩn renderer intact, tắt collider intact, `DetachDebrisToWorld()` (SetParent null), bật fragment, `ActivatePhysics(inheritVelocity × 0.42)` + `ApplyBreakImpulse` (impulse 0.78, bias lên 0.32, jitter ngang 0.38, torque 0.3).
- **Không Instantiate lúc vỡ** — khác hẳn GameJam (`BreakableBlock.SpawnDebris` Instantiate prefab `_Shattered` mỗi lần).
- Fragment luôn dùng **BoxCollider** (MeshCollider bị gỡ trong `EnsureBoxCollider`), layer 9, `Physics.IgnoreLayerCollision(9, 9)` — debris không va nhau.
- Debris physic material chung (`SharedPhysicMaterialUtil`), drag 0.32/0.32; trong lúc settle, `DampGroundedMotion` nhân vận tốc ngang ×0.88/frame khi |vy| ≤ 0.35 để mảnh không trượt mãi.

### 3.4 Fade & cap (`BlockDebrisFadeSession`, `BlockDebrisActivePool`, `MaterialFadeUtility`)

- Settle 0.4 s → fade 0.6 s bằng **alpha** (đổi shader sang `Game/CannonKnockdown/AlphaFade`, material instance per fragment tạo 1 lần ở `Init`, tái dùng). Glass fade bằng float `_GlassFade`.
- **Cap cứng**: 12 session / 96 fragment đang sống; vượt thì session cũ nhất `ReassembleAndRelease()` ngay. GameJam hiện không có cap.
- Update-driven state machine, không coroutine, không alloc.

### 3.5 Projectile & VFX

- `ProjectilePool` (queue, size = số shot), `ContinuousDynamic` khi bay → `Discrete` ngay sau chạm đầu, `SphereCastNonAlloc` mỗi FixedUpdate để chống xuyên. Gravity tự tính (−3 m/s²). Sau hit: chờ 1 s → fade 0.4 s → trả pool.
- `CannonKnockdownVfxSpawner`: ring-buffer pool per effect (6, jam 12), pool size = cap số instance đồng thời. Không có camera shake; haptic hook có nhưng thân rỗng.

### 3.6 Nên mang gì sang GameJam

| Từ luna | Áp vào GameJam | Lý do |
|---|---|---|
| Debris nhúng sẵn trong prefab, bật/tắt thay vì Instantiate | `BreakableBlock.SpawnDebris` → pool `_Shattered` prefab (hoặc nhúng như luna) | Instantiate 8–12 rigidbody trong 1 frame là spike lớn nhất khi cascade |
| `BlockDebrisActivePool` cap 12/96 + oldest-first | Thêm `ShatteredBlockPool`/cap trong `ShatteredBlock` | Trả lời open question "max debris count" của doc |
| Layer debris ignore self | Thêm layer `Debris`, `IgnoreLayerCollision` | Debris-debris là phần contact tốn nhất mà không ai nhìn |
| Velocity clamp sau impulse | `KnockdownBlock.Knock` | Block bay đi 5–10 m làm mất "chain the player can understand" |
| `Discrete` + `interpolation None` cho block | `KnockdownBlock.ApplyRuntimeBodySettings` hiện set `Continuous` + `Interpolate` cho **mọi** block | Comment luna: CCD + Interpolate "melts frames" khi nhiều body release cùng lúc |
| `DampGroundedMotion` | `ShatteredBlock.Update` | Mảnh settle nhanh → shrink sớm hơn → ít body sống |
| Không nên bắt chước: alpha fade bằng material instance | Giữ scale-down như hiện tại | Opaque shader + Toony Colors, đổi shader để fade tốn variant và overdraw |

---

## 4. Tối ưu cho Android — làm phần nào trước

Thứ tự dưới đây xếp theo **tỉ lệ lợi ích / công sức**, dựa trên code hiện tại. Trước khi sửa, profile 1 lần trên máy thật với `test_03.json` (471 block) để có baseline; thư mục `ProfilerCaptures/` đang trống.

### P0 — Spike khi build map

1. **Map đang được build 2 lần.** `KnockdownLayoutMapAuthoring.OnEnable` subscribe `mapSelection.SelectionChanged → BuildMap()`, sau đó `GameFlowController.EnterAmmoPick` gọi `ClearMap()`, rồi `ConfirmAmmoPick` gọi `BuildMap()` lần nữa. Bỏ subscription trong authoring (flow đã sở hữu thời điểm build) *(cần kiểm tra `mapSelection` có được gán trên component trong scene không)*.
2. **`WallBlockPhysicsSetup.PrepareBlocks` `AddComponent` lúc runtime** cho mọi block: `BoxCollider` (nếu prefab chưa có), `Rigidbody`, `KnockdownBlock` — 3 AddComponent × 471 block trong 1 frame. Bake sẵn `Rigidbody` (kinematic) + `KnockdownBlock` + collider vào block prefab trong `BlockPrefabBuilder`; `PhysicsSetup` chỉ còn `ApplyAuthoring`. Cùng lý do, `BreakableWall.SpawnCell` gọi `PrepareBlock` cho từng block khi wall vỡ.
3. **Mesh leak**: `BuildPanelMesh` (`Instantiate(source)`) và `CombineToMesh` (`new Mesh`) tạo Mesh mới mỗi lần build; `ClearGeneratedBlocks` chỉ Destroy GameObject, không Destroy Mesh → mỗi lần retry rò thêm N mesh. Giữ `List<Mesh> builtMeshes` và Destroy trong `ClearMap`.
4. Spread build qua nhiều frame (coroutine, ~50 block/frame) sau khi UI transition bắt đầu, hoặc dùng prefab bake (§5).

### P1 — Physics trong lúc chơi

5. `KnockdownBlock.ApplyRuntimeBodySettings`: đổi `collisionDetectionMode` → `Discrete` và `interpolation` → `None` cho block; chỉ giữ `ContinuousDynamic` trên projectile (đã có). Đây là thay đổi 2 dòng, ảnh hưởng lớn nhất khi 50–100 body cùng thức.
6. `KnockdownBlock.ReleaseSupportedBlocksAbove`: mỗi lần Activate duyệt **toàn bộ** sibling, `GetComponent` từng cái, alloc `List` → O(N) per activation, O(N²) trong cascade. Thay bằng registry `Dictionary<Vector3Int, KnockdownBlock>` theo `GridPosition` do builder điền, tra cột phía trên trực tiếp.
7. `GridKnockdownCannonProjectile.KnockBlocks`: `Physics.OverlapSphere` + `new HashSet` mỗi shot → `OverlapSphereNonAlloc` với buffer tĩnh.
8. Debris: cap + pool + layer ignore-self (§3.6). `ShatteredBlock` hiện shrink từng chunk trong `Update` — ổn, nhưng thêm `DampGroundedMotion` để mảnh nằm yên sớm.
9. `BreakableBlock.OnCollisionEnter` và `BreakableWall.OnCollisionEnter` chạy trên mọi contact của mọi block; ngưỡng `minimumImpactSpeed` đã có, nên đủ. Chỉ lưu ý: **không** bật `allowCollisionCascade` + `Continuous` cùng lúc trên concrete (mass 2, cascade velocity 2.5) — hiện đang bật cả hai.

### P2 — Rendering

10. Quality `Mobile`: `shadows: 2`, `pixelLightCount: 2`. Với toon shading, tắt realtime shadow cho debris (`MeshRenderer.shadowCastingMode = Off` khi spawn) và cân nhắc hard shadow / 1 cascade. Kiểm tra SRP Batcher bật trong `Mobile_RPAsset` và Toony Colors shader tương thích SRP Batcher (không thì mỗi block là 1 draw call).
11. Wall panel mesh dùng UV tiling — đảm bảo texture brick ở `Repeat`, mip on, ASTC 6×6 cho Android.
12. `Application.targetFrameRate = 60` phải set tường minh (Android mặc định 30). Chưa thấy chỗ nào set.

### P3 — Vệ sinh

13. Xoá path legacy `SmashBlock` / `RuntimeGlassFracture` / `DemoLevelRuntimeBuilder` / `CannonProjectile` nếu không còn trong scene. `RuntimeGlassFracture` tạo Mesh runtime per shard + `Destroy(..., 8f)` — thứ không nên tồn tại trong build mobile.
14. `LevelProgressTracker.CountRemainingBlocks` mỗi 0.25 s đi qua children — rẻ, nhưng khi giảm xuống 0.15 s theo doc thì cân nhắc đếm event-driven (`BreakableBlock.Broken` đã có event; wall thì `BreakUp`).

---

## 5. Load map từ JSON vs. setup sẵn prefab

### 5.1 Chi phí thực sự nằm ở đâu

Parse JSON bằng `JsonUtility` cho 500 block là vài ms — **không phải nút thắt**. Chi phí là những gì `BuildMap` làm sau parse:

| Bước | JSON runtime (hiện tại) | Prefab bake sẵn |
|---|---|---|
| Validate + reserve cell | mỗi block | 0 (đã làm lúc bake) |
| `Instantiate` từng block | N lần | 1 lần `Instantiate(root)` (vẫn tạo N GameObject nhưng trong 1 call native, nhanh hơn đáng kể) |
| `AddComponent` Rigidbody/KnockdownBlock/Collider | 3N | 0 (đã serialize trong prefab) |
| `CombineMeshes` / `BuildPanelMesh` | mỗi wall, mỗi lần build | 0 (mesh là sub-asset của prefab) |
| `GetComponentsInChildren` (physics setup, subscribe walls, renderer bounds cho center) | 3–4 lần duyệt cây | 0–1 |
| Có thể load async | Không (đồng bộ) | Có (Addressables / `Resources.LoadAsync` → `InstantiateAsync` trên Unity 6) |
| Asset size | JSON vài chục KB | prefab + mesh vài trăm KB đến vài MB / map |
| Đổi tuning block prefab | tự động | tự động cho block (nested prefab) — **không** tự động cho wall mesh đã bake |
| Iteration cho designer | sửa JSON, Play | sửa JSON → bấm Bake → Play |

### 5.2 Đề xuất: hybrid — JSON là source of truth, prefab là cache

`BuildMap()` **đã** hỗ trợ chạy trong editor (`PrefabUtility.InstantiatePrefab` khi `!Application.isPlaying`, nút *Build Map* trong inspector). Chỉ cần thêm một bước:

1. Editor: `Tools > Smashdown > Bake Map Prefabs` — với mỗi `MapInfo`, `BuildMap()` trong editor, lưu `GeneratedLayoutBlocks` thành prefab `Assets/GameJam/Maps/Baked/<mapId>.prefab`, lưu các wall mesh làm sub-asset (giống cách `SaveChunkMeshes` đang làm cho debris). Ghi kèm `PlacedBlockCount` vào một component nhỏ trên root (`BakedMapInfo`) vì `LevelProgressTracker` cần con số này.
2. `MapInfo` thêm field `bakedPrefab` (hoặc AssetReference nếu dùng Addressables).
3. Runtime: `KnockdownLayoutMapAuthoring.BuildMap()` → nếu `bakedPrefab != null` thì `Instantiate` prefab dưới `structureRoot`, đọc `PlacedBlockCount` từ `BakedMapInfo`, gọi `SubscribeWalls` + `SetupStructureCenter` như cũ; nếu không có thì fallback build từ JSON (giữ nguyên code cũ cho dev iteration).
4. Bake trước khi build Android (có thể chạy trong pre-build hook để không quên).

Cách này giữ được đúng flow hiện tại (JSON, converter, inspector preview) mà bỏ được toàn bộ chi phí P0-2/3 và mở đường cho load async trong lúc màn ammo pick đang hiện — người chơi sẽ không bao giờ thấy frame hitch lúc bắt đầu run.

### 5.3 Khi nào **không** nên bake

- Map sinh ngẫu nhiên / user-generated → không có.
- Nếu block count mỗi map < ~150 và chỉ có 1–2 map, spike build có thể đã chấp nhận được sau P0-1, P0-2 — đo trước rồi quyết.

---

## 6. Thứ tự làm đề xuất

1. **BuildMap `NamedOnly`** (§2) + cập nhật inspector + kiểm tra exporter ghi `wall_id`. Nhỏ, không rủi ro, mở khoá việc tune block count.
2. **P0 physics-setup bake vào prefab + fix build 2 lần + mesh leak** (§4). Đo lại.
3. **Quyết định §1.5 (nudge without damage)** — ảnh hưởng cách viết `TryAffect` trước khi tune feel.
4. **Debris pool + cap + layer** (§3.6) và `Discrete/None` cho block.
5. **Feel pass**: rotation damping, projectile lifetime sau hit, sampleInterval 0.15, ngưỡng 70 %, rồi mới tới SFX/haptic/shake/trail (những thứ này là wiring, nên làm sau khi physics ổn để không tune hai lần).
6. **Steel** (material thứ 4) chỉ khi 3 material đầu đã pass checklist — đúng "prototype boundary" trong doc.
7. **Bake map prefab** (§5) khi có ≥ 3 map thật từ converter và đã có số đo.
