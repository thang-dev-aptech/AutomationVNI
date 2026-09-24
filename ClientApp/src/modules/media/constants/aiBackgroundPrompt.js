/**
 * Meta-prompt tạo ẢNH NỀN sạch (không chữ/logo) để dùng làm Template ghép chữ bằng code
 * (RichTemplateRenderService) — khác CLAUDE_TEMPLATE_PROMPT (prompt-templates module) vốn sinh
 * cả text lẫn ảnh có chữ/CTA/logo bake sẵn. Đoạn này để copy dán vào AI ngoài (Claude/ChatGPT...),
 * không gọi API nào từ trong app — biến {{...}} được thay bằng renderBackgroundPrompt() trước khi
 * hiển thị/copy.
 */
export const BACKGROUND_IMAGE_PROMPT_TEMPLATE = `Bạn là chuyên gia prompt engineering cho AI tạo ảnh (Midjourney / DALL·E / Gemini Image...).
Nhiệm vụ: viết 1 PROMPT ẢNH (tiếng Anh) để sinh ẢNH NỀN sạch cho banner mạng xã hội — một hệ
thống khác sẽ tự ghép chữ (tiêu đề, mô tả, CTA) và logo ĐÈ LÊN ảnh này bằng code sau đó.

## Bối cảnh
- Chủ đề / ý tưởng: {{title}}
- Danh mục: {{category}}
- Thương hiệu: {{brand}}
- Màu thương hiệu: {{brandColors}}
- Giọng điệu: {{tone}}

## Yêu cầu bắt buộc cho ảnh
- TUYỆT ĐỐI không có chữ, số, logo, watermark, icon giao diện nào trong ảnh — chỉ là ảnh
  chụp/minh hoạ thuần tuý, không có bất kỳ đồ hoạ chữ/logo nào được vẽ sẵn.
- Bố cục nên có ít nhất 1 vùng (mảng trên, mảng dưới, hoặc 1 bên) "êm" hơn phần còn lại — đủ
  thoáng để sau này phủ 1 dải màu bán trong suốt + chữ lớn lên trên mà không bị rối — nhưng
  tổng thể ảnh vẫn phải đầy đặn, có chi tiết thật, không được trống trơn/phẳng lì.
- Màu sắc/ánh sáng nên gợi đúng tông màu thương hiệu ở trên một cách tự nhiên (như color
  grading ảnh thật), không vẽ thành mảng màu phẳng.
- Chất lượng: ảnh thật/nét cao (hoặc minh hoạ sạch nếu phù hợp chủ đề), 4K, không watermark,
  không chữ/ký tự lộn xộn xuất hiện trong ảnh, không tay/mặt bị méo.

## Output
Chỉ trả về đúng 1 đoạn prompt tiếng Anh (120-180 từ), không thêm giải thích, không markdown,
không tiêu đề.`

/** Thay {{var}} trong template bằng giá trị thật — thiếu key nào thì thay rỗng, không throw. */
export function renderBackgroundPrompt(values = {}) {
  return BACKGROUND_IMAGE_PROMPT_TEMPLATE.replace(
    /\{\{\s*(\w+)\s*\}\}/g,
    (_, key) => values[key] ?? '',
  )
}
