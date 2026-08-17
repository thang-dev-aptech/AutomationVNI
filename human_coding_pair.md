# Pair-coding: Đăng bài TikTok

Checklist theo plan `Đăng bài lên TikTok — tái dùng pipeline Facebook / Reels`.
Đi từng mục một, tick `[x]` khi xong. Nhánh làm việc: `feature/tiktok-publish`.

> Yêu cầu trước khi bắt đầu phần OAuth thật: cần 1 TikTok Developer App đã đăng ký
> (có `client_key` / `client_secret`, đã bật scope `user.info.basic`, `video.publish`,
> `video.upload`). Chưa audit thì chỉ đăng được ở chế độ `SELF_ONLY` (riêng tư).

## 0. Setup
- [x] Tạo nhánh `feature/tiktok-publish`
- [x] Tạo file `human_coding_pair.md` này

## 1. Backend — Enum
- [x] `backend/Modules/SocialChannel/Enums/SocialChannelEnums.cs`
  - [x] Thêm `SocialChannelType.TikTok = 5`
  - [x] Thêm `SocialProvider.TikTok = 4`
  - (`SocialPlatform.TikTok = 4` đã có sẵn, không cần sửa)

## 2. Backend — OAuth connect flow
- [x] `backend/Shared/TikTok/TikTokOAuthOptions.cs` (+ RefreshEnabled/RefreshIntervalMinutes/RefreshBeforeExpiryHours — đơn vị giờ vì access token TikTok chỉ sống 24h)
- [x] `appsettings.json` — block `TikTokOAuth` (sau `ThreadsOAuth`)
- [ ] `dotnet user-secrets` — set `TikTokOAuth:ClientKey` / `TikTokOAuth:ClientSecret` thật (không commit) — **cần có TikTok Developer App trước**
- [x] `backend/Shared/TikTok/TikTokOAuthDtos.cs`, `ITikTokOAuthService.cs`, `TikTokOAuthService.cs`
  - [x] `BuildConnectUrl` — dựng URL `https://www.tiktok.com/v2/auth/authorize/`, cache `state` chống CSRF
  - [x] `HandleCallbackAsync` — đổi code → token (`POST /v2/oauth/token/`, kèm sẵn open_id + refresh_token), lấy profile (`/v2/user/info/`)
  - [x] `RefreshTokenAsync` — dùng `refresh_token` (không phải access_token như Threads), TikTok rotate cả 2 token mỗi lần refresh
- [x] `backend/Shared/TikTok/TikTokProfileSyncService.cs` — upsert `SocialChannelModel` (Platform=TikTok, ExternalPageId=open_id)
- [x] `backend/Modules/SocialChannel/SocialChannelRepository.cs` — mở rộng `UpsertFromMetaAsync` thêm param optional `refreshToken` (cột `RefreshToken` có sẵn trên model nhưng chưa từng được ghi) — không cần method mới, không phá lời gọi Meta/Threads cũ
- [x] `backend/Modules/TikTok/TikTokController.cs`
  - [x] `GET /api/tiktok/connect-url`
  - [x] `GET /api/tiktok/callback` (`[AllowAnonymous]`)
- [x] `backend/Shared/TikTok/TikTokTokenRefreshService.cs` — quét theo giờ (không phải ngày), refresh bằng RefreshToken, cập nhật cả AccessToken lẫn RefreshToken mới sau mỗi lần refresh
- [x] `Program.cs` — đăng ký DI: `TikTokOAuthService`, `TikTokProfileSyncService`, `TikTokTokenRefreshService` (hosted service), `HttpClient` cho OAuth, `Configure<TikTokOAuthOptions>`

## 3. Backend — Publish service
- [x] `backend/Shared/SocialPublish/SocialPublishOptions.cs` — thêm `UseRealTikTok` + `TikTokPublishOptions` (ApiBaseUrl, PublishTimeoutSeconds, DefaultPrivacyLevel=SELF_ONLY, PublicBaseUrl, MaxCaptionLength)
- [x] `backend/Shared/SocialPublish/TikTokPublishService.cs`
  - [x] `PublishAsync` — dispatch theo media (video / ảnh / rỗng)
  - [x] `PublishVideoAsync` — init (FILE_UPLOAD, 1 chunk = cả file, giống Facebook Reels) → upload PUT → poll status
  - [x] `PublishPhotoPostAsync` — init (PULL_FROM_URL) → poll status
  - [x] Case 0 media → trả `TIKTOK_UNSUPPORTED_FORMAT`
  - [x] `CommentAsync` → trả `TIKTOK_COMMENT_UNSUPPORTED`
  - [x] Error codes `TIKTOK_*` + xử lý envelope 2 tầng (HTTP lỗi vs body lỗi dù HTTP 200)
