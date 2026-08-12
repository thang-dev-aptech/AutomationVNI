# Plan REVISION 2: Gộp Reels vào FullAI/Template — chọn lúc ĐĂNG, không phải lúc TẠO bài

## Context (revision này thay đổi gì so với bản đã code)

Bản đầu (đã code + build/test xong trên branch `feature/reels-multi-image-template`) làm Reels thành
**1 `GenerationFlow` riêng** (`Reels = 7`) — người dùng chọn "Reels" ngay lúc tạo bài, AI tự chọn khung
hình từ MediaFolder, ghép video tự động trong lúc sinh bài.

Người dùng muốn đổi lại: **Reels không phải lựa chọn lúc TẠO bài, mà là lựa chọn lúc ĐĂNG bài.** Tạo bài
vẫn chỉ có 2 flow như cũ (FullAI, Template — Template vẫn giữ khả năng nhiều ảnh vừa làm). Sau khi bài đã
sinh xong (ảnh sẵn có, dù FullAI 1 ảnh AI hay Template N ảnh), người dùng mới quyết định: **đăng Facebook
bình thường** (giữ nguyên, ảnh/multi-photo như cũ) hay **đăng Reels** (biến ảnh đang có thành video).

Đã hỏi lại: khi "biến thành video", có 2 khả năng — (a) dùng đúng ảnh bài đang có, hoặc (b) vẫn cho chỉnh
tay (thêm/bớt/sắp xếp) trước khi ghép. Người dùng chọn **cả hai** — mặc định dùng ảnh bài đang có, nhưng
vẫn giữ được picker để chỉnh tay/thêm ảnh khác từ thư mục nếu muốn.

Phần lớn hạ tầng đã xây ở bản đầu **vẫn dùng lại được nguyên vẹn**: FFmpeg static binary đã tải + test
end-to-end, `SlideshowVideoRenderService`, `FileStorageOptions` cho `.mp4`, `FacebookPagePublishService
.PublishReelAsync` (3-phase upload) + routing theo MimeType `video/` — **không đổi gì ở các phần này**.
Chỉ đổi phần "khi nào và từ ảnh nào thì kích hoạt ghép video".

## Phần đã có, KHÔNG đụng tới

- `backend/Resources/ffmpeg/ffmpeg` (đã tải thật, `.gitignore`) + `ReelsOptions` (`Shared/SocialPublish/SocialPublishOptions.cs`).
- `Modules/MediaAsset/SlideshowVideoRenderService.cs` — ghép N frame bằng FFmpeg, đã test end-to-end.
- `Shared/FileStorageOptions.cs` + `appsettings.json` — đã cho phép `.mp4`/`video/mp4`.
- `Shared/SocialPublish/FacebookPagePublishService.cs` — `PublishReelAsync` (start/upload/poll/finish) +
  named `HttpClient` "FacebookRupload" (`Program.cs`) + routing `mediaItems.Count==1 && MimeType video/`
  trong `PublishAsync`. Cơ chế này đã ĐÚNG Ý: hễ post chỉ còn đúng 1 media là video, publish tự động ra
  Reels — không cần sửa gì thêm, chỉ cần tầng trên đảm bảo đúng lúc post có 1 media video.
- Phần A + Phần B (Template chọn N ảnh) — giữ nguyên 100%, không phải sửa.
- `MediaRole.TemplateSource` (`MediaAssetEnums.cs`) — vẫn dùng cho Template chọn N ảnh (Phần B) VÀ giờ
  dùng thêm cho bước chỉnh tay khung hình Reels (mục 3 dưới).

## Phần cần bỏ (revert từ bản đầu)

1. `Modules/Post/Enums/PostEnums.cs`: bỏ `GenerationFlow.Reels = 7`.
2. `GenerationJobPipelineService.cs`: bỏ `GenerateFromReelsAsync` + nhánh dispatch `if (post.GenerationFlow == GenerationFlow.Reels)` trong `GenerateForPostAsync`.
3. `ProcessImageOverlayAsync`: bỏ điều kiện đặc cách "giữ TemplateSource nếu là Reels" — quay lại dọn TemplateSource vô điều kiện sau khi render (Template flow lại là nơi DUY NHẤT tạo TemplateSource lúc sinh bài).
4. Frontend: bỏ option "Reels" khỏi `GenerationFlowPicker.jsx` (revert), bỏ nhánh `isReelsFlow` khỏi `PostCreateForm.jsx` (revert về chỉ `isTemplateFlow`).

## Phần cần thêm/sửa mới

### 1. Trích xuất `RenderFrameAsync` dùng chung

