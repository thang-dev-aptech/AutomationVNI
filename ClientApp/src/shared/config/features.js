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
  /**
   * Kill switch tạm: giữ nút sinh ảnh AI trên UI nhưng disabled + cảnh báo.
   * Ảnh hưởng Full AI, Template, Tạo lại ảnh, tick banner AI khi duyệt tin.
   * Bật lại = true khi hạ tầng AI ảnh ổn định.
   * Nguồn: yêu cầu người dùng 2026-10-07.
   */
  aiImageGeneration: false,
}

/** Cảnh báo hiển thị trên mọi nút sinh ảnh AI đang tạm tắt. */
export const AI_IMAGE_DISABLED_HINT = 'Tính năng tạm thời tắt'

/** Các phương pháp tạo bài gọi pipeline sinh ảnh AI. */
export const AI_IMAGE_FLOWS = Object.freeze(['fullai', 'template'])

export function isAiImageGenerationEnabled() {
  return FEATURES.aiImageGeneration !== false
}

export function isAiImageFlow(flow) {
  return AI_IMAGE_FLOWS.includes(flow)
}