- [x] `appsettings.json` — block `SocialPublish:TikTok` + `UseRealTikTok: false`
- [x] `Program.cs` — đăng ký typed `HttpClient<TikTokPublishService>`

## 4. Backend — Nối dispatcher
- [x] `backend/Shared/SocialPublish/SocialPublishService.cs` — inject `TikTokPublishService`, thêm `PublishMode.RealTikTok`, mở rộng `ResolvePublishMode`
- [x] `backend/Modules/PublishLog/PublishPipelineService.cs:253-255` — thêm 3 mã lỗi TikTok vào `IsTokenOrPermissionError`

## 5. Frontend — Constants & API
- [x] `ClientApp/src/modules/social-channels/constants/socialPlatform.js` — `SocialChannelType`, `SOCIAL_PROVIDERS`, `PROVIDER_CATALOG` entry cho TikTok
- [x] `ClientApp/src/modules/social-channels/services/tiktokApi.js` (mới)
- [x] `ClientApp/src/modules/social-channels/hooks/useSocialChannels.js` — thêm `useTikTokConnectUrl()`

## 6. Frontend — UI connect flow
- [x] `ClientApp/src/modules/social-channels/pages/PlatformsPage.jsx` — `handleConnectTikTok` + xử lý redirect-back `?tiktokConnected=...` + `handleResync`/`resyncPending` cập nhật cho TikTok
- [x] `ClientApp/src/modules/social-channels/components/ConnectionCard.jsx` — nhánh group TikTok + `ChannelSection` + đếm "N Profile"
- [x] (phát sinh) `backend/Modules/SocialConnection/SocialConnectionDtos.cs` + `SocialConnectionRepository.cs` — thêm `TikTokCount` để frontend đếm đúng, giống `ThreadsCount` đã có

## 7. Verification
- [ ] Mock mode: Publish Now trên channel TikTok mock → `PostStatus.Published`
- [ ] OAuth thật: bấm Connect → tạo đúng `SocialChannelModel`, hiển thị đúng nhóm "TikTok"
- [ ] Publish thật (sandbox, `UseRealTikTok=true`): 1 post ảnh + 1 post video, `SELF_ONLY`, poll ra `PUBLISH_COMPLETE`
- [ ] Token invalid → `PostStatus.NeedFix`
- [ ] Token sắp hết hạn → `TikTokTokenRefreshService` tự renew

---
Ghi chú trong lúc code: ghi thẳng vào đây (dưới mỗi mục hoặc cuối file) nếu có quyết định/đổi ý so với plan gốc, để không lạc mất ngữ cảnh.

## Điểm dừng hiện tại (2026-08-17)
- Backend (mục 1-4): xong, `dotnet build` **0 lỗi** (SDK 10.0.400).
- Frontend (mục 5-6): xong, `npm run build` **0 lỗi** — phải `npm install` lại 1 lần vì
  `node_modules` trước đó thiếu shim Windows (`vite.cmd`), có vẻ được cài từ máy Linux rồi copy sang.
- `dotnet user-secrets set` cho `TikTokOAuth:ClientKey`/`ClientSecret` — đã làm xong (real TikTok
  sandbox app). OAuth connect-flow thật đã test qua ngrok tunnel, đăng nhập TikTok thành công.
- Còn thiếu: publish thật (sandbox, `UseRealTikTok=true`) + token-invalid/token-refresh test (mục 7).

---

# Pair-coding vòng 2: UX điều hướng + thư viện nhạc + TikTok Inbox Draft

Plan gốc: `Tạo bài đăng TikTok — điều hướng UX + thư viện nhạc + đóng nốt gap config`
(đã approve). Checklist theo 7 phần của plan.

## A. Frontend — gợi ý/auto-default Reels cho kênh TikTok
- [x] `PostCreateForm.jsx` — state `formatTouched`, `handleFormatChange` bọc `onChange` của
      `PostFormatPicker` để không đè lựa chọn thủ công
