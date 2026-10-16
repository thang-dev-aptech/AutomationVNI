# Full Media Pipeline cho Chiến dịch — Thiết kế

Ngày: 2026-10-16 · Nhánh: Implement-CRM · Trạng thái: chờ duyệt spec

## 1. Mục tiêu

Thêm chế độ **Full Media** cho Chiến dịch: lấy ảnh từ cây Google Drive → Vision AI mô tả ảnh →
chấm mức phù hợp với từng page của chiến dịch → chỉ page đạt ngưỡng mới được sinh text theo
phong cách page → tự động xếp lịch đăng. Ảnh không page nào đạt ngưỡng thì bị loại, chiến dịch
lấy ảnh khác. Toàn bộ quá trình **tự động, không có bước duyệt tay**.

## 2. Quyết định đã chốt (từ người dùng)

| # | Quyết định |
|---|---|
| D1 | Nguồn ảnh: **cây Google Drive** (thư mục nguồn + thư mục con), ảnh đã nằm trong Media. |
| D2 | Ngưỡng duy nhất: **94%**. Ảnh đăng lên **tất cả page đạt ≥ 94%**. Không có ngưỡng 85%. |
| D3 | Ảnh không page nào đạt → **Rejected** cho chiến dịch đó (không xoá khỏi Drive/Media), lấy ảnh khác. |
| D4 | Bài sau khi sinh text **tự động xếp lịch** (Scheduled), không qua bước duyệt. |
| D5 | Các bài của các page cho cùng một ảnh dùng **cùng khe giờ cộng độ lệch ngẫu nhiên** như chiến dịch hiện tại. |
| D6 | Cách chấm điểm = **cách A**: Vision mô tả ảnh một lần (tiếng Anh, có cấu trúc, lưu đệm), sau đó Jev chấm từng page bằng văn bản. |
| D7 | Mô tả ảnh và hồ sơ page gửi Jev bằng **tiếng Anh**. Ảnh **không** gửi tới Jev. |
| D8 | Hồ sơ page được dịch sang tiếng Anh **một lần tổng thể**; sau đó chỉ dịch lại page vừa tạo/sửa. Bản dịch **ẩn** (chỉ ở backend, không trả qua API cho frontend, không hiện trên UI). |
| D9 | Worker **không được ảnh hưởng** tiến trình khác của app (mục 8). |
| D10 | **Không có thao tác con người** trong quá trình (không duyệt tay, không nút đánh giá lại, không gán nhãn). Hệ thống tự kiểm tra chính nó (mục 7). |
| D11 | Jev được gọi qua **Vercel AI Gateway** (tính tiền theo token, không dùng gói trả trước của `jev-ai.pro`): `POST https://ai-gateway.vercel.sh/typesafe/v1/systemone`, model `typesafe-ai/jev`, câu hỏi kiểu `noul`, kèm `providerOptions.gateway.zeroDataRetention = true`. |

## 3. Ngoài phạm vi (bản đầu)

- Ghi đè ngưỡng bằng tay, hàng chờ xem tay, nút "Đánh giá lại", nút "Mô tả sai".
- Chế độ chờ duyệt cho bài của chiến dịch Full Media.
- Video; chỉ xử lý ảnh (`image/*`).
- Sinh ảnh AI mới. Ảnh luôn là ảnh có sẵn trên Drive.

## 4. Kiến trúc tổng quan

```
Drive tree (MediaAsset) ──► Chọn ảnh kế tiếp (chưa đánh giá/chưa dùng)
        │
        ▼
Vision A  ──► mô tả tiếng Anh có cấu trúc (lưu đệm theo ảnh)
        │            └─ đối chiếu tag cũ; unclear → thử model khác → Unusable
        ▼
IPageFitScorer (Jev) ── state = mô tả; mỗi page = 1 câu hỏi noul (hồ sơ page tiếng Anh)
        │ điểm = xác suất "có"
        ├─ không page nào ≥ 94%  ──► Rejected
        ▼
Vision B (model khác) ──► mô tả độc lập ──► Jev chấm lại (lượt hai)
        │ chỉ page ≥ 94% ở CẢ HAI lượt mới được nhận
        ▼
Sinh caption cho từng page (GenerateCaptionForPageAsync, tiếng Việt)
        ▼
Tạo Post (Scheduled, CampaignId, CampaignSlotAt) cho từng page đạt
```

