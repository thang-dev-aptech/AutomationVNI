# Requirements

<!--
This file is kept in two-way sync with the Project Context MCP Server (FR16a, D12).

  * One `### R-NNN — title` heading per requirement.
  * Then one managed `<!-- req status=… files=… blocker=… -->` line.
  * Then free-form prose — yours; the server never reflows it.

`status` is one of: not-started | in-progress | blocked | done
(a missing `<!-- req … -->` line means not-started).

To add a requirement, write a new `### heading` with prose and run a sync — the
server assigns its `R-NNN` id and the metadata line. Titles and prose are yours
to edit; the `<!-- req … -->` line and the heading id belong to the server.
-->

### R-001 — Quản lý thư mục media theo kênh xã hội
<!-- req status=done files=_ai_agent/business/media_folder.md,backend/Modules/MediaFolder/MediaFolderController.cs,backend/Modules/MediaFolder/MediaFolderRepository.cs,backend/Modules/MediaFolder/MediaFolderDtos.cs,tests/Backend.Tests/Modules/MediaFolder/MediaFolderChildrenTests.cs,tests/Backend.Tests/Modules/MediaFolder/MediaFolderBreadcrumbSearchTests.cs,tests/Backend.Tests/Modules/MediaFolder/MediaFolderBulkCreateTests.cs -->

Business intent: giúp người vận hành tổ chức và tìm media theo cây thư mục trong phạm vi một SocialChannel/Page. Phạm vi: duyệt cây theo từng cấp, breadcrumb/tìm kiếm và tạo hierarchy hàng loạt; các thao tác phải bảo toàn ranh giới Page, không rò metadata và không tạo cây không hợp lệ. Actor: Admin, ContentManager, Reviewer, Viewer với quyền đọc; thao tác ghi bị giới hạn cho Admin/ContentManager. Dependency/execution order: chuẩn hóa ba task con độc lập MEDIA-01, MEDIA-02, MEDIA-03; các task dùng chung data boundary SocialChannel. Out of scope batch này: kéo-thả MediaAsset và PageContext logo picker. Nguồn: _ai_agent/business/media_folder.md; backend/Modules/MediaFolder/MediaFolderController.cs; backend/Modules/MediaFolder/MediaFolderRepository.cs; tests/Backend.Tests/Modules/MediaFolder; commits 75f307f, 850c745, 6a53385, 2f49e58.

### R-002 — Duyệt thư mục media trực tiếp theo Page
<!-- req status=done files=backend/Modules/MediaFolder/MediaFolderController.cs,backend/Modules/MediaFolder/MediaFolderRepository.cs,backend/Modules/MediaFolder/MediaFolderDtos.cs,tests/Backend.Tests/Modules/MediaFolder/MediaFolderChildrenTests.cs blocker=f085d3be-9eb0-47c4-a695-0b3c06487818 -->

Business intent: người dùng có quyền duyệt thư mục gốc hoặc con trực tiếp trong một SocialChannel/Page mà không tải toàn bộ descendants. Input: SocialChannelId bắt buộc, ParentFolderId tùy chọn, phân trang và sắp xếp. Expected behavior: trả đúng một cấp, tổng số folder, số con trực tiếp, số media trực tiếp và HasChildren; kết quả phân trang ổn định. Authorization/data boundary: Admin, ContentManager, Reviewer, Viewer được đọc nhưng chỉ trong Page được phép; Page không được phép được xử lý như không tồn tại và không lộ tên/ID/path. Failure: channel rỗng, channel/parent không tồn tại hoặc parent khác Page bị từ chối. Dependency: task con của epic quản lý thư mục media; là contract nền cho UI tree và count consistency. Nguồn: backend/Modules/MediaFolder/MediaFolderController.cs; backend/Modules/MediaFolder/MediaFolderRepository.cs; backend/Modules/MediaFolder/MediaFolderDtos.cs; tests/Backend.Tests/Modules/MediaFolder/MediaFolderChildrenTests.cs; commit 850c745; security boundary commit 2f49e58.

