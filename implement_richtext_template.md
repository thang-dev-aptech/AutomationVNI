# Tổng hợp tiến độ chức năng RichText Template & AI Layout Analysis

Dưới đây là bảng tổng hợp chi tiết những công việc đã được hoàn thành và những việc cần thực hiện tiếp theo để hoàn thiện tính năng "Tạo ảnh bài viết bằng Template tĩnh đè chữ" (thay vì sinh ảnh bằng DALL-E).

## 1. Những phần đã hoàn thiện

### A. Cấu trúc dữ liệu & Phân tích Layout (AI)
- **Thêm liên kết PageContext - MediaFolder**: Đã map trường `PageContextId` vào `MediaFolderModel`. Điều này cho phép một thư mục ảnh trong kho được gắn trực tiếp với một Fanpage (đóng vai trò là thư mục chứa các Template của page đó).
- **Giao diện cấu hình Thư mục**: Đã bổ sung trường chọn PageContext trong `MediaFolderFormModal.jsx`.
- **Nút "Quét Vùng An Toàn"**: Bổ sung nút ✨ quét layout tự động bằng AI tại `MediaPage.jsx` khi người dùng chọn một thư mục.
- **Service Phân tích Layout**: Đã thêm hàm `AnalyzeLayoutFolderAsync` trong `MediaIntelligenceService.cs`. 
  - Hàm này duyệt tất cả ảnh trong thư mục.
  - Sử dụng AI (OpenAI GPT-4o-mini Vision) để phân tích bức ảnh và trả về `safeTextRegion` (tọa độ `x, y, width, height`) - vùng an toàn không đè lên chi tiết quan trọng để có thể viết chữ vào đó.
  - Thông tin này được lưu dưới dạng JSON vào trường `Tags` của `MediaAssetModel`.
- **API Endpoint**: Đã mở API `POST /api/MediaAsset/analyze-layout-folder/{folderId}` tại `MediaAssetController`.

### B. Service Render Chữ (RichTextKit)
- **RichTemplateRenderService**: Đã xây dựng hoàn chỉnh service dùng SkiaSharp và `Topten.RichTextKit` để render text lên ảnh.
- **Logic Vẽ Chữ**: Hỗ trợ đọc tọa độ `safeTextRegion` từ ảnh đầu vào để vẽ chữ gọn gàng trong vùng an toàn đó. Hỗ trợ gradient, đổ bóng chữ, và font chữ tùy chỉnh.

## 2. Phần 2 — Đấu nối luồng "Tạo Bài Viết" (Đã xong 2026-08-11)

Khác với plan gốc bên dưới (giữ lại để tham chiếu lịch sử), bản implement thật đổi 2 điểm sau khi
thảo luận với người dùng:
- Thay vì menu 4 lựa chọn nhét trong form, dùng **modal 2 lựa chọn** (Full AI / Template) hiện
  ngay khi vào `/posts/create` (và inline picker tương đương ở `/bulk` — Bulk cũng được áp dụng).
- Khi page chưa có `MediaFolder` gắn `PageContext` (hoặc folder rỗng) → **báo lỗi rõ ràng
  (`PostStatus.NeedFix` + `GenerationError`), KHÔNG tự fallback** sang sinh ảnh AI.

### A. Model & Enum
- [x] `GenerationFlow.Template = 6` (`PostEnums.cs`).
- [x] `MediaFolder` gắn **trực tiếp với page** qua `SocialChannelId` (không qua `PageContextId`
  nữa) — không phải page nào cũng setup PageContext, nhưng page nào cũng có SocialChannelId.
  Đổi lại đúng như bản gốc trước đó (migration `AddMediaFolderSocialChannelId` 2026-08-07) —
  migration `PageContextId` (2026-08-11) là bước đi lệch, đã revert qua migration
  `RenameMediaFolderPageContextToSocialChannel` (giữ nguyên dữ liệu 2 folder đã gắn page).
  `GenerateFromTemplateAsync` tìm folder theo `f.SocialChannelId == post.SocialChannelId` trực
  tiếp, không cần tra PageContext trước nữa.
- [x] `AiTextGenerationResult.BannerBullets` — AI text-gen sinh kèm 3-4 bullet ngắn (system prompt
  `OpenAiCompatibleTextGenerationService`, mock `MockTextGenerator`), lưu vào `Post.ExtraJson.textGeneration.bannerBullets`.
- [x] `ImageOverlayRequest.OrgLine` / `ContactLine` — footer 2 dòng (tên page + hotline/website/hashtag từ `PageContext`).
- [x] `AnalyzeLayoutForAssetAsync` (Quét Vùng An Toàn) mở rộng trả thêm `layoutStyle`
  (`TopBottomSplit`/`FreeText`) do GPT-4o vision tự quyết theo bố cục ảnh — không thêm AI call mới,
  không đụng luồng Full AI.

