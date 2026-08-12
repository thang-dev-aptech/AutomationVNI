# Tiến độ: Multi-image Template + Reels

Xem plan đầy đủ ở `REELS_PLAN.md`. File này ghi lại đã làm gì / còn thiếu gì / quyết định phát sinh ngoài plan gốc, cập nhật sau mỗi phần.

## Trạng thái tổng quan

- [x] Phần A — `MatchForPostAsync` lọc theo `folderId`
- [x] Phần B — Template chọn N ảnh
  - [x] B1. `ImageCount` trên `PostModel`
  - [x] B2. UI nhập số lượng
  - [x] B3. Chọn N ảnh bằng AI
  - [x] B4. Render N ảnh
  - [x] B5. Publish (không cần sửa — verify)
- [x] Phần C — Reels
  - [x] C1. `GenerationFlow.Reels` + `MediaRole.TemplateSource` + auto AI chọn khung hình
  - [x] C2. UI + endpoint chỉnh tay khung hình
  - [x] C3. `JobType.ReelsRender` + `SlideshowVideoRenderService`
  - [x] C4. Đóng gói FFmpeg static binary
  - [x] C5. `FileStorageOptions` cho `.mp4`
  - [x] C6. `PublishReelAsync` (3-phase upload)
  - [x] C7. Routing video vs ảnh khi publish
  - [x] C8. UI `GenerationFlowPicker` thêm "Reels"