### R-003 — Tạo cây thư mục media hàng loạt nguyên tử
<!-- req status=done files=backend/Modules/MediaFolder/MediaFolderController.cs,backend/Modules/MediaFolder/MediaFolderRepository.cs,backend/Modules/MediaFolder/MediaFolderDtos.cs,tests/Backend.Tests/Modules/MediaFolder/MediaFolderBulkCreateTests.cs,tests/Backend.Tests/Modules/MediaFolder/MediaFolderBulkAuthPipelineTests.cs,tests/Backend.Tests/Backend.Tests.csproj blocker=f085d3be-9eb0-47c4-a695-0b3c06487818 -->

Business intent: Admin hoặc ContentManager tạo hoặc preview một hierarchy folder trong một SocialChannel/Page bằng clientRef/parentRef. Input: một SocialChannelId cho toàn batch, optional parent có sẵn, danh sách node, duplicate policy và ValidateOnly. Expected behavior: mọi node kế thừa cùng Page; mapping clientRef sang ID giữ đúng hierarchy; Error từ chối tên trùng, Skip tái sử dụng folder hiện có; preview không ghi dữ liệu. Transaction/data boundary: Page phải thuộc quyền actor; parent khác Page, cycle, reference sai, tên quá dài, batch >200 hoặc depth >10 bị từ chối; bất kỳ lỗi nào phải không lưu một phần batch. Authorization: chỉ Admin/ContentManager được gọi thao tác ghi. Dependency: task con của epic, phụ thuộc data boundary SocialChannel và validation hierarchy. Nguồn: backend/Modules/MediaFolder/MediaFolderController.cs; backend/Modules/MediaFolder/MediaFolderRepository.cs; backend/Modules/MediaFolder/MediaFolderDtos.cs; tests/Backend.Tests/Modules/MediaFolder/MediaFolderBulkCreateTests.cs; commit 6a53385; security boundary commit 2f49e58.

### R-004 — Tìm kiếm và breadcrumb thư mục trong Page
<!-- req status=done files=backend/Modules/MediaFolder/MediaFolderController.cs,backend/Modules/MediaFolder/MediaFolderRepository.cs,backend/Modules/MediaFolder/MediaFolderDtos.cs,tests/Backend.Tests/Modules/MediaFolder/MediaFolderBreadcrumbSearchTests.cs blocker=f085d3be-9eb0-47c4-a695-0b3c06487818 -->

Business intent: người dùng có quyền tìm folder theo tên và định vị folder trong hierarchy của một SocialChannel/Page. Input: SocialChannelId cùng keyword hoặc FolderId. Expected behavior: search hỗ trợ tiếng Việt có/không dấu, full path phân biệt tên trùng, counts nhất quán và phân trang ổn định; breadcrumb trả thứ tự từ root đến target. Data boundary: không đi xuyên Page; folder sai Page, chain hỏng hoặc truy cập Page không được phép trả not-found tương đương dữ liệu không tồn tại, không lộ metadata. Dependency: task con của epic; dùng semantics/counts từ duyệt thư mục một cấp. Nguồn: backend/Modules/MediaFolder/MediaFolderController.cs; backend/Modules/MediaFolder/MediaFolderRepository.cs; backend/Modules/MediaFolder/MediaFolderDtos.cs; tests/Backend.Tests/Modules/MediaFolder/MediaFolderBreadcrumbSearchTests.cs; commit 6a53385; security boundary commit 2f49e58.

### R-005 — Bảo toàn hierarchy khi thay đổi thư mục media
<!-- req status=done files=_ai_agent/business/media_folder.md,backend/Modules/MediaFolder/MediaFolderController.cs,backend/Modules/MediaFolder/MediaFolderRepository.cs,backend/Modules/MediaFolder/MediaFolderModel.cs,backend/Modules/MediaAsset/MediaAssetModel.cs blocker=f085d3be-9eb0-47c4-a695-0b3c06487818 -->

Business intent: Admin hoặc ContentManager có thể tạo, đổi thông tin/parent và xóa mềm folder mà không phá cây hoặc làm mất media. Input: create/update/delete một MediaFolder, optional parent và SocialChannel. Expected behavior: folder con kế thừa hoặc giữ cùng SocialChannel với parent; đổi parent không được tạo cycle; xóa folder còn children bị chặn; xóa folder không còn children chuyển media trực tiếp về trạng thái chưa phân loại thay vì xóa media. Authorization: thao tác create/update chỉ dành cho Admin/ContentManager theo controller hiện tại. Failure: parent thiếu, parent khác Page hoặc cycle bị từ chối; folder có children không được xóa. Dependency: task con của epic quản lý thư mục media; MEDIA-01/02/03 phụ thuộc cây hợp lệ và semantics media trực tiếp. Chưa có regression suite chuyên biệt được tìm thấy trong tests/Backend.Tests, nên giữ not-started để lập kế hoạch xác minh. Nguồn: _ai_agent/business/media_folder.md; backend/Modules/MediaFolder/MediaFolderController.cs; backend/Modules/MediaFolder/MediaFolderRepository.cs; commit 75f307f; current tree commit 2f49e58.