Thành phần mới đều nằm trong module `Campaign` (hoặc thư mục con `Campaign/FullMedia`), dùng lại
`MediaIntelligenceService`, `PromptContextDefaults`, `PostModel`, `PostMediaModel`,
`CampaignGenerationService` (khe giờ, độ lệch, ràng buộc duy nhất khe).

## 5. Dữ liệu mới

Tất cả không FK constraint (theo quy ước dự án); migration đủ cặp `.cs` + `.Designer.cs`.

- `PageContextEnglishModel` (1 dòng/page): `SocialChannelId`, `ProfileEnglishJson`,
  `SourceHash` (băm các trường tiếng Việt đã dịch), `Status` (Pending/Translated/Failed),
  `FailureReason`, `Attempts`, `TranslatedAt`, `Model`. Không có DTO nào trả bảng này ra ngoài.
- `MediaVisionProfileModel` (1 dòng/ảnh/model): `MediaAssetId`, `FileHash`, `Pass` (A/B),
  `DescriptionEnglishJson` (chủ đề, đối tượng, tông, bối cảnh, chữ trong ảnh kèm bản dịch),
  `Confidence`, `Unclear`, `Model`, `CreatedAt`.
- `CampaignMediaEvaluationModel` (chiến dịch × ảnh): `Status`
  (Pending/Scoring/Verifying/Accepted/Rejected/Unusable/Failed), `ReasonCode`, `BestScore`,
  `BestChannelId`, `Attempts`, `NextAttemptAt`, `EvaluatedAt`, `InputHash` (băm đầu vào chấm điểm).
- `CampaignMediaScoreModel` (chiến dịch × ảnh × page × lượt): `Score`, `Passed`, `ScoredAt`.
- `CampaignModel` thêm: `Mode` (Recycle=1 mặc định, FullMedia=2), `SourceFolderId` (Guid?),
  `QualityPausedAt`/`QualityPauseReason` (cầu chì chất lượng).
- Bài sinh ra dùng `GenerationFlow` mới `FullMedia` (không tái dùng giá trị 3/4 để không lẫn với
  logic bài nguồn của `RecycleSourcePicker`), `SourcePostId = null`, ảnh gắn qua `PostMedia`.

## 6. Hành vi chi tiết

### 6.1 Dịch hồ sơ page (D8)
- Trường dịch: `BrandName`, `ToneOfVoice`, `CtaText`, chủ đề chính, nội dung cấm (xem mục 12 — giả
  định cần xác nhận). Tên riêng (VNI, TOEIC…) giữ nguyên trong prompt dịch.
- **Backfill một lần**: lệnh quản trị chạy một lần, bỏ qua page đã dịch (idempotent).
- **Tăng dần**: khi tạo/sửa `PageContext`, so `SourceHash`; đổi thì xếp dịch lại **chỉ page đó**;
  không đổi (ví dụ chỉ sửa màu) thì không dịch. Dịch chạy nền, không chặn lưu hồ sơ.
- Lỗi: tự thử lại tăng dần (1 phút, 10 phút, 1 giờ, rồi mỗi chu kỳ); phân loại lỗi tạm/vĩnh viễn
  (`timeout`, `provider_error`, `empty_profile`, `invalid_response`); lỗi vĩnh viễn dừng, chờ bản
  tiếng Việt được sửa. Thông báo Admin/ContentManager qua hệ thống thông báo sẵn có.
- Danh sách hồ sơ page chỉ hiện chip trạng thái (Đã dịch/Đang dịch/Lỗi) và nút "Dịch lại"
  (chỉ kích hoạt dịch, không lộ nội dung).
- Page chưa dịch/bản dịch lỗi thời **không tham gia chấm điểm**; không dùng bản dịch cũ.
  Chiến dịch bỏ qua page đó trong lượt chạy. Nếu **tất cả** page đều không chấm được thì tạm dừng
  lượt chạy (không dừng chiến dịch) và tự chạy lại khi có bản dịch.

### 6.2 Mô tả ảnh (Vision)
- Prompt cố định, đầu ra JSON có schema; ảnh đọc từ storage với giới hạn dung lượng.
- Lưu đệm theo `FileHash`; nhiều chiến dịch dùng chung, không gọi lại.
- Đối chiếu tự động với `Tags`/`Description` đã có của `MediaAsset`: lệch hẳn → chạy lại bằng model
  dự phòng; vẫn lệch → coi là không chắc chắn, không đăng.
