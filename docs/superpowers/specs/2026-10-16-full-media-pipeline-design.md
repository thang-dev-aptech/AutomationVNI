# Full Media Pipeline cho Chiến dịch — Thiết kế

Ngày: 2026-10-16 · Nhánh: Implement-CRM · Trạng thái: bản 2 (đổi bộ chấm điểm sang LLM có logprobs,
bỏ bước dịch hồ sơ page) — chờ người dùng duyệt lại

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
| D6 | Cách chấm điểm = **cách A**: Vision mô tả ảnh một lần (có cấu trúc, lưu đệm), sau đó một **LLM chỉ đọc văn bản** chấm từng page. Ảnh **không** gửi cho bộ chấm điểm, chỉ gửi văn bản. |
| D7 | Mô tả ảnh và hồ sơ page đều bằng **tiếng Việt** (**bỏ bước dịch sang tiếng Anh**, vì lý do dịch chỉ là Jev chưa hỗ trợ tiếng Việt). |
| D9 | Worker **không được ảnh hưởng** tiến trình khác của app (mục 8). |
| D10 | **Không có thao tác con người** trong quá trình (không duyệt tay, không nút đánh giá lại, không gán nhãn). Hệ thống tự kiểm tra chính nó (mục 7). |
| D12 | Bộ chấm điểm là **LLM OpenAI-compatible có `logprobs`**, dùng **cùng provider đang cấu hình cho Vision** (`AiProviders:DefaultProvider`). Jev/Vercel AI Gateway bị loại: Gateway yêu cầu thẻ, ZDR chỉ có gói Pro, Jev không nằm trong gói miễn phí (đã thử thật: 403 ba lần). |

(D8 và D11 cũ — dịch hồ sơ page, Jev qua Vercel AI Gateway — đã bị thay thế bởi D7 và D12.)

## 3. Ngoài phạm vi (bản đầu)

- Ghi đè ngưỡng bằng tay, hàng chờ xem tay, nút "Đánh giá lại", nút "Mô tả sai".
- Chế độ chờ duyệt cho bài của chiến dịch Full Media.
- Video; chỉ xử lý ảnh (`image/*`). Không sinh ảnh AI mới.
- Dịch hồ sơ page; Jev; Vercel AI Gateway.

## 4. Kiến trúc tổng quan

```
Drive tree (MediaAsset) ──► Chọn ảnh kế tiếp (chưa đánh giá/chưa dùng)
        │
        ▼
Vision A ──► mô tả tiếng Việt có cấu trúc (lưu đệm theo ảnh)
        │       └─ đối chiếu tag cũ; unclear → thử model khác → Unusable
        ▼
IPageFitScorer (LlmLogprobPageFitScorer): mỗi page một request chat completion,
   trả đúng 1 token YES/NO, logprobs=true → điểm = P(YES)/(P(YES)+P(NO))
        ├─ không page nào ≥ 94%  ──► Rejected
        ▼
Vision B (model khác) ──► mô tả độc lập ──► chấm lại (lượt hai)
        │ chỉ page ≥ 94% ở CẢ HAI lượt mới được nhận
        ▼
Sinh caption cho từng page (caption theo PageContext, tiếng Việt)
        ▼
Tạo Post (Scheduled, CampaignId, CampaignSlotAt) cho từng page đạt
```

Thành phần mới nằm trong `backend/Modules/Campaign/FullMedia/`, dùng lại `MediaIntelligenceService`,
`PromptContextDefaults`, `PostModel`, `PostMediaModel`, `CampaignGenerationService` (khe giờ, độ lệch,
ràng buộc duy nhất khe).

## 5. Dữ liệu mới

Tất cả không FK constraint (theo quy ước dự án); migration đủ cặp `.cs` + `.Designer.cs`.

- `PageContextModel` thêm `string? Topics` (chủ đề chính của page) và `string? Exclusions` (nội dung
  không đăng) — không có hai trường này điểm chấm kém chính xác (giả định 12.1).