### R-006 — MEDIA-07: Tree lazy-load chỉ dùng chọn vị trí
<!-- req status=done files=ClientApp/src/modules/media/components/MediaFolderPickerTree.jsx,ClientApp/src/modules/media/components/MediaFolderFormModal.jsx,ClientApp/src/modules/media/hooks/useMediaFolders.js,ClientApp/src/modules/media/services/mediaFolderApi.js,ClientApp/src/modules/media/test/MediaFolderPickerTree.test.jsx blocker=f085d3be-9eb0-47c4-a695-0b3c06487818 -->

Tree không còn là navigation chính. Chỉ dùng trong modal chọn parent/move destination hoặc chế độ xem phụ; mỗi node chỉ tải children khi expand, không gọi endpoint tree toàn bộ. Phải giữ Page scope, trạng thái loading/error theo node và cấm chọn destination tạo cycle hoặc sai Page. Write scope component tree/picker riêng; integration owner xử lý file hooks/API chung. Dependency: MEDIA-01 và R-005 lifecycle; có thể chạy song song MEDIA-05/06 sau khi DTO ổn định. Nguồn plan: xác nhận trực tiếp của người dùng; current path ClientApp/src/modules/media/components/MediaFolderTree.jsx.

### R-007 — MEDIA-06: Frontend modal tạo folder hàng loạt
<!-- req status=in-progress files=ClientApp/src/modules/media/components/MediaFolderFormModal.jsx,ClientApp/src/modules/media/pages/MediaPage.jsx,ClientApp/src/modules/media/services/mediaFolderApi.js,ClientApp/src/modules/media/hooks/useMediaFolders.js,ClientApp/src/modules/media/test/MediaFolderFormModal.test.jsx,backend/Modules/MediaFolder/MediaFolderController.cs,backend/Modules/MediaFolder/MediaFolderRepository.cs,backend/Modules/MediaFolder/MediaFolderDtos.cs blocker=f085d3be-9eb0-47c4-a695-0b3c06487818 -->

Modal cho Admin/ContentManager nhập hoặc dựng nhiều folder cùng hierarchy, preview validation, chọn duplicate policy và submit nguyên tử theo Page hiện tại. Hiển thị lỗi theo node nhưng không báo thành công một phần; sau thành công refresh explorer đúng Page/parent. Write scope ưu tiên modal/component riêng; integration owner nối MediaPage/hooks/API. Dependency: MEDIA-03 và MEDIA-04. Nguồn plan: xác nhận trực tiếp của người dùng; current paths ClientApp/src/modules/media/components/MediaFolderFormModal.jsx, pages/MediaPage.jsx, services/mediaFolderApi.js, hooks/useMediaFolders.js.

### R-008 — MEDIA-05: Frontend tìm kiếm và truy cập nhanh
<!-- req status=done files=ClientApp/src/modules/media/components/MediaFolderSearchBox.jsx,ClientApp/src/modules/media/pages/MediaPage.jsx,ClientApp/src/modules/media/services/mediaFolderApi.js,ClientApp/src/modules/media/hooks/useMediaFolders.js,ClientApp/src/modules/media/test/MediaFolderSearchBox.test.jsx blocker=f085d3be-9eb0-47c4-a695-0b3c06487818 -->

Cho phép tìm folder toàn Page, hiển thị full path để phân biệt tên trùng và mở trực tiếp vị trí bằng breadcrumb/explorer; có truy cập nhanh tới folder gần đây hoặc thường dùng theo UX được thống nhất. Search không trộn Page và không giữ kết quả cũ khi đổi Page. Write scope ưu tiên component discovery riêng; integration owner nối vào MediaPage/hooks/API. Dependency: MEDIA-02 và MEDIA-04. Nguồn plan: xác nhận trực tiếp của người dùng; current paths ClientApp/src/modules/media/pages/MediaPage.jsx, services/mediaFolderApi.js, hooks/useMediaFolders.js.