`ProcessImageOverlayAsync` hiện loop qua N ảnh nguồn, build `ImageOverlayRequest` (headline/subheadline/
bullet/contact/brand color/logo — tính 1 lần dùng chung) rồi gọi `imageOverlayService.RenderAsync` cho
từng ảnh. Tách phần "render 1 ảnh nguồn thành 1 ảnh có chữ" ra hàm riêng
`RenderFrameAsync(PostModel post, MediaAssetModel sourceMedia, <các tham số dùng chung đã tính 1 lần>, ct)
-> MediaAssetModel`, để dùng lại được ở mục 3 (convert-to-reels cần render THÊM ảnh mới chọn từ thư mục,
không chỉ ảnh đã render sẵn của post).

### 2. `ConvertToReelsAsync` — biến ảnh bài đang có thành video

Method mới trong `GenerationJobPipelineService.cs`, dùng `JobType.ReelsRender` (giữ nguyên enum + job
bookkeeping đã có) nhưng đổi cách lấy khung hình:

```
ConvertToReelsAsync(postId, frameMediaIds: List<Guid>?, ct):
  post = RequirePostAsync(postId)
  current = PostMedia hiện tại của post, role Cover/Attachment (id + storageKey) — đây LÀ ẢNH ĐÃ CÓ CHỮ
  order = frameMediaIds ?? (đọc TemplateSource nếu có) ?? current.Select(x => x.MediaId) theo SortOrder
  // Mặc định không cần chỉnh tay: order = current, dùng thẳng luôn.

  frames = []
  for each mediaId in order:
    if mediaId nằm trong current (đã là Cover/Attachment của CHÍNH post này):
      dùng thẳng làm frame (đã có chữ rồi, không render lại — render lại sẽ chồng chữ 2 lần)
    else:
      // ảnh mới người dùng thêm từ MediaFolder, chưa qua overlay — render cho nhất quán
      rendered = RenderFrameAsync(post, sourceMedia, ...) // mục 1
      dùng rendered làm frame

  videoBytes = SlideshowVideoRenderService.RenderAsync(frames, brandColorHex)
  lưu video qua fileStorageService.SaveBytesAsync (đã hỗ trợ .mp4)
  SoftDeleteAllForPostAsync(postId, [Attachment, Cover])   // dọn hết ảnh cũ
  ReplaceCoverAsync(postId, videoAssetId)                  // post giờ chỉ còn 1 media = video
  dọn TemplateSource nếu có (đã dùng xong)
```

Việc "ảnh đã thuộc post hay chưa" tự suy ra được (so mediaId với danh sách Cover/Attachment hiện tại của
CHÍNH post đó) — không cần thêm field đánh dấu nguồn, không cần đổi DTO.

`QueueReelsRenderAsync`/`ProcessReelsRenderAsync` (đã có) đổi tên/nội dung cho khớp: `QueueReelsRenderAsync`
giữ nguyên (tạo job row, set status RenderingTemplate) nhưng bỏ ràng buộc trạng thái quá chặt — cho phép
từ `WaitingReview` HOẶC `Approved` (người dùng có thể muốn đổi ý đăng Reels sau khi đã duyệt bài).

### 3. Endpoint: `PUT reels-frames` (giữ) + `POST convert-to-reels` (đổi tên từ regenerate-reels-video)

`PostController.cs`:
- `PUT /api/Post/{id}/reels-frames` — GIỮ NGUYÊN như đã code (ghi `TemplateSource` theo mảng mediaIds
  người dùng chọn/sắp xếp) — chỉ bỏ validation `GenerationFlow != Reels` (áp dụng được cho MỌI post có
  ảnh, không phân biệt flow tạo).
- `POST /api/Post/{id}/convert-to-reels` (đổi tên từ `regenerate-reels-video`) — gọi
  `ConvertToReelsAsync` (mục 2) thay vì `QueueImageRenderAsync`+`QueueReelsRenderAsync` nối tiếp như bản
  cũ (bản cũ render TỪ TemplateSource giả định luôn là ảnh thô; bản mới xử lý ảnh thô LẪN ảnh đã render
  sẵn của post, nên gộp lại thành 1 method).

### 4. Frontend: `ReelsFramePicker.jsx` mở rộng phạm vi hiển thị

- Bỏ điều kiện `post.generationFlow !== 7` — hiện với MỌI post đã có ít nhất 1 ảnh (`Cover`/`Attachment`
  không rỗng), không phân biệt FullAI hay Template.