### B. Giao diện Tạo bài viết
- [x] `GenerationFlowPicker.jsx` (dùng chung đơn + bulk) — 2 thẻ: "🎨 Sinh toàn bộ bằng AI" /
  "🖼️ AI sinh text, ghép vào ảnh mẫu".
- [x] `PostCreatePage.jsx` — modal chọn phương pháp trước khi hiện `PostCreateForm`.
- [x] `PostCreateForm.jsx` nhận prop `flow`; `flow=template` ẩn khối RAG, submit `generationFlow=6`.
- [x] `BulkCreatePage.jsx` — `GenerationFlowPicker` inline trong "Tuỳ chọn chung"; ẩn khối RAG khi Template.
- [x] Badge "📐 Đã quét Layout" trên `MediaAssetCard.jsx` — hiện khi `Tags` có `safeTextRegion`.

### C. Backend: Pipeline (`GenerationJobPipelineService.cs`)
- [x] `GenerateForPostAsync` — nhánh `GenerationFlow.Template` gọi `GenerateFromTemplateAsync`:
  tìm `MediaFolder` theo `PageContextId` của page → chọn ngẫu nhiên 1 ảnh còn lại trong folder làm
  Cover (`ReplaceCoverAsync`) → queue + process job `ImageOverlay` có sẵn (`QueueImageRenderAsync`/
  `ProcessImageOverlayAsync`, không tạo job type mới).
  - Không có folder/folder rỗng → set `PostStatus.NeedFix` + `GenerationError` rồi throw
    `InvalidOperationException` (khớp cơ chế báo lỗi có sẵn ở `PostController.CreateAndGenerate` và
    `PostGenerationWorker`).
- [x] `ProcessImageOverlayAsync` ưu tiên đọc `BannerCopy` (Subheadline/Bullets do AI sinh, qua
  `ExtractBannerCopy`) trước khi fallback về `ExtractTemplateText` (regex cũ); build `OrgLine`/`ContactLine`
  từ `PageContext.BrandName`/`Hotline`/`Website`/`DefaultHashtags`.

### D. Backend: `RichTemplateRenderService.cs` — viết lại để khớp ảnh mẫu thương hiệu
So với bản trước (Phần 1), bản này sửa các lỗi/thiếu sót đã đối chiếu với ảnh mẫu thật:
- [x] Nạp font thật vào RichTextKit (`AppFontMapper` override `FontMapper.TypefaceFromStyle`, load
  `Resources/Inter.ttf` + `Resources/NotoColorEmoji.ttf`) — trước đây `FontMapper.Default = new FontMapper()`
  không đăng ký gì, luôn fallback font hệ thống.
- [x] Tách run emoji riêng (`AddRichText` — segment theo grapheme cluster, phát hiện emoji theo
  code point range) vì RichTextKit không tự fallback font theo glyph.
- [x] Vẽ logo (`LogoStorageKey`) — trước đây field có nhưng không dùng.
- [x] Overlay nền navy bán trong suốt thay cho gradient trắng; chữ trắng/soft-white thay vì đen.
- [x] Vẽ `Subheadline` (trước đây có field nhưng bỏ qua).
- [x] Khung bo góc nền navy sau bullet (`DrawBulletBox`/box trong `DrawFreeTextLayout`).
- [x] Footer 2 dòng (`OrgLine` + `ContactLine`) thay vì 1 dòng `CtaText`.
- [x] `TopBottomSplit` (căn giữa) và `FreeText` (căn trái theo `safeTextRegion`) dùng chung style
  logo/footer/font, khác nhau ở vị trí headline/bullet — chọn theo `layoutStyle` AI-vision đã quét.

---

## 3. Kế hoạch gốc (tham chiếu lịch sử — đã thực hiện khác đi, xem mục 2)

### B. Giao diện Tạo bài viết (`PostCreateForm.jsx`) — plan gốc, KHÔNG dùng
- Thay đổi checkbox "Dùng ảnh kho" thành một **Menu chọn phương pháp tạo ảnh**:
  1. Sinh ảnh AI (DALL-E) - mặc định
  2. Dùng Template của Page (sẽ truyền `GenerationFlow.Template`)
  3. Lấy ảnh kho ngẫu nhiên (RAG)
  4. Không dùng ảnh (Text-only)

### C. Backend — plan gốc, KHÔNG dùng
- Fallback: Nếu không tìm thấy Folder hoặc Folder không có ảnh, tự động chuyển về luồng sinh ảnh AI
  (DALL-E) để bài viết không bị lỗi. **Đã đổi quyết định**: báo lỗi rõ ràng, không tự fallback (xem mục 2C).

---
*(Tài liệu này đóng vai trò checkpoint để đối chiếu. Phần 1 + Phần 2 đã xong; còn lại: badge "Đã quét Layout" (optional) và test runtime end-to-end thật với server chạy).*
