/**
 * Cờ bật/tắt tính năng ở giao diện. Chỉ ẩn lối vào trên UI — API backend vẫn giữ nguyên.
 * Đổi giá trị rồi build lại frontend để hiện/ẩn.
 */
export const FEATURES = {
  // Lựa chọn "Sinh toàn bộ bằng AI" ở trang tạo bài (/posts/create) và Tạo hàng loạt (/bulk).
  // Hiện lại theo yêu cầu người dùng 2026-10-03.
  aiFullImage: true,
  // Lựa chọn "AI sinh text, ghép vào ảnh mẫu" ở cả 2 trang trên. Ẩn theo yêu cầu người dùng 2026-10-03.
  aiTemplate: false,
  // Menu và trang "Tạo từ chứng chỉ" (/bulk-chung-chi). Ẩn theo yêu cầu người dùng 2026-10-03.
  chungChiBulk: false,
}