- `MediaVisionProfileModel` (1 dòng/ảnh/lượt): `MediaAssetId`, `FileHash`, `Pass` (A/B),
  `DescriptionJson` (chủ đề, đối tượng, tông, bối cảnh, chữ đọc được trong ảnh, từ khoá — tiếng
  Việt), `Confidence`, `Unclear`, `Model`, `CreatedAt`.
- `CampaignMediaEvaluationModel` (chiến dịch × ảnh): `Status`
  (Pending/Scoring/Verifying/Accepted/Rejected/Unusable/Failed), `ReasonCode`, `BestScore`,
  `BestChannelId`, `Attempts`, `NextAttemptAt`, `EvaluatedAt`, `InputHash`, `CostUsd`.
- `CampaignMediaScoreModel` (chiến dịch × ảnh × page × lượt): `Score`, `Passed`, `ScoredAt`.
- `CampaignModel` thêm: `Mode` (Recycle=1 mặc định, FullMedia=2), `SourceFolderId` (Guid?),
  `QualityPausedAt`, `QualityPauseReason` (cầu chì chất lượng).
- Bài sinh ra dùng `GenerationFlow` mới `FullMedia` (không tái dùng giá trị 3/4 để không lẫn với
  logic bài nguồn của `RecycleSourcePicker`), `SourcePostId = null`, ảnh gắn qua `PostMedia`.

## 6. Hành vi chi tiết

### 6.1 Hồ sơ page dùng để chấm điểm
- `PageProfileSnapshot.Build(PageContextModel)` dựng văn bản tiếng Việt từ `BrandName`,
  `ToneOfVoice`, `CtaText`, `Topics`, `Exclusions` (không dùng màu, hotline, website) và `ProfileHash`
  băm đúng các trường đó. Đổi trường không liên quan (màu, hotline) không đổi `ProfileHash`.
- Hồ sơ rỗng (không có `Topics` và `ToneOfVoice`) thì page **không chấm được**: bị bỏ qua trong lượt,
  chiến dịch báo "n page thiếu hồ sơ".

### 6.2 Mô tả ảnh (Vision)
- Prompt cố định, đầu ra JSON theo schema bằng tiếng Việt; ảnh đọc từ storage với giới hạn dung
  lượng, giải phóng ngay.
- Lưu đệm theo `FileHash`; nhiều chiến dịch dùng chung, không gọi lại.
- Đối chiếu tự động với `Tags`/`Description` đã có của `MediaAsset`: lệch hẳn → chạy lại bằng model
  dự phòng; vẫn lệch → coi là không chắc chắn, không đăng.
- `Unclear` hoặc `Confidence` thấp → thử model khác; sau số lần tối đa → `Unusable` (không bao giờ
  đăng).

### 6.3 Chấm điểm (LLM có logprobs)
- Giao diện `IPageFitScorer` (cài đặt: `LlmLogprobPageFitScorer`), thay được bằng model/provider
  khác mà không đổi pipeline.
- Mỗi page một request chat completion tới provider của Vision (`AiProviders:DefaultProvider`), có
  thể ghi đè model bằng `PageFit:Model`: system prompt cố định + mô tả ảnh + hồ sơ page, hỏi ảnh có
  phù hợp page không, **chỉ trả `YES` hoặc `NO`**, `max_tokens=1`, `temperature=0`, `logprobs=true`,
  `top_logprobs=5`.
- Điểm = P(YES)/(P(YES)+P(NO)) tính từ xác suất của các token `YES`/`NO` (không phân biệt hoa
  thường, bỏ khoảng trắng) trong `top_logprobs`; không thấy token nào trong hai token đó thì lỗi tạm.
- **Fail closed:** provider không trả `logprobs` (hoặc trả rỗng) là lỗi cấu hình **vĩnh viễn**: ảnh
  giữ `Pending`, thông báo Admin/ContentManager, **không** quay sang dùng điểm model tự khai.
- Đạt khi điểm **≥ 0,94**. Lượt hai (Vision B + chấm lại) chỉ chạy cho ảnh có ít nhất một page đạt ở
  lượt một; page được nhận khi đạt ở **cả hai lượt**.