- [x] `PostCreateForm.jsx` — `channelById` map + `allSelectedAreTikTok`/`hasTikTokSelected`
      (platform===4) + `useEffect` auto-set `generateAsReels=true` khi mọi kênh chọn đều TikTok
      và user chưa tự đổi định dạng
- [x] `PostCreateForm.jsx` — hint cảnh báo dưới `PostFormatPicker` khi có TikTok + chưa chọn Reels
- [x] `BulkCreatePage.jsx` — hint tương tự (không auto-default, chỉ cảnh báo), gộp cả
      `skelChannelIds` (khung lịch) và `channelIds` (fan-out) vì dùng chung 1 panel định dạng

## B. Frontend — sửa nhãn
- [x] `PostFormatPicker.jsx` — `'Bài viết Facebook'` → `'Bài viết (ảnh)'`

## C. Backend/docs — đóng gap `PublicBaseUrl` cho TikTok
- [x] `.env.example` — thêm `TikTokOAuth__*` (OAuth) + `SocialPublish__TikTok__*` (publish),
      mirror đúng convention Threads
- [x] `VNI Automation env production.md` — thêm mục "6B. TIKTOK OAUTH" (không renumber các mục
      sau để tránh vỡ tham chiếu "mục N" trong file) + block `SocialPublish__TikTok__*` trong
      mục 8 + cập nhật checklist cuối file (redirect URI TikTok)

## D. Thư viện nhạc — Backend
- [x] `backend/Modules/MusicTrack/MusicTrackModel.cs` (mới)
- [x] `backend/Modules/MusicTrack/MusicTrackDtos.cs`, `MusicTrackUrls.cs` (mới)
- [x] `backend/Modules/MusicTrack/MusicTrackRepository.cs` (mới)
- [x] `backend/Modules/MusicTrack/MusicTrackController.cs` (mới) — GET list (kế thừa
      `BaseController`) / POST upload / GET preview (streaming, `enableRangeProcessing:true` cho
      tua audio) / DELETE (kế thừa)
- [x] `AppDbContext` — `DbSet<MusicTrackModel>` + `modelBuilder.Entity<MusicTrackModel>` +
      migration `20260817081343_AddMusicTracks`
- [x] `Program.cs` — đăng ký `MusicTrackRepository`
- [x] `FileStorageOptions` + `appsettings.json` — mở whitelist `.mp3/.wav/.m4a`
- [x] `SlideshowVideoRenderService.RenderAsync` — thêm `string? audioTrackPathOverride = null`
      (đặt trước `ct`, có default nên không phá lời gọi cũ)
- [x] `GenerationJobPipelineService` — `ConvertToReelsAsync`/`ProcessReelsRenderAsync` thêm
      `Guid? musicTrackId` — đặt SAU `CancellationToken ct` (theo đúng convention đã dùng cho
      `refreshToken` ở vòng 1) để không phá 2 lời gọi cũ (`PostController.cs:208`,
      `PostGenerationWorker.cs:96` đều gọi `(postId, null, ct)` theo thứ tự cũ)
- [x] `ResolveMusicTrackTempFileAsync` (helper mới) — copy nhạc từ storage ra file tạm
      `%TEMP%/music-{guid}.ext` vì FFmpeg cần path thật trên đĩa; xoá trong `finally` sau render
- [x] `PostController` — endpoint convert-to-reels nhận `musicTrackId` optional
      (`ConvertToReelsRequest.MusicTrackId`)
- [x] `dotnet build` **0 lỗi** sau bước D (20 warning cũ, không thêm mới)

## E. Thư viện nhạc — Frontend
- [x] `modules/music/services/musicTrackApi.js` (mới)
- [x] `modules/music/hooks/useMusicTracks.js` (mới)
- [x] `modules/music/pages/MusicLibraryPage.jsx` (mới) — upload inline (không modal, giữ đơn giản)
- [x] `modules/music/components/MusicTrackCard.jsx` (mới) — `<audio controls>`
- [x] Route `/music` (`router/index.jsx`) + nav "Nhạc" (`MainLayout.jsx`, dùng lại quyền
      `canViewMedia`/`canManageMedia` — không tạo quyền riêng)
- [x] `ReelsFramePicker.jsx` — dropdown chọn nhạc trước hàng nút hành động (`useMusicTrackAll`)
- [x] `usePosts.js`/`postApi.js` — `convertToReels` truyền thêm `musicTrackId`
- [x] `npm run build` **0 lỗi** sau bước E