- Danh sách mặc định tick sẵn: đọc từ `postMediaApi.getByPost` lọc `mediaRole` là `Cover`(4)/`Attachment`(3)
  (ảnh ĐÃ CÓ của post) — thay vì đọc `TemplateSource`(5) như bản cũ (vì mới tạo bài thì chưa hề có
  `TemplateSource`, chỉ có khi người dùng chủ động mở picker và bấm chọn).
- Khối "chọn thêm từ thư mục Template": vẫn hiện nếu page có `MediaFolder` (dùng lại `mediaFolderApi
  .filter`/`mediaAssetApi.filter` đã thêm) — với post FullAI (thường không gắn MediaFolder) khối này
  đơn giản không có ảnh để hiện, không lỗi gì.
- Đổi nhãn nút: "Dựng lại video" → "🎬 Đăng dạng Reels" (gọi `convert-to-reels`); bỏ nút "Lưu khung hình"
  riêng — 1 nút xác nhận là chuyển thẳng, `PUT reels-frames` gọi ngầm bên trong (chỉ khi người dùng có
  chỉnh tay khác mặc định, tránh gọi thừa).
- Đặt trong `PostDetailPage.jsx` như 1 khối luôn hiện (không còn gate theo `generationFlow`), đặt cạnh
  `PostGenerationActions` — đổi tiêu đề khối cho đúng ngữ cảnh mới ("Muốn đăng Reels thay vì bài ảnh
  thường?").

### 5. Publish bình thường — không đổi

Sau `ConvertToReelsAsync`, post chỉ còn 1 `PostMedia` là video → luồng Approve → Schedule/Đăng ngay hiện
tại (`PostWorkflowService`, không đổi) hoạt động y hệt, `FacebookPagePublishService.PublishAsync` tự
route sang `PublishReelAsync` nhờ MimeType `video/` (đã có, không đổi). Nếu người dùng KHÔNG bấm "Đăng
dạng Reels", post giữ nguyên ảnh, đăng Facebook như cũ (photo/multi-photo) — không có gì thay đổi so với
hành vi hiện tại.

Muốn quay lại ảnh (sau khi đã convert sang video, đổi ý muốn đăng ảnh thường): bấm "Tạo lại ảnh" (nút có
sẵn, `RegenerateImage`) — sinh lại ảnh mới, tự nhiên thay thế video.

## Verification

1. `cd backend && dotnet build` — 0 lỗi. Không cần migration mới (chỉ bỏ 1 enum value, không đổi schema).
2. Tạo bài FullAI bình thường (1 ảnh AI) → mở `ReelsFramePicker` → xác nhận mặc định tick sẵn đúng 1 ảnh
   đó → bấm "Đăng dạng Reels" → xác nhận ra đúng 1 video, ảnh cũ bị xoá khỏi PostMedia.
3. Tạo bài Template với `ImageCount=4` → convert sang Reels → xác nhận cả 4 ảnh (đã có chữ, không bị
   render chồng chữ lần 2) được ghép làm 4 khung hình video theo đúng thứ tự.
4. Test "chỉnh tay": convert xong 1 bài, mở lại picker, thêm 1 ảnh mới từ thư mục (chưa từng render) →
   xác nhận ảnh mới được overlay chữ (không phải ảnh trần) trước khi ghép vào video.
5. Test publish thật: post đã convert sang video → Approve → Đăng ngay → xác nhận `PublishReelAsync`
   được gọi (không phải `PublishPhotoMultipartAsync`), lên Facebook Reels thật.
6. Test không convert: tạo bài Template N ảnh, KHÔNG bấm Reels, đăng thẳng → xác nhận ra bài multi-photo
   bình thường như trước — không có regression.

## Tham chiếu: Phần A + B (đã code xong, không đổi trong revision này)

- **Phần A**: `MediaIntelligenceService.MatchForPostAsync` (`Modules/MediaAsset/MediaIntelligenceService.cs:370-435`) thêm `Guid? folderId = null` — lọc ứng viên theo MediaFolder của page thay vì toàn kho.
- **Phần B**: `PostModel.ImageCount` (null/1 = hành vi cũ; >=3 = Template nhiều ảnh) → `GenerateFromTemplateAsync` dùng `MatchForPostAsync` chọn N ảnh (fallback ngẫu nhiên nếu AI lỗi/thiếu) → `ProcessImageOverlayAsync` loop render N ảnh, lưu `MediaRole.Attachment` → publish tự route multi-photo qua `FacebookPagePublishService.PublishMultiPhotoFeedAsync` có sẵn. UI: ô nhập "Số lượng ảnh" trong `PostCreateForm.jsx` khi flow Template.