- Request tuần tự, có khoảng nghỉ, timeout và retry riêng (429 thì tôn trọng `Retry-After`); chi phí
  ước tính từ `usage` của response nhân đơn giá cấu hình, cộng vào bộ đếm theo ngày.

### 6.4 Ảnh bị loại và tự chấm lại
- `Rejected` chỉ đặt khi ảnh **thực sự được chấm** và không page nào đạt. Nếu không chấm được (hồ sơ
  rỗng, provider lỗi, thiếu logprobs) thì giữ `Pending`, không loại oan.
- Tự đưa về hàng chờ khi `InputHash` đổi (băm `ProfileHash` của page ứng viên + băm mô tả): hồ sơ
  page đổi, chiến dịch thêm kênh/nhóm kênh, mô tả ảnh tạo lại. Chỉ chấm lại page bị ảnh hưởng, mô tả
  ảnh đã lưu, không gọi lại Vision.
- Không tự chấm lại vô hạn: mỗi ảnh có trần số lần chấm lại và khoảng nghỉ.

### 6.5 Tạo bài
- Với mỗi page được nhận: sinh caption riêng cho page (`GenerateCaptionTextForPageAsync`, không ghi
  vào `MediaAsset`). Lỗi caption của một page chỉ ảnh hưởng bài của page đó.
- Bài: `Status = Scheduled`, `ApprovedBy = campaign-worker`, `CampaignId`, `CampaignSlotAt`,
  `ScheduledPublishAt` = khe giờ + độ lệch (dùng `BuildSlots`/`ApplyJitter` hiện có).
- Chống trùng: một ảnh không đăng lại trên cùng một page; ảnh đã dùng không vào đánh giá lại. Giữ
  ràng buộc duy nhất khe giờ của chiến dịch hiện tại.

## 7. Tự kiểm tra chất lượng (D10)

1. **Đồng thuận hai model Vision** (mục 6.3): nhận nhầm bị chặn ở tầng này.
2. **Đối chiếu tag cũ** (mục 6.2).
3. **Ảnh `unclear` tự xử lý** (mục 6.2) → `Unusable`.
4. **Kiểm chuẩn tự động bằng dữ liệu lịch sử**: định kỳ lấy mẫu ảnh đã đăng **riêng ở một page**
   (bỏ ảnh từng đăng đồng loạt lên nhiều page), chạy qua pipeline, đo (a) page thật có nằm trong
   nhóm điểm cao nhất không, (b) tỉ lệ ảnh được chấm đạt cho page không liên quan. Nhãn là nhãn
   yếu nên chỉ dùng chỉ số tương đối. Điểm LLM thường quá tự tin, nên bước này đặc biệt quan trọng:
   nó kiểm tra ngưỡng 94% có thật sự phân biệt được hay không. Chỉ số dưới mức cấu hình → đặt
   `QualityPausedAt`, chiến dịch Full Media tự tạm dừng lượt chạy, gửi thông báo; tự chạy lại khi
   chỉ số phục hồi.
5. **Cầu chì bất thường**: tỉ lệ nhận gần 0% hoặc gần 100% trong một cửa sổ, hoặc Vision/chấm điểm
   lỗi liên tiếp → tự tạm dừng lượt chạy và thông báo.

Giới hạn đã biết: tự động hoàn toàn không loại hết sai sót; hai lớp bảo vệ chính là mục 1 và 4.

## 8. Ràng buộc cô lập worker (D9)

Worker mới (`CampaignFullMediaWorker`, cùng khuôn `CampaignWorker`/`PostGenerationWorker`) phải thoả:
- Cờ `Enabled` **mặc định false**; chu kỳ tối thiểu 15 giây; trễ khởi động như `CampaignWorker`.
- Mỗi vòng tạo scope riêng; mọi lỗi bị bắt, vòng lặp không chết; dừng gọn theo `stoppingToken`.
- Mỗi cuộc gọi ngoài (Vision, chấm điểm, caption) có **timeout riêng**.
- **Ngắt mạch**: lỗi liên tiếp quá ngưỡng thì nghỉ một khoảng.
- **Không giữ giao dịch/kết nối DB khi chờ AI** (SQLite chỉ một người ghi): đọc → thả → gọi AI →
  ghi bằng giao dịch ngắn, lưu theo từng ảnh; mỗi ảnh một `DbContext`.