### R-009 — MEDIA-04: Frontend Folder Explorer một cấp
<!-- req status=done files=ClientApp/src/modules/media/pages/MediaPage.jsx,ClientApp/src/modules/media/components/MediaFolderTree.jsx,ClientApp/src/modules/media/components/MediaFolderExplorer.jsx,ClientApp/src/modules/media/components/MediaFolderFormModal.jsx,ClientApp/src/modules/media/services/mediaFolderApi.js,ClientApp/src/modules/media/hooks/useMediaFolders.js,ClientApp/src/modules/media/hooks/useMediaFolderExplorer.js,ClientApp/src/modules/media/test/MediaFolderExplorer.test.jsx,ClientApp/src/modules/media/test/useMediaFolderExplorer.test.jsx,ClientApp/src/modules/media/test/MediaPage.explorer.test.jsx blocker=f085d3be-9eb0-47c4-a695-0b3c06487818 -->

Thay tree toàn bộ ở màn Media bằng Page-scoped Folder Explorer: chọn Page, breadcrumb, grid/list chỉ một cấp, điều hướng vào folder và phân trang. Tích hợp children API MEDIA-01 và breadcrumb MEDIA-02; không tải toàn bộ descendants. Write scope chính: MediaPage và component explorer mới; một integration owner chịu trách nhiệm file chung. Dependency: MEDIA-01 và breadcrumb contract MEDIA-02. Nguồn plan: xác nhận trực tiếp của người dùng; current paths ClientApp/src/modules/media/pages/MediaPage.jsx, components/MediaFolderTree.jsx, services/mediaFolderApi.js, hooks/useMediaFolders.js.

### R-010 — MEDIA-08: Kiểm thử tích hợp và hiệu năng 500 folder/Page
<!-- req status=in-progress files=tests/Backend.Tests/Modules/MediaFolder/MediaFolderScaleIntegrationTests.cs,tests/Backend.Tests/Modules/MediaAsset/MediaAssetMoveTests.cs,tests/Backend.Tests/Modules/MediaFolder/MediaFolderLifecycleTests.cs,ClientApp/src/modules/media blocker=f085d3be-9eb0-47c4-a695-0b3c06487818 -->

QA toàn luồng với ít nhất hai Page và 500 folder/Page: explorer một cấp, breadcrumb/search/full path, bulk create atomic, lazy tree request count/payload, authorization và không rò metadata. Hồi quy upload, move, create/update/delete folder và media chưa phân loại. Dependency: MEDIA-01..07; Wave 4 sau integration. Evidence cần automated tests/commands và independent review cho data boundary/performance findings. Nguồn plan: xác nhận trực tiếp của người dùng; backend tests hiện có dưới tests/Backend.Tests/Modules/MediaFolder; frontend test paths sẽ được tạo trong ClientApp.

### R-011 — BULK-CC-01: Tạo hàng loạt từ chứng chỉ không yêu cầu Ý tưởng
<!-- req status=done files=ClientApp/src/modules/bulk/pages/BulkChungChiPage.jsx,ClientApp/src/modules/bulk/pages/BulkChungChiPage.test.jsx,backend/Modules/Post/PostRepository.cs,backend/Modules/Post/PostDtos.cs -->

Người vận hành tạo bài từ ảnh chứng chỉ chỉ cần chọn Page, loại bài (tùy chọn) và chế độ ảnh; không nhập danh sách Ý tưởng. Mỗi Page được chọn tạo đúng một bài. Frontend duy trì contract backend hiện hữu bằng một title nội bộ ổn định, không hiển thị thành input. Nguồn: xác nhận trực tiếp của người dùng ngày 2026-09-21; ClientApp/src/modules/bulk/pages/BulkChungChiPage.jsx; backend/Modules/Post/PostRepository.cs.

### R-012 — BULK-CC-02: Chỉ cho chọn Page có ảnh trong folder chung_chi
<!-- req status=in-progress files=backend/Modules/MediaFolder/MediaFolderRepository.cs,backend/Modules/MediaFolder/MediaFolderController.cs,backend/Modules/Post/PostRepository.cs,ClientApp/src/modules/media/services/mediaFolderApi.js,ClientApp/src/modules/media/hooks/useMediaFolders.js,ClientApp/src/modules/bulk/pages/BulkChungChiPage.jsx,tests/Backend.Tests/Modules/MediaFolder/MediaFolderCrossPageReadsTests.cs,tests/Backend.Tests/Modules/Post/PostBulkCreateChungChiTests.cs,ClientApp/src/modules/bulk/pages/BulkChungChiPage.test.jsx -->