## F. TikTok Inbox Draft (MEDIA_UPLOAD) — Backend
- [x] `enum TikTokPostMode { DirectPost = 1, InboxDraft = 2 }` (`Post/Enums/PostEnums.cs`)
- [x] `PostModel.TikTokPostMode` (nullable) + EF Core migration `20260817082043_AddPostTikTokPostMode`
- [x] `SocialPublishRequest.TikTokPostMode`
- [x] `PublishPipelineService.ExecutePublishAsync` — gán `TikTokPostMode = post.TikTokPostMode`
- [x] `TikTokPublishService.PublishVideoToInboxAsync` (mới) — `/v2/post/publish/inbox/video/init/`,
      không gửi `post_info`; `PollPublishStatusAsync` tham số hoá `successStatus` (mặc định
      `PUBLISH_COMPLETE`, Inbox truyền `SEND_TO_USER_INBOX`) — publishedUrl rỗng cho case Inbox vì
      TikTok chưa thật sự đăng gì
- [x] `TikTokPublishService.PublishAsync` — dispatch InboxDraft khi video đơn, fallback DirectPost
      nếu là ảnh (log warning)
- [x] `PostWorkflowService.ScheduleAsync`/`PublishNowAsync` — thêm `TikTokPostMode? tikTokPostMode`
      SAU `ct` (cùng convention tránh phá lời gọi cũ)
- [x] `PostController` — `schedule` nhận `SchedulePostRequest.TikTokPostMode`; `publish-now` nhận
      body mới `PublishNowRequest` (trước đây không có `[FromBody]`)
- [x] `dotnet build` **0 lỗi** sau bước F (20 warning cũ, không thêm mới)

## G. TikTok Inbox Draft — Frontend
- [x] `PostWorkflowActions.jsx` — nhận thêm prop `channel`, `<select>` chế độ đăng TikTok (chỉ hiện
      khi `channel.platform === 4` và có nút Đăng ngay/Lên lịch)
- [x] `PostDetailPage.jsx` — tìm `channel` từ `useSocialChannelAll()` theo `post.socialChannelId`,
      truyền xuống `PostWorkflowActions`
- [x] `usePosts.js` — `usePublishNowPost` đổi từ nhận thẳng `id` sang nhận `{ id, tikTokPostMode }`
      (chỉ 1 nơi gọi trong repo, đã cập nhật theo)
- [x] `postApi.js` — `publishNow(id, tikTokPostMode)` + `schedule` payload thêm `tikTokPostMode`
- [x] `PublishLogTable.jsx` — badge tím "📥 TikTok Inbox" ở cột Error khi `responsePayload` chứa
      `SEND_TO_USER_INBOX` (thay vì banner riêng — gọn hơn trong bảng)
- [x] `npm run build` **0 lỗi** sau bước G

---

## Điểm dừng vòng 2 (2026-08-17)
- Tất cả 7 mục (A–G) đã code xong. `dotnet build` và `npm run build` đều **0 lỗi** ở lần build
  cuối cùng (không có warning mới ngoài 20 warning cũ có sẵn từ trước).
- 2 migration EF Core mới: `20260817081343_AddMusicTracks`, `20260817082043_AddPostTikTokPostMode`
  — CHƯA áp dụng vào DB (migration tự chạy lúc backend khởi động qua `db.Database.MigrateAsync()`
  ở `Program.cs`, chỉ cần chạy `dotnet run`/khởi động backend là xong, không cần lệnh `ef database
  update` thủ công).
- Chưa test runtime — cần chạy theo đúng 6 bước ở mục "Verification" của plan gốc
  (`C:\Users\DELL\.claude\plans\gentle-waddling-wreath.md`):
  1. Nhánh Reels tự động khi chỉ chọn kênh TikTok (hint + auto-default)
  2. Nhánh photo post (set tạm `PublicBaseUrl` = domain ngrok)
  3. Hint hiển thị/biến mất đúng lúc
  4. Thư viện nhạc: upload mp3 → nghe thử → chọn khi Đăng dạng Reels → xác nhận video có đúng nhạc
  5. Không chọn nhạc → hành vi y hệt cũ (regression)
  6. TikTok Inbox Draft: chọn "Gửi vào Draft" → publish → kiểm `ResponsePayload` chứa
     `SEND_TO_USER_INBOX`, `PublishedUrl` rỗng → mở app TikTok thật trên điện thoại xác nhận video
     nằm trong Inbox