- Nhận việc bằng cập nhật nguyên tử (`ExecuteUpdateAsync`, theo mẫu tin nhắn hẹn giờ) để chạy hai
  bản app không xử lý trùng.
- Tuần tự một ảnh mỗi lúc; **trần số ảnh mỗi vòng và mỗi ngày**, **trần chi phí mỗi ngày** (giới hạn tải
  và chi phí AI); ảnh đọc vào bộ nhớ có giới hạn dung lượng và giải phóng ngay.
- Khi khởi động lại, mục kẹt ở trạng thái đang xử lý được đưa về chờ.

## 9. API (chỉ đọc cho phần mới)

Trong `CampaignController`, `[Authorize(Roles="Admin,ContentManager")]` trên **từng action**
(convention 951a0d11):
- `GET api/Campaign/{id}/media-evaluations` — phân trang, lọc theo trạng thái; trả ảnh, trạng thái,
  điểm cao nhất, page cao nhất.
- `GET api/Campaign/{id}/media-evaluations/{mediaId}` — mô tả tiếng Việt của ảnh và điểm từng page.
- `GET api/Campaign/{id}/pipeline-health` — trạng thái cầu chì, số page thiếu hồ sơ, chi phí hôm nay.
- Tạo/sửa chiến dịch nhận thêm `mode`, `sourceFolderId`. `sourceFolderId` phải là thư mục thuộc cây
  Drive, kiểm bằng `MediaFolderRepository.IsInGoogleDriveTreeAsync` (đã có, dùng bởi
  `MediaCaptionJobService`); sai thì trả 400.

## 10. Giao diện (ClientApp, module campaigns và page-contexts)

- `CampaignFormModal.jsx`: ô **Chế độ**; Full Media hiện bộ chọn thư mục Drive nguồn và ẩn ô loại
  media/chiến lược ảnh. Giữ lịch, khung giờ, độ lệch, kênh, nhóm kênh.
- `CampaignDetailPage.jsx`: tab **Ảnh** (chỉ xem): số ảnh theo trạng thái, danh sách có ảnh thu
  nhỏ/trạng thái/điểm/page cao nhất, mở rộng xem mô tả và điểm từng page, lọc trạng thái; dải thông
  báo tình trạng pipeline.
- `PageContextFormModal.jsx`: thêm hai trường **Chủ đề chính** và **Nội dung cấm**.
- Tuân thủ: icon qua `Icon.jsx` (lucide), không emoji, không CSS class mồ côi.

## 11. Cấu hình

`FullMediaPipeline` (Enabled=false, IntervalSeconds, MinFitScore=0.94, MaxImagesPerTick,
MaxImagesPerDay, MaxDailyCostUsd, trần số lần chấm lại, ngưỡng cầu chì/kiểm chuẩn, VisionModelA,
VisionModelB, VisionFallbackModel) và `PageFit` (Model tuỳ chọn ghi đè, TimeoutSeconds, TopLogprobs=5,
MinDelayBetweenRequestsMs, MaxRetries, đơn giá token để ước tính chi phí). **Không có khoá API mới**:
dùng khoá của `AiProviders` hiện có; không thêm khoá nào vào file được commit.

## 12. Giả định cần xác nhận

1. Hồ sơ page thêm hai trường **chủ đề chính** và **nội dung cấm**.
2. "Loại ảnh" = đánh dấu trong bảng đánh giá, không xoá/di chuyển ảnh trên Drive.
3. Danh sách page chấm điểm = kênh lẻ ∪ thành viên nhóm kênh của chiến dịch
   (`ResolveRunningChannelIdsAsync`), chỉ các page dùng được cho ảnh.
4. **Provider hiện tại phải trả `logprobs`.** Chưa biết `vietai` (apithat.dev, model `gpt-6-sol`) có
   hỗ trợ hay không; cần một request thử chỉ chứa chữ trước khi viết bộ chấm điểm. Nếu không hỗ trợ
   thì đổi `PageFit` sang provider khác (ví dụ `openai`, tài liệu chính thức có logprobs) — cần thêm
   khoá cho provider đó.