- [x] Migration EF Core cho `ImageCount` (`AddPostImageCount`, đã áp vào DB dev)
- [x] Migration EF Core cho phần C — KHÔNG CẦN: xác nhận qua log khởi động thật ("No migrations were
  applied. The database is already up to date.") — `GenerationFlow`/`MediaRole`/`JobType` lưu int,
  thêm enum value không đổi schema.
- [x] Build backend + frontend: 0 lỗi. Khởi động app thật (`dotnet run`) qua hết DI resolution, EF
  migration check, mọi BackgroundService start bình thường — dừng do timeout kill giữa lúc Kestrel
  đang bind port (artifact của lệnh test, không phải lỗi app).

## Nhật ký

### 2026-08-12 — Khởi tạo

- Tạo branch `feature/reels-multi-image-template` từ `feature/richtextkit-upgrade`.
- Tạo `REELS_PLAN.md` + `REELS_PROGRESS.md`.

### 2026-08-12 — Phần A + B xong, build + migration OK

- **Phần A**: `MediaIntelligenceService.MatchForPostAsync` thêm tham số `Guid? folderId = null`, lọc `candidates` theo `FolderId` khi có giá trị. Sửa lại 1 caller cũ (`GenerationJobPipelineService.cs` nhánh RAG) để dùng `ct: ct` named argument (tham số mới chen vào giữa vị trí cũ của `ct`).
- **Phần B**: thêm `MediaRole.TemplateSource = 5` (dùng chung cho cả Template nhiều ảnh và Reels — ảnh NGUỒN đang chọn để render, khác `Attachment`/`Cover` là ảnh ĐÃ RENDER). Thêm `PostModel.ImageCount`, `CreatePostRequest.ImageCount`, nối qua `PostController.CreateAndGenerate` → `PostRepository.CreateAsync` (chỉ đường tạo bài 1-kênh đồng bộ; đường fan-out nhiều-kênh `CreateFanOutQueuedAsync` CHƯA nối — ngoài phạm vi v1).
  - `GenerateFromTemplateAsync`: `ImageCount` null/&lt;=1 → giữ nguyên hành vi cũ 100% (random 1 ảnh, `ReplaceCoverAsync`). `>=3` → `ChooseTemplateSourceImagesAsync` gọi `MatchForPostAsync` (AI), bù ngẫu nhiên nếu thiếu, lưu qua `PostMediaRepository.ReplaceTemplateSourcesAsync` (method mới — soft-delete hàng cũ, tạo hàng mới theo `SortOrder`).
  - `ProcessImageOverlayAsync`: đọc `TemplateSource` rows nếu có (nhiều ảnh) hoặc fallback `RequireCoverMediaAsync` (1 ảnh, hành vi cũ). Loop render từng ảnh nguồn bằng `RichTemplateRenderService` (qua `imageOverlayService.RenderAsync`, không đổi service này), ảnh đầu → `Cover`, ảnh 2..N → `Attachment`. Dọn `TemplateSource` sau khi render xong.
  - **B5 verify**: không sửa code — `FacebookPagePublishService.PublishAsync` đã tự route N ảnh (`mediaItems.Count > 1`) sang `PublishMultiPhotoFeedAsync` có sẵn từ trước.
- Migration `AddPostImageCount` (chỉ 1 cột mới `Posts.ImageCount INTEGER NULL`) — tạo + áp vào DB dev thành công.
- `dotnet build`: 0 error. `oxlint` trên file JSX sửa: chỉ 1 warning cũ có sẵn (không liên quan thay đổi).
- **Chưa test end-to-end thật** (chưa tạo post Template `ImageCount=4` qua UI thật + publish thử lên Facebook) — để làm ở bước verification cuối cùng sau khi xong cả Phần C, tránh phải bật/tắt server nhiều lần giữa chừng.

### 2026-08-12 — Phần C xong toàn bộ, build + smoke-test pass

- **C1+C3** (`GenerationJobPipelineService.cs`): `GenerateFromReelsAsync` tái dùng gần như 100% logic chọn ảnh của `GenerateFromTemplateAsync` (đã tách thành `ResolveFolderCandidatesAsync`/`EnsureEnoughTemplateCandidatesAsync`/`ChooseTemplateSourceImagesAsync` dùng chung). Khác Template ở chỗ: N mặc định lấy `ReelsOptions.DefaultFrameCount` (không phải 1), và sau khi `QueueImageRenderAsync`+`ProcessAsync` render N khung hình (tái dùng nguyên `ProcessImageOverlayAsync` của Phần B, không sửa gì thêm), chain tiếp `QueueReelsRenderAsync`+`ProcessAsync` để ghép video.
  - `ProcessReelsRenderAsync` (job mới `JobType.ReelsRender`): đọc N `PostMedia(Attachment/Cover)` vừa render → đọc bytes từ storage → `SlideshowVideoRenderService.RenderAsync` (FFmpeg) → lưu video qua `fileStorageService.SaveBytesAsync` → xoá N ảnh khung hình cũ (`PostMediaRepository.SoftDeleteAllForPostAsync`, method mới) → `ReplaceCoverAsync` set video làm Cover duy nhất. Post luôn kết thúc với ĐÚNG 1 `PostMedia` (video) để publish routing (C7) hoạt động đúng.
  - `ProcessImageOverlayAsync` (Phần B) sửa thêm 1 điều kiện: chỉ dọn `TemplateSource` sau render nếu KHÔNG PHẢI Reels — Reels giữ lại `TemplateSource` làm "khung hình đang dùng" để `ReelsFramePicker` (C2) đọc lại khi người dùng muốn chỉnh tay.
- **C2**: `PostController` thêm `PUT /api/Post/{id}/reels-frames` (ghi đè `TemplateSource`, không tự render) + `POST /api/Post/{id}/regenerate-reels-video` (render lại + ghép video, mẫu y hệt `RegenerateImage`). Frontend `ReelsFramePicker.jsx` tái dùng toàn bộ API có sẵn (`mediaFolderApi.filter` — thêm method mới, `mediaAssetApi.filter`, `postMediaApi.getByPost` — đều đã có sẵn từ BaseController pattern) — chỉ cần thêm 2 hook mutation (`useSetReelsFrames`, `useRegenerateReelsVideo`).
- **C4**: **Đã tải FFmpeg static build THẬT** (không phải giả định) từ johnvansickle.com (v7.0.2, amd64, ~80MB, statically linked, `--enable-gpl --enable-libx264`) vào `backend/Resources/ffmpeg/ffmpeg`, set `+x`. Verify: `dotnet build` copy đúng file + giữ quyền thực thi vào `bin/`. **Đã test end-to-end thật bằng bash** (không chỉ đọc code): dựng 3 frame khác kích thước bằng `ffmpeg lavfi`, chạy đúng câu lệnh concat+scale+pad+libx264 mà `SlideshowVideoRenderService` sinh ra → ra đúng video 1080x1920 (DAR 9:16), 30fps, H.264; test thêm bản có audio (sine wave) → AAC 48kHz mux đúng, `-shortest` cắt đúng theo video. Xác nhận cú pháp FFmpeg trong service đúng trước khi tin tưởng, không đoán.
- **C5**: phát hiện + sửa 1 bug tiềm ẩn quan trọng — `appsettings.json` có `FileStorage.AllowedExtensions`/`AllowedContentTypes` GHI ĐÈ TOÀN BỘ mảng mặc định trong `FileStorageOptions.cs` (IConfiguration binding thay thế mảng, không merge). Chỉ sửa default C# thôi sẽ KHÔNG đủ — `.mp4`/`video/mp4` vẫn bị chặn trong thực tế nếu không sửa luôn `appsettings.json`. Đã sửa cả 2 chỗ.
- **C6-C7**: `FacebookPagePublishService.PublishReelAsync` — đủ 4 bước (start/upload/poll/finish) đúng theo tài liệu Meta Video API đã research đầu phiên. Upload dùng named `HttpClient` riêng (`"FacebookRupload"`, đăng ký ở `Program.cs`) trỏ `rupload.facebook.com` — khác hẳn client typed hiện có đang trỏ `graph.facebook.com`. Routing: `PublishAsync` sniff `MimeType.StartsWith("video/")` khi `mediaItems.Count==1` để rẽ sang `PublishReelAsync` — không thêm field mới vào DTO, theo tiền lệ sniff MIME đã có trong codebase.
- **Build + smoke-test cuối**: `dotnet build` (backend) 0 lỗi. `npm run build` (frontend) 0 lỗi. Chạy thật `dotnet run` — qua hết DI resolution (mọi constructor mới: `SlideshowVideoRenderService`, `IOptions<ReelsOptions>`, `IHttpClientFactory` trong `FacebookPagePublishService`...), EF migration check log rõ "database đã up to date" (xác nhận không cần thêm migration ngoài `AddPostImageCount`), toàn bộ BackgroundService khởi động bình thường.

## Quyết định phát sinh ngoài plan gốc

- `MediaRole.TemplateSource` dùng CHUNG cho cả Template nhiều ảnh lẫn Reels (plan gốc dự định chỉ Reels mới cần role này, Template thì AI chọn xong render thẳng không qua bước lưu tạm). Đổi ý lúc code vì `ProcessImageOverlayAsync` cần biết "N ảnh nguồn nào" trước khi loop render — dùng lại đúng 1 cơ chế cho cả 2 nhánh đỡ phải viết 2 đường dẫn khác nhau, và nếu job lỗi giữa chừng vẫn còn dữ liệu để retry (không mất danh sách ảnh đã chọn).
- Template flow (Phần B) sau khi render xong thì XOÁ `TemplateSource` (dọn dẹp); Reels thì GIỮ LẠI — vì Reels cần cho người dùng chỉnh tay sau (C2), Template thì không có bước chỉnh tay tương đương nên không cần giữ.
- `appsettings.json` cần sửa song song với `FileStorageOptions.cs` cho `.mp4` — phát hiện giữa chừng, không có trong plan gốc (plan chỉ nói sửa `FileStorageOptions.cs`).
- Không thêm `MaxVideoUploadBytes` riêng như plan gốc dự tính — phát hiện `SaveBytesAsync` (path mà `SlideshowVideoRenderService`/`ImageOverlay` dùng để lưu file sinh ra) không hề kiểm `MaxUploadBytes` (chỉ `SaveAsync` cho `IFormFile` upload từ người dùng mới kiểm) — thêm cap không dùng tới sẽ là code thừa.
- **`backend/Resources/ffmpeg/ffmpeg` KHÔNG commit vào git** (đã hỏi người dùng, chọn phương án này) — thêm vào `.gitignore`. File vẫn nằm trên đĩa của máy này nên `dotnet publish`/build zip vẫn hoạt động bình thường ngay bây giờ, nhưng **máy khác clone repo sẽ KHÔNG có sẵn file này** — cần tải lại theo hướng dẫn ở `REELS_PLAN.md` phần C4 (`https://johnvansickle.com/ffmpeg/releases/ffmpeg-release-amd64-static.tar.xz`, giải nén lấy file `ffmpeg`, đặt vào đúng đường dẫn, `chmod +x`) trước khi build/publish bản có Reels.

## REVISION 2 (2026-08-12) — Gộp Reels vào FullAI/Template, chọn lúc ĐĂNG thay vì lúc TẠO

Người dùng đổi ý sau khi bản đầu đã code + build/test xong: Reels không nên là 1 `GenerationFlow` riêng
chọn lúc tạo bài, mà là **lựa chọn ở bước ĐĂNG** (Facebook thường hay Reels) áp dụng cho MỌI post đã sinh
xong (FullAI hay Template). Xem plan đầy đủ ở `REELS_PLAN.md` (đã ghi đè bằng bản REVISION 2).

**Đã bỏ:**
- `GenerationFlow.Reels` (enum value 7) — revert hoàn toàn.
- `GenerateFromReelsAsync` + nhánh dispatch trong `GenerateForPostAsync`.
- Option "Reels" trong `GenerationFlowPicker.jsx`/`PostCreateForm.jsx` (revert về chỉ FullAI/Template).
- Điều kiện đặc cách "giữ TemplateSource nếu Reels" trong `ProcessImageOverlayAsync` — quay lại dọn vô điều kiện (Template lại là nơi DUY NHẤT tạo TemplateSource lúc sinh bài).

**Đã giữ nguyên 100% (không đổi 1 dòng nào):** FFmpeg binary, `SlideshowVideoRenderService`, `FileStorageOptions`/`appsettings.json` cho `.mp4`, `FacebookPagePublishService.PublishReelAsync` + routing MimeType `video/`, Phần A + Phần B (Template chọn N ảnh) — toàn bộ hạ tầng render/publish video tái dùng nguyên vẹn, chỉ đổi CHỖ GỌI.

**Đã thêm/sửa mới:**
- `BuildFrameRenderContextAsync` + `RenderFrameAsync` — trích xuất từ vòng lặp cũ trong `ProcessImageOverlayAsync` thành 2 method dùng chung: `BuildFrameRenderContextAsync` tính 1 lần headline/subheadline/bullet/liên hệ/logo/brand color (record `FrameRenderContext`), `RenderFrameAsync` render 1 ảnh nguồn thành 1 khung hình có chữ. Cả `ProcessImageOverlayAsync` (Template nhiều ảnh) lẫn `ProcessReelsRenderAsync` (ảnh mới thêm vào Reels) đều gọi lại đúng 2 method này — không còn code trùng lặp.
- `GenerationJobPipelineService.ConvertToReelsAsync(postId, frameMediaIds, ct)` — method public mới, entry point cho "Đăng dạng Reels": `QueueReelsRenderAsync` (tạo job row, giờ cho phép cả trạng thái `Approved` không chỉ `WaitingReview`) rồi gọi thẳng `ProcessReelsRenderAsync(job, frameMediaIds, ct)` (overload mới, bỏ qua dispatcher `ProcessAsync` chung vì cần truyền thêm `frameMediaIds`).
- `ProcessReelsRenderAsync` viết lại hoàn toàn cách lấy khung hình — thứ tự ưu tiên: `frameMediaIds` tường minh (người dùng chọn qua picker) → `TemplateSource` (lần chỉnh tay trước, nếu có) → ảnh Cover/Attachment hiện tại của post theo `SortOrder` (mặc định, "biến ảnh đang có thành video"). Mỗi mediaId: nếu ĐÃ thuộc post (Cover/Attachment hiện tại) → dùng thẳng bytes (đã có chữ, không render lại — tránh chồng chữ 2 lần); nếu là ảnh MỚI từ MediaFolder → gọi `RenderFrameAsync` render cho nhất quán trước khi ghép. Vẫn giữ overload 1-tham-số cho dispatcher `ProcessAsync` (dùng khi retry qua job timeline, mặc định `frameMediaIds: null`).
- `PostController`: `PUT reels-frames` giữ nguyên (bỏ validation `GenerationFlow != Reels`, áp dụng mọi post); đổi `POST regenerate-reels-video` → `POST convert-to-reels` (nhận body `{ mediaIds }` optional, gọi thẳng `ConvertToReelsAsync`).
- `ReelsFramePicker.jsx` viết lại: bỏ gate `generationFlow !== 7` (early-return null nếu post chưa có ảnh nào, không phân biệt flow); mặc định tick sẵn = ảnh Cover/Attachment hiện tại của post (đọc qua `postMediaApi.getByPost`), trừ khi đã có `TemplateSource` từ lần chỉnh tay trước thì ưu tiên dùng lại; khối "chọn thêm từ thư mục" chỉ hiện khi page có `MediaFolder` (post FullAI thường không có, tự động ẩn không lỗi); 1 nút chính "🎬 Đăng dạng Reels" gọi `convert-to-reels` với đúng `selected` hiện tại (gộp bước lưu + convert làm 1, không cần 2 lượt bấm cho trường hợp mặc định).
- Đổi tên hook/API: `useRegenerateReelsVideo` → `useConvertToReels` (nhận `{id, mediaIds}`), `postApi.regenerateReelsVideo` → `postApi.convertToReels`.

**Verify sau revision:** `dotnet build` (backend) 0 lỗi, `npm run build`/`oxlint` (frontend) 0 lỗi/warning mới, `dotnet run` thật qua hết DI + EF migration check + BackgroundService start bình thường (test lại từ đầu sau khi bỏ `GenerationFlow.Reels` để chắc chắn không có DI nào còn phụ thuộc enum đã xoá). Chưa test end-to-end qua UI thật (tạo post → convert → publish Facebook Reels thật) — để người dùng tự kiểm tra tay theo mục Verification trong `REELS_PLAN.md`.

**Quyết định phát sinh mới:**
- `ConvertToReelsAsync`/`ProcessReelsRenderAsync` KHÔNG đi qua dispatcher `ProcessAsync(jobId, ct)` chung như các flow khác — vì cần truyền thêm `frameMediaIds` mà dispatcher không hỗ trợ tham số phụ. Giữ overload 1-tham-số cho `ProcessAsync` switch (dùng khi retry qua job timeline) để không phá vỡ pattern retry chung của hệ thống.
- `QueueReelsRenderAllowedStatuses` tách riêng khỏi `QueueRenderAllowedStatuses` (thêm `Approved`) — vì giờ Reels là hành động ở bước đăng, có thể xảy ra sau khi post đã duyệt, khác với `ImageOverlay` (Template) vốn luôn chạy trước khi duyệt.