- `Unclear` hoặc `Confidence` thấp → thử model khác; sau số lần tối đa → `Unusable` (không bao giờ
  đăng).

### 6.3 Chấm điểm (Jev)
- Giao diện `IPageFitScorer` (cài đặt đầu tiên: `JevPageFitScorer`), thay được bằng model
  khác mà không đổi pipeline.
- Một request Jev tới Vercel AI Gateway (D11): `state` = mô tả ảnh (tiếng Anh), `questions` = một
  câu `noul` cho mỗi page ứng viên, dựng từ `ProfileEnglishJson`; quá 64 page thì tách request.
  Model `typesafe-ai/jev`. Điểm của page = xác suất "có" (0–1). Đạt khi **≥ 0,94**.
- Chi phí mỗi request được đọc từ `provider_metadata.gateway.cost` và cộng vào bộ đếm chi phí
  theo ngày (dùng cho trần chi phí ở mục 8). Lỗi 429 thì tôn trọng `Retry-After` và exponential
  backoff; lỗi 401/402/403 là lỗi vĩnh viễn: dừng và thông báo, không thử dồn dập.
- **Không bắn loạt request thử**: có báo cáo team bị Gateway chặn vì "nghi lạm dụng" sau khoảng 20
  request thử. Worker và bước kiểm chuẩn phải giãn nhịp (tuần tự, có khoảng nghỉ).
- Lượt hai (Vision B + Jev lần nữa) chỉ chạy cho ảnh có ít nhất một page đạt ở lượt một. Page được
  nhận khi đạt ở **cả hai lượt**.

### 6.4 Ảnh bị loại và tự chấm lại
- `Rejected` chỉ đặt khi ảnh **thực sự được chấm** và không page nào đạt. Nếu không chấm được
  (chưa dịch, Jev lỗi) thì giữ `Pending`, không loại oan.
- Tự đưa về hàng chờ khi `InputHash` đổi: hồ sơ page đổi và dịch xong, chiến dịch thêm
  kênh/nhóm kênh, mô tả ảnh được tạo lại. Chỉ chấm lại page bị ảnh hưởng, mô tả ảnh đã lưu.
- Không tự chấm lại vô hạn: mỗi ảnh có trần số lần chấm lại và khoảng nghỉ.

### 6.5 Tạo bài
- Với mỗi page được nhận: `GenerateCaptionForPageAsync` (ảnh + `PageContext` tiếng Việt, dùng lại
  retry/chuẩn hoá hashtag/CTA). Lỗi caption của một page chỉ ảnh hưởng bài của page đó.
- Bài: `Status = Scheduled`, `ApprovedBy = campaign-worker`, `CampaignId`, `CampaignSlotAt`,
  `ScheduledPublishAt` = khe giờ + độ lệch (dùng `BuildSlots`/`ApplyJitter` hiện có).
- Chống trùng: một ảnh không đăng lại trên cùng một page; ảnh đã dùng không vào đánh giá lại.
  Giữ ràng buộc duy nhất khe giờ của chiến dịch hiện tại.

## 7. Tự kiểm tra chất lượng (D10)

1. **Đồng thuận hai model** (mục 6.3): nhận nhầm bị chặn ở tầng này.
2. **Đối chiếu tag cũ** (mục 6.2).
3. **Ảnh `unclear` tự xử lý** (mục 6.2) → `Unusable`.
4. **Kiểm chuẩn tự động bằng dữ liệu lịch sử**: định kỳ lấy mẫu ảnh đã đăng **riêng ở một page**
   (bỏ ảnh từng đăng đồng loạt lên nhiều page), chạy qua pipeline, đo (a) page thật có nằm trong
   nhóm điểm cao nhất không, (b) tỉ lệ ảnh được chấm đạt cho page không liên quan. Nhãn là nhãn
   yếu nên chỉ dùng chỉ số tương đối. Chỉ số dưới mức cấu hình → đặt `QualityPausedAt`, chiến dịch
   Full Media tự tạm dừng lượt chạy, gửi thông báo; tự chạy lại khi chỉ số phục hồi.
5. **Cầu chì bất thường**: tỉ lệ nhận gần 0% hoặc gần 100% trong một cửa sổ, hoặc Vision/Jev lỗi
   liên tiếp → tự tạm dừng lượt chạy và thông báo.

Giới hạn đã biết: tự động hoàn toàn không loại hết sai sót; hai lớp bảo vệ chính là mục 1 và 4.