Màn hình Tạo hàng loạt từ chứng chỉ chỉ hiển thị Page actor có quyền quản lý, có folder gốc active, folder con trực tiếp tên chính xác chung_chi active và ít nhất một MediaAsset active có MimeType image/* nằm trực tiếp trong folder đó. Backend phải kiểm tra lại eligibility khi tạo để chặn request thủ công/stale. Nguồn: backend/Modules/GenerationJob/GenerationJobPipelineService.cs (ResolvePageSubfolderAsync, LoadFolderImageCandidateIdsAsync); xác nhận trực tiếp của người dùng ngày 2026-09-21.

### R-013 — DEPLOY-BE-01: Backend publish artifact chạy trực tiếp trên Linux production
<!-- req status=blocked files=backend/backend.csproj,backend/Properties/PublishProfiles/LinuxX64.pubxml,backend/README.md,docs/DEPLOYMENT.md -->

Backend phải được publish thành executable Linux tên `backend` để người vận hành có thể chạy từ `/home/vni/domains/auto.vni.edu.vn/publish_output` bằng `nohup env ASPNETCORE_URLS="http://127.0.0.1:5000" ASPNETCORE_ENVIRONMENT=Production ./backend > app.log 2>&1 &`. Phạm vi write: cấu hình publish/tài liệu backend và artifact build trong workspace nếu được tạo; không thay đổi business logic. Nguồn: xác nhận trực tiếp của người dùng ngày 2026-09-21.

### R-014 — CONTENT-CRAWL-02: Bài Pending chấm điểm lỗi phải được quét lại, không kẹt vĩnh viễn không điểm
<!-- req status=not-started files=backend/Modules/ContentCrawl/ContentCrawlPipelineService.cs,backend/Modules/ContentCrawl/CrawlScreenService.cs,backend/Modules/ContentCrawl/ContentCrawlWorker.cs,backend/Modules/ContentCrawl/ContentCrawlRepository.cs,backend/Modules/ContentCrawl/ContentCrawlOptions.cs -->

Hành vi hiện tại (xác minh qua DB snapshot vni_automation (1).db + code, 2026-09-22): khi CrawlScreenService.ScreenAsync trả về null (AI timeout/lỗi/JSON không đọc được), ContentCrawlPipelineService.ProcessPendingAsync (dòng ~315-454) cố ý đẩy bài vào Status=Pending mà không set QualityScore — thiết kế "fail open" có chủ đích (comment trong CrawlScreenService.cs dòng 94-96: "Lỗi thì MỞ, không đóng"). Vấn đề: ProcessPendingAsync CHỈ chấm bài đúng lúc nó chuyển vào Pending, không bao giờ quét lại bài Pending cũ chưa có điểm — không có cơ chế retry nào tồn tại.

Đo trên DB snapshot: Status=4 (Pending) chỉ 56% có điểm (365/652), 287 bài kẹt không điểm liên tục từ 2026-08-17 đến tận fetch mới nhất (2026-09-22 06:03) — không phải sự cố nhất thời, đang tiếp diễn mỗi ngày.

Yêu cầu: bổ sung cơ chế quét lại (rescan/retry) các bài Pending chưa có QualityScore, để giảm số bài kẹt vĩnh viễn không điểm, mà không phá vỡ hành vi chấm điểm tức thời hiện có cho bài mới cào về.

Nguồn: backend/Modules/ContentCrawl/ContentCrawlPipelineService.cs (ProcessPendingAsync, ~315-454); backend/Modules/ContentCrawl/CrawlScreenService.cs (ScreenAsync, dòng 94-128); backend/Modules/ContentCrawl/ContentCrawlWorker.cs (ProcessArticlesAsync, dòng 100-106); truy vấn trực tiếp vni_automation (1).db bảng CrawledArticles.

### R-015 — NEWSSITE-01: Trang tin tức đã có sẵn tính năng dựng lại (rebuild) — backfill
<!-- req status=in-progress files=backend/Modules/NewsSite/NewsSiteController.cs,backend/Modules/NewsSite/NewsSiteBuilder.cs,ClientApp/src/modules/news-site/hooks/useNewsSite.js,ClientApp/src/modules/news-site/pages/NewsSitePage.jsx -->

Xác nhận với người dùng (2026-09-22): "tạo trang web tin tức" mà người dùng nhắc tới chính là action rebuild/dựng lại trang tĩnh đã tồn tại sẵn trong code, KHÔNG phải tính năng mới cần xây.

Bằng chứng: backend/Modules/NewsSite/NewsSiteController.cs dòng 209-212, endpoint POST /api/NewsSite/build [Authorize(Roles = "Admin,ContentManager")], gọi NewsSiteBuilder.BuildAsync(ct) — build lại TOÀN BỘ site tĩnh (trang chủ, trang chuyên mục, trang bài) rồi ghi ra thư mục phục vụ, trả message "Đã dựng lại trang tin". Frontend đã có hook useRebuildSite (ClientApp/src/modules/news-site/hooks/useNewsSite.js:45) dùng trong ClientApp/src/modules/news-site/pages/NewsSitePage.jsx.

Requirement này chỉ để ghi nhận tính năng đã hoàn thiện vào PCS (tránh PCS thiếu context khiến agent sau tưởng nhầm là chưa có), không phát sinh task triển khai mới. Không backfill evidence "passed" vì không có lệnh test cụ thể chạy trong phiên này — chỉ xác nhận qua đọc code trực tiếp, để review/manual test sau nếu cần.

### R-016 — CONTENT-CRAWL-03: Nút dừng/chạy toàn bộ chức năng tin tức tại runtime
<!-- req status=in-progress files=backend/Modules/ContentCrawl/ContentCrawlWorker.cs,backend/Modules/ContentCrawl/ContentCrawlOptions.cs,backend/Modules/ContentCrawl/ContentCrawlController.cs,backend/Modules/ContentCrawl/ContentCrawlRepository.cs,ClientApp/src/modules/content-crawl/pages/CrawlInboxPage.jsx,ClientApp/src/modules/content-crawl/hooks/useCrawl.js,ClientApp/src/modules/content-crawl/services/crawlApi.js -->

Xác nhận với người dùng (2026-09-22): cần một nút dừng TOÀN BỘ chức năng tin tức — dừng cào tin, dừng chấm điểm bài báo, và dừng đưa bài báo lên trang tin tức — cùng lúc, không cần restart app.

Hiện trạng: ContentCrawlWorker.ExecuteAsync (backend/Modules/ContentCrawl/ContentCrawlWorker.cs dòng 36-57) mỗi tick gọi tuần tự đúng 3 bước này: FetchDueSourcesAsync (cào), ProcessArticlesAsync (chấm trùng + chấm điểm AI), ComposeQueuedArticlesAsync (viết bài đã duyệt lên trang tin). Cờ bật/tắt DUY NHẤT hiện có là ContentCrawlOptions.Enabled (backend/Modules/ContentCrawl/ContentCrawlOptions.cs dòng 10) — đọc MỘT LẦN lúc worker khởi động qua IOptions<T> (không phải IOptionsMonitor), đổi giá trị phải sửa appsettings/biến môi trường VÀ restart app. Không có cách nào dừng/chạy lại pipeline khi app đang chạy.

Yêu cầu: thêm cơ chế bật/tắt runtime (lưu bền trong DB, không mất khi restart) mà ContentCrawlWorker kiểm tra lại MỖI tick — khi tắt thì bỏ qua cả 3 bước (Fetch/Process/Compose) trong tick đó, khi bật lại thì tiếp tục hoạt động từ tick kế tiếp mà không cần restart. Phân biệt rõ với NEWSSITE-01 (R-015) — nút "dựng lại trang tin" (rebuild) đã có sẵn và KHÔNG thuộc phạm vi requirement này; đây là việc dừng luồng TỰ ĐỘNG (worker), không phải hành động thủ công build/approve/reject của người duyệt.

Nguồn: backend/Modules/ContentCrawl/ContentCrawlWorker.cs; backend/Modules/ContentCrawl/ContentCrawlOptions.cs; xác nhận trực tiếp từ người dùng qua AskUserQuestion.
