# GÓI PORT — mang thành quả Falcon sang main mới (sau đợt 43 commit của dev)

Tạo: 2026-09-01, từ nhánh `Falcon/UpdateMapData` (+ các fix trên `Falcon/Clone_Alvin`).
Thư mục này nằm ngoài git — đứng nhánh nào cũng dùng được.

## Có gì trong gói

### maps/  — 18 map SẴN SÀNG THẢ VÀO (đã xử lý theo chuẩn main mới)
- Đã GỠ TOÀN BỘ wall data (main mới xoá hệ thống wall — mỗi cell là 1 block độc lập)
- Đã ĐỔI TÊN + đổi id bên trong theo scheme mới: mission2_map1..9 (biển), mission3_map1..9 (vũ trụ)
- mission2_map1..3: GHI ĐÈ nội dung 3 file placeholder có sẵn trong Assets/GameJam/Maps/
  (giữ nguyên .meta của chúng — khỏi nối lại guid)
- mission2_map4..9 và mission3_map1..9: file mới, chép vào Assets/GameJam/Maps/ cho Unity tự sinh meta

### maps_goc_con_wall/ — 18 JSON gốc của Falcon, còn nguyên wall (chỉ để đối chiếu)

### textures/ — 8 ảnh BG + floor, kèm .meta gốc (giữ meta để guid khớp nếu sau này cần)
BG_Beach_01, BG_New_01, BG_New_09, BG_Universe_01, Floor_New_01, Floor_New_09, Floor_Sand_01, Floor_Space_01

### scenery_reference/ — code scenery của Falcon, CHỈ ĐỂ THAM KHẢO, đừng chép đè
Main mới đã đổi kiến trúc (MissionConfig.asset thay cho slot trong MissionPanelView),
nên LevelScenery/MissionEditor phải viết lại nhắm vào MissionConfig. Logic đáng giữ:
- LevelScenery.cs: đọc BG+floor theo mission, property block cho floor (_BaseMap/_BaseMap_ST),
  fit theo authored size, preview trong edit mode, khung camera trong Scene view
- MissionEditorWindow.cs: picker tự build bằng GenericMenu (ShowObjectPicker bị Unity 6 lờ filter),
  tự convert texture Default -> Sprite qua TextureImporter
- MissionPanelView.cs: 3 hàm tra cứu MissionOf/SlotOf/TryGetMissionScenery

## Thứ tự áp dụng trên nhánh mới

1. Chép 18 file trong maps/ vào Assets/GameJam/Maps/ (3 file đầu là ghi đè)
2. Chép 8 ảnh + meta trong textures/ vào Assets/GameJam/Textures/
3. Mở MissionConfig.asset: mission_2 giữ 3 id cũ + thêm mission2_map4..9;
   thêm mission_3 với mission3_map1..9
4. Thêm 18 entry vào MapConfig.asset (id + mapJson; mission2_map1..3 đã có sẵn entry thì thôi)
5. Bake prefab bằng baker MỚI của main (đừng dùng prefab cũ của Falcon — chúng chứa
   BreakableWall đã bị xoá, kéo qua là missing script)
6. CHƠI LẠI TỪNG MAP để cân bằng: bỏ wall nghĩa là mất HP gộp thân tường —
   map dễ sập hơn hẳn, requiredClearPercent 0.8 có thể phải chỉnh
7. Scenery per-mission: viết lại theo hướng gắn field background/floorTexture/floorTiling
   vào Mission trong MissionConfig.cs (dev chưa làm phần này — kiểm tra lại trước khi viết)

## KHÔNG đem qua nữa (dev đã làm xong theo cách khác)
- Vá xoay/tự vỡ: main mới xoay bằng CameraOrbit, không đụng vật lý
- Tối ưu mesh: main mới bỏ welded mesh toàn bộ
- Chỉnh cannon: đã có BarrelOnly + không lún sàn + bench so nòng
- Canh giữa map (KnockdownLayoutMapAuthoring): kiểm tra bản main mới trước, có thể đã ổn

## Số liệu khung camera an toàn (để vẽ/duyệt BG, đo trên scene cũ — đo lại nếu camera rig đổi)
FOV dọc 22.5°, backdrop z=19.43: chỉ 34–66% bề ngang và 28–95% chiều cao (từ đỉnh) của
sprite 20x16.7 là lọt khung. Cụm công trình đặt ở 26–40% và 60–74% bề ngang.
