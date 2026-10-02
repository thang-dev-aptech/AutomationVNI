/**
 * Cờ bật/tắt tính năng ở giao diện. Chỉ ẩn lối vào trên UI — API backend vẫn giữ nguyên.
 * Đặt lại `true` để hiện lại.
 */
export const FEATURES = {
  // Tạm ẩn theo yêu cầu người dùng 2026-10-02: lựa chọn "Sinh toàn bộ bằng AI" khi tạo bài.
  aiFullImage: false,
  // Tạm ẩn theo yêu cầu người dùng 2026-10-02: trang "Tạo hàng loạt" (/bulk).
  bulkCreate: false,
}