## 8. Ràng buộc cô lập worker (D9)

Worker mới (`CampaignFullMediaWorker`, cùng khuôn `CampaignWorker`/`PostGenerationWorker`) và
worker dịch hồ sơ page phải thoả:
- Cờ `Enabled` **mặc định false**; chu kỳ tối thiểu 15 giây; trễ khởi động như `CampaignWorker`.
- Mỗi vòng tạo scope riêng; mọi lỗi bị bắt, vòng lặp không chết; dừng gọn theo `stoppingToken`.
- Mỗi cuộc gọi ngoài (Vision, Jev, dịch) có **timeout riêng**; `HttpClient` Jev riêng.
- **Ngắt mạch**: lỗi liên tiếp quá ngưỡng thì nghỉ một khoảng.
- **Không giữ giao dịch/kết nối DB khi chờ AI** (SQLite chỉ một người ghi): đọc → thả → gọi AI →
  ghi bằng giao dịch ngắn, lưu theo từng ảnh; mỗi ảnh một `DbContext`.
- Nhận việc bằng cập nhật nguyên tử (`ExecuteUpdateAsync`, theo mẫu tin nhắn hẹn giờ) để chạy hai
  bản app không xử lý trùng.
- Tuần tự một ảnh mỗi lúc; **trần số ảnh mỗi vòng và mỗi ngày** (giới hạn tải và chi phí AI); ảnh
  đọc vào bộ nhớ có giới hạn dung lượng và giải phóng ngay.
- Khi khởi động lại, mục kẹt ở trạng thái đang xử lý được đưa về chờ.

## 9. API (chỉ đọc cho phần mới)

Trong `CampaignController`, `[Authorize(Roles="Admin,ContentManager")]` trên **từng action**
(convention 951a0d11):
- `GET api/Campaign/{id}/media-evaluations` — phân trang, lọc theo trạng thái; trả ảnh, trạng thái,
  điểm cao nhất, page cao nhất.
- `GET api/Campaign/{id}/media-evaluations/{mediaId}` — mô tả tiếng Anh của ảnh và điểm từng page.
- `GET api/Campaign/{id}/pipeline-health` — trạng thái cầu chì, số page chưa dịch.
- Tạo/sửa chiến dịch nhận thêm `mode`, `sourceFolderId`. `sourceFolderId` phải là thư mục thuộc cây
  Drive, kiểm bằng `MediaFolderRepository.IsInGoogleDriveTreeAsync` (đã có, dùng bởi
  `MediaCaptionJobService`); sai thì trả 400.
- Không endpoint nào trả `ProfileEnglishJson`.

## 10. Giao diện (ClientApp, module campaigns)

- `CampaignFormModal.jsx`: ô **Chế độ**; Full Media hiện bộ chọn thư mục Drive nguồn và ẩn ô loại
  media/chiến lược ảnh. Giữ lịch, khung giờ, độ lệch, kênh, nhóm kênh.
- `CampaignDetailPage.jsx`: tab **Ảnh** (chỉ xem): số ảnh theo trạng thái, danh sách có ảnh thu
  nhỏ/trạng thái/điểm/page cao nhất, mở rộng xem mô tả tiếng Anh và điểm từng page, lọc trạng
  thái; dải thông báo tình trạng pipeline.
- Danh sách hồ sơ page: chip trạng thái dịch + nút "Dịch lại".
- Tuân thủ: icon qua `Icon.jsx` (lucide), không emoji, không CSS class mồ côi.

## 11. Cấu hình

`FullMediaPipeline` (Enabled=false, IntervalSeconds, MinFitScore=0.94, MaxImagesPerTick,
MaxImagesPerDay, MaxDailyCostUsd, trần số lần chấm lại, ngưỡng cầu chì/kiểm chuẩn) và `Jev`
(BaseUrl=`https://ai-gateway.vercel.sh/typesafe`, Model=`typesafe-ai/jev`, ZeroDataRetention=true,
timeout; `ApiKey` đọc từ biến môi trường `AI_GATEWAY_API_KEY` hoặc `appsettings.Production.json`,
**không bao giờ vào file được commit**; log phải redact theo convention c125649e). Thêm mục vào `appsettings.json` chỉ với
giá trị rỗng/mặc định an toàn.

## 12. Giả định cần xác nhận khi duyệt spec