5. Model Vision B (khác Vision A) và mức chỉ số của kiểm chuẩn/cầu chì chốt bằng một lần chạy thử với
   ảnh/hồ sơ thật trước khi bật `Enabled` trên production.
6. Dữ liệu gửi bộ chấm điểm chỉ là mô tả ảnh và hồ sơ page (không có ảnh, không có dữ liệu khách).

## 13. Kiểm thử (mọi test phải đỏ khi hoàn nguyên code)

Backend (xUnit, HTTP TestServer): ngưỡng biên 0,939/0,94; tính điểm từ logprobs đúng (cả khi chỉ
thấy một trong hai token); thiếu logprobs là lỗi vĩnh viễn, ảnh giữ Pending; đồng thuận hai lượt; một
ảnh nhiều page tạo bài cùng khe giờ + độ lệch; không đăng lặp ảnh trên cùng page; Rejected không chấm
lại khi `InputHash` không đổi, tự chấm lại khi hồ sơ page/kênh đổi; hồ sơ rỗng/provider lỗi → Pending
(không Rejected); `ProfileHash` đổi đúng khi đổi trường liên quan; cầu chì và kiểm chuẩn tự dừng; tag
lệch → model dự phòng → Unusable; cô lập worker (lỗi một ảnh, timeout, ghi đồng thời của người
dùng, `Enabled=false` thì không gọi AI, mục kẹt được đưa về chờ); quyền 403 cho Viewer/Reviewer trên
mọi endpoint mới; request tới bộ chấm điểm không chứa ảnh/data URL/dữ liệu khách;
`dotnet ef migrations has-pending-model-changes` sạch.

Frontend (vitest): form đổi theo chế độ; tab Ảnh hiển thị đúng; hai trường mới của hồ sơ page; test
quét CSS mồ côi và quét emoji.

Thủ công (người triển khai chạy, không phải người dùng): chạy app, tạo chiến dịch Full Media với vài
ảnh Drive mẫu, kiểm bằng trình duyệt thật; một request thử chỉ chứa chữ tới provider để xác nhận
`logprobs` (giả định 12.4).

## 14. Quản trị dự án và tách task

PCS requirement `FULLMEDIA-01` (invariant + AC đo được; `independent_review=required` cho quyền,
ranh giới dữ liệu, giao dịch DB/worker, tích hợp AI bên ngoài, migration). Plan, mỗi task có phạm vi ghi
riêng, nối tiếp:
t1 hồ sơ page (Topics/Exclusions + `PageProfileSnapshot`) · t2 mô tả ảnh (Vision) + lưu đệm ·
t3 `IPageFitScorer` + `LlmLogprobPageFitScorer` · t4 worker pipeline + tạo bài ·
t5 kiểm chuẩn tự động + cầu chì · t6 API · t7 giao diện · t8 review độc lập.
t3 phụ thuộc kết quả request thử logprobs (giả định 12.4).

## 15. Rủi ro

- **`logprobs` không có ở provider hiện tại**: giảm thiểu bằng fail closed (không đăng) và giao diện
  `IPageFitScorer`; có thể chuyển sang `openai` chỉ bằng cấu hình `PageFit`.
- **Điểm LLM thiếu hiệu chỉnh** (thường quá tự tin): giảm thiểu bằng đồng thuận hai model Vision và
  kiểm chuẩn tự động (mục 7); nếu kiểm chuẩn cho thấy ngưỡng 94% không phân biệt được thì pipeline tự
  tạm dừng chứ không đăng.
- **Nhiều request hơn Jev** (mỗi page một request): giảm thiểu bằng model nhỏ, trần mỗi vòng/ngày và
  trần chi phí.
- **Tự động hoàn toàn**: có thể nhận nhầm; giảm thiểu bằng mục 7, không loại bỏ được hoàn toàn.
- Nhãn kiểm chuẩn là nhãn yếu từ lịch sử đăng.
