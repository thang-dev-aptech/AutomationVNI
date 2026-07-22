# Sinh ảnh banner từ caption bằng LLM (prompt động, ảnh đa dạng)

Tài liệu ghi lại **vì sao** và **cách** đổi luồng sinh prompt ảnh, để ảnh mỗi lần tạo lại
có bố cục khác nhau nhưng vẫn giữ đúng nhận diện thương hiệu — thay cho việc render một
template prompt cố định (làm ảnh "na ná nhau").

> Liên quan: `dang_bai_tu_dong.md` (Luồng 1 – sinh ảnh + overlay), `coding/backend_guide.md`.

## 1. Vấn đề trước khi sửa

Người dùng phát hiện: dùng ChatGPT web (đưa logo + caption, bảo "từ caption viết prompt
rồi tạo ảnh") thì mỗi lần ra một bố cục mới, đẹp. Nhưng hệ thống tạo lại ảnh thì **ảnh na
ná nhau**.

Nguyên nhân (đối chiếu code cũ):

1. LLM text-gen đã sinh field `imagePrompt`, nhưng `BuildImagePromptAsync` **ưu tiên
   template `ImageBody`**: có template thì render rồi `return` luôn → prompt AI bị bỏ.
2. Template `ImageBody` quá ngắn (vd "Create a clean product social image…") → thiếu
   art-direction (phong cách, hình ảnh chính, icon, khung CTA…).
3. Tạo lại ảnh **không gọi lại LLM** — `ResolveImagePromptAsync` chỉ đọc lại prompt cũ đã
   lưu; `post.Content` cũng cố định → prompt y hệt.
4. Gemini `generationConfig` không có `temperature`/`seed` → prompt giống → ảnh giống.

## 2. Nguyên tắc thiết kế

**KHÓA nhận diện, MỞ bố cục.**

| Khóa cứng (không đổi) | Cho biến thiên (mỗi lần khác) |
|---|---|
| Logo top-left, hotline, website, bộ màu, các khối nội dung bắt buộc | Tư thế nhân vật, góc nền, cách sắp icon, vị trí khối chữ, tông sáng |

Phần khóa cứng đã có sẵn và giữ nguyên: `AppendBrandLock` + logo reference.

## 3. Kiến trúc mới (chỉ đổi nhánh dựng prompt ảnh)

```
Text job (giữ nguyên) → caption + imagePrompt ngắn + bannerCopy lưu Post
Image job:
  BuildImagePromptAsync
    1. styleGuide = render ImageBody template (nay là "gợi ý phong cách", KHÔNG return)
    2. avoidLayout = prompt của image-job Completed gần nhất của post (ép khác bố cục)
    3. NẾU LLM khả dụng:
         prompt = aiTextGenerationService.ComposeImagePromptAsync(
                    caption, styleGuide, brandCtx, bannerCopy, imagePromptHint, avoidLayout)
       NGƯỢC LẠI: fallback cũ (styleGuide đã render → imagePrompt → generic)
    4. prompt = AppendBrandLock(prompt, ctx, hasLogo)   ← GIỮ NGUYÊN (khóa cứng)
  → GenerateImageAssetAsync gửi Gemini (thêm temperature = 1.0)
```

Bước `ComposeImagePromptAsync` **chạy ngầm** trong image-job: không endpoint/UI mới, sinh
prompt xong mới ra ảnh.

## 4. File đã sửa

| File | Thay đổi |
|---|---|
| `Shared/Ai/AiTextGenerationDtos.cs` | Thêm `AiImagePromptRequest` (caption, brand, styleGuide, avoidLayout, hasLogo…) |
| `Shared/Ai/IAiTextGenerationService.cs` | Thêm `ComposeImagePromptAsync(...)` |
| `Shared/Ai/OpenAiCompatibleTextGenerationService.cs` | System prompt "art-director" + implement + `BuildImagePromptUser` (temperature 0.95) |
| `Modules/GenerationJob/GenerationJobPipelineService.cs` | `BuildImagePromptAsync`: template → style guide, gọi LLM ngầm; thêm `GetPreviousImagePromptAsync` |
| `Shared/Ai/GeminiImageGenerationService.cs` | `generationConfig` thêm `temperature = 1.0` |

Chỉ 1 implementer interface (`OpenAiCompatibleTextGenerationService`); DI ở `Program.cs`
không đổi.

## 5. Edge cases đã xử lý

- Không có API key / LLM lỗi → `ComposeImagePromptAsync` trả rỗng → fallback prompt tĩnh,
  không chặn sinh ảnh.
- Post đầu tiên (chưa có ảnh cũ) → `avoidLayout` null, bỏ ràng buộc "khác lần trước".
- Template rỗng → `styleGuide` null, LLM tự do sáng tác theo brand context.
- LLM lỡ bịa hotline/website → `AppendBrandLock` vẫn ép in đúng ở cuối.
- Tạo lại liên tục → luôn đọc prompt ảnh Completed mới nhất, tránh lặp dây chuyền.

## 6. Cách test runtime

Cần API key thật (OpenAI-compatible + Gemini) và server chạy:

```text
1. Tạo 1 post → chờ ra ảnh.
2. Bấm "tạo lại ảnh" 2–3 lần.
3. Kỳ vọng: 3 bố cục khác nhau; logo/hotline/website/màu giữ nguyên.
4. Log xác nhận nhánh mới: "image prompt composed by LLM ... variedFromPrev=True".
```

## 6b. Cấu hình model ảnh Gemini (quan trọng — đo thực tế)

Đo trực tiếp Gemini API (prompt banner thật, key trong user-secrets):

| Model | Thời gian | Ra ảnh? | Ghi chú |
|---|---|---|---|
| `gemini-2.5-flash-image` | ~8s | ✅ | Nhanh, ổn định; chữ/chi tiết kém hơn Pro |
| `gemini-3-pro-image` | ~70s (có lúc >90s) | ✅ nhưng chậm/thất thường | Dễ chạm timeout → rơi về ảnh mock |
| **`gemini-3-pro-image-preview`** | **~18s** | ✅ | **Pro chất lượng, nhanh gấp ~3.7×** bản non-preview → đang dùng |

- `temperature` trong `generationConfig` **được chấp nhận** (flash-image + temp=1.0 ra ảnh bình thường) — không phải nguyên nhân lỗi.
- **Bug đã sửa**: image HttpClient trước đây không set timeout → dùng mặc định 100s; `TimeoutSeconds` trong config là *dead config*. Đã wire lại ở `Program.cs` (lấy theo provider mặc định, min 30s) và nâng `TimeoutSeconds = 120`.
- Triệu chứng khi model ảnh quá chậm/treo: mỗi lần sinh ảnh fail → `GenerateImageAssetAsync` rơi về **ảnh mock (xám)** → người dùng tưởng "ảnh không được gen".

## 6c. Đa dạng bố cục — Creative Direction Injection

Trước đây `ImagePromptSystem` kê sẵn cấu trúc cứng (headline trên · chips trái · CTA bar dưới)
→ ảnh lần nào cũng generic, "nhìn là biết AI". Giải quyết bằng **bốc ngẫu nhiên hướng sáng tạo**:

- `Modules/GenerationJob/CreativeDirectionLibrary.cs`: 9 trục bốc ngẫu nhiên — **format/genre**
  (banner/poster/advertising/magazine...), **composition**, **content block colour** (đa dạng, có
  cả "không panel", không mặc định xanh), **focal point** (ép 1 điểm nhấn pop), **typography**,
  **script accent** (font viết tay — tùy chọn, ~1/3 là "không"), **visual style**, **mood**, **hero
  focus**. `BuildBrief(new Random())` → 1 brief độc nhất (~450k+ tổ hợp), mức "sáng tạo có kiểm soát"
  (chữ Việt luôn đọc tốt, lấp đầy khung bằng 1 cảnh tràn viền/split panel — không nền hoa văn/collage).
- `AiImagePromptRequest.CreativeBrief` mang brief sang bước compose.
- `ImagePromptSystem` viết lại thành **creative director**: hiện thực hóa brief, kèm anti-cliché
  (liệt kê thẳng 2 layout generic cần TRÁNH), vẫn khóa brand + chữ Việt đọc tốt.
- `BuildImagePromptAsync` bốc brief mỗi lần sinh/regenerate + log ra (grep "Creative brief:").

Kiểm chứng thực tế: 4 lần regenerate cùng chủ đề → 4 bố cục khác hẳn (headline-phải / layered 3D
cards / diagonal blob / cut-out + ribbon động). Không thêm API call (vẫn 1 lượt compose).

Lưu ý còn lại:
- Logo vẫn do AI vẽ → không đảm bảo 100% (đôi khi lệch). Muốn chuẩn tuyệt đối: ghép logo thật đè
  lên bằng `ImageOverlayService` (ImageSharp) — tách riêng, chưa làm.
- Đôi khi AI lặp 1 nhãn feature (vd 2 chip "Xử lý tình huống") — có thể siết prompt "exactly 4
  distinct labels" nếu cần.

## 7. Phase 2 (chưa làm) — Lưu prompt + sửa-rồi-tạo-lại

Hiện MediaAsset **không lưu** prompt sinh ảnh (Description bị AI-vision ghi đè — xem mục
"sửa ảnh ở Media"). Muốn cho người dùng **xem/sửa prompt rồi tạo lại đúng ảnh đó** cần:

- Thêm cột `Prompt` (nullable) vào `MediaAssetModel` + migration + cập nhật file database.
- Khi sinh ảnh: lưu prompt cuối (sau AppendBrandLock) vào cột này, KHÔNG để AI-vision ghi đè.
- Endpoint "regenerate" nhận `promptOverride`: có thì dùng thẳng, bỏ qua bước LLM/template.
- FE: ô sửa prompt riêng (tách khỏi Description/AltText).

Tách riêng vì đụng schema + FE, trong khi Phase 1 (file này) chỉ đụng backend logic.