1. Hồ sơ page cần thêm hai trường **chủ đề chính** và **nội dung cấm** (hiện chưa có trong
   `PageContextModel`) để điểm có nghĩa. Nếu không muốn thêm trường, chất lượng chấm điểm sẽ thấp.
2. "Loại ảnh" = đánh dấu trong bảng đánh giá, không xoá/di chuyển ảnh trên Drive.
3. Danh sách page chấm điểm = kênh lẻ ∪ thành viên nhóm kênh của chiến dịch (cơ chế
   `ResolveRunningChannelIdsAsync` hiện có), chỉ các page dùng được cho ảnh.
4. Model Vision B (khác Vision A) và mức chỉ số của kiểm chuẩn/cầu chì sẽ chốt bằng một lần chạy
   thử với ảnh/hồ sơ thật trước khi bật `Enabled` trên production.
5. Định dạng `noul` qua Gateway đã đọc từ tài liệu Vercel (cập nhật 2026-10-05); cần **một**
   request thử nhỏ (chỉ gửi chữ, không gửi ảnh) để xác nhận trước khi viết bộ chấm điểm. Giá
   khoảng $0,042 cho 1 triệu token đầu vào (output miễn phí) lấy từ nguồn thứ cấp, cần đối chiếu
   với trang Models của Vercel khi tạo tài khoản. Cần tài khoản Vercel và `AI_GATEWAY_API_KEY`.
6. Dữ liệu gửi Jev chỉ là mô tả ảnh và hồ sơ page (không có ảnh, không có dữ liệu khách).

## 13. Kiểm thử (mọi test phải đỏ khi hoàn nguyên code)

Backend (xUnit, HTTP TestServer): ngưỡng biên 0,939/0,94; đồng thuận hai lượt; một ảnh nhiều page
tạo bài cùng khe giờ + độ lệch; không đăng lặp ảnh trên cùng page; Rejected không chấm lại khi
`InputHash` không đổi, tự chấm lại khi hồ sơ page/kênh đổi; chưa dịch → Pending (không Rejected);
dịch một lần + dịch tăng dần theo `SourceHash`; lỗi dịch tạm/vĩnh viễn; cầu chì và kiểm chuẩn tự
dừng; tag lệch → model dự phòng → Unusable; cô lập worker (lỗi một ảnh, timeout, ghi đồng thời của
người dùng, `Enabled=false` thì không gọi AI, mục kẹt được đưa về chờ); quyền 403 cho
Viewer/Reviewer trên mọi endpoint mới; không endpoint nào lộ `ProfileEnglishJson`;
`dotnet ef migrations has-pending-model-changes` sạch.

Frontend (vitest): form đổi theo chế độ; tab Ảnh hiển thị đúng; test quét CSS mồ côi và quét emoji.

Thủ công (người triển khai chạy, không phải người dùng): chạy app, tạo chiến dịch Full Media với
vài ảnh Drive mẫu, kiểm bằng trình duyệt thật.

## 14. Quản trị dự án và tách task

PCS requirement mới `FULLMEDIA-01` (invariant + AC đo được; `independent_review=required` cho quyền,
ranh giới dữ liệu, tích hợp AI bên ngoài). Plan, mỗi task có phạm vi ghi riêng:
t1 dịch hồ sơ page · t2 mô tả ảnh (Vision) + lưu đệm · t3 `IPageFitScorer` + Jev · t4 worker
pipeline + tạo bài · t5 kiểm chuẩn tự động + cầu chì · t6 API · t7 giao diện · t8 review độc lập.
t3 phụ thuộc kết quả request thử Jev (mục 12.5).

## 15. Rủi ro

- Phụ thuộc Jev qua Vercel AI Gateway (dịch vụ còn mới, tài liệu cập nhật tháng 10/2026): có báo
  cáo tài khoản bị chặn vì "nghi lạm dụng" không gỡ được; giảm thiểu bằng việc giãn nhịp request,
  `zeroDataRetention`, giao diện `IPageFitScorer` (đổi được sang Decision-1/d1/Laya hoặc LLM có
  logprobs) và việc chỉ gửi văn bản. Chưa lấy được giá/ID của các model quyết định khác.
- Tiếng Việt: giảm thiểu bằng việc dùng tiếng Anh cho phần gửi Jev (D7).
- Tự động hoàn toàn: có thể nhận nhầm; giảm thiểu bằng mục 7, không loại bỏ được hoàn toàn.
- Nhãn kiểm chuẩn là nhãn yếu từ lịch sử đăng.
