# Smash — sổ ý tưởng

Nơi ghi lại ý tưởng chốt trong lúc làm việc khác, để không rơi mất.
Mỗi mục: ngày, ý tưởng, và những gì đã biết chắc về nó.

---

## 2026-08-31 — Background vũ trụ

Một chủ đề background mới: **vũ trụ / không gian**.

Chưa quyết mission nào dùng. Ghi lại ở đây trước, quyết sau.

Những gì hệ thống hiện tại đã sẵn sàng cho nó:

- Scenery đã tách theo mission và theo từng level (`MapInfo.background`,
  `MapInfo.floorTexture`), chỉnh trong **Tools → Smashdown → Mission Editor**.
- Thêm một BG mới chỉ là thả file PNG vào `Assets/GameJam/Textures`, đặt tên
  bắt đầu bằng `BG` (ví dụ `BG_Space_01`) để lọt bộ lọc của picker, và đặt
  Texture Type = Sprite.
- Sàn đặt tên bắt đầu bằng `Floor` (ví dụ `Floor_Moon_01`), là PNG, không cần
  tạo material.
- `LevelScenery` tự co giãn tấm nền theo cỡ ảnh, nên ảnh vũ trụ **không cần**
  vẽ đúng 2000×1670 như `BG_New_02`. Nhưng nếu khác tỷ lệ khung thì ảnh sẽ bị
  kéo nhẹ theo chiều dọc — vẽ đúng tỷ lệ 2000×1670 là sạch nhất.

Đã giải quyết: khung an toàn của camera, giá trị độ sáng cho nền và sàn, và
cách đặt tiling — tất cả nằm trong `SMASH_Background_and_Floor_Spec.txt`.

Câu chưa trả lời, cần chốt trước khi vẽ:

- Vũ trụ thì "sàn" là gì — mặt trăng, tiểu hành tinh, sàn trạm không gian?
- Trọng lực có đổi không? Nếu nhẹ hơn thì khối rơi chậm, mà `FallBreakZone`
  chỉ phá khối khi va chạm nhanh hơn 1.5 m/s — trọng lực thấp có thể khiến
  công trình đổ mà không vỡ, tức là không tính vào phần trăm phá huỷ.
  Đây là thứ phải thử trước khi cam kết chủ đề này.
- Chất liệu vẫn chỉ gạch / kính / bê tông, hay cần chất liệu mới?
