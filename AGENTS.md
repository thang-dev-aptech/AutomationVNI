# AutomationVNI — Quy ước làm việc với PCS MCP

Tài liệu này áp dụng cho mọi agent làm việc trong repository AutomationVNI.

## 1. PCS là nguồn sự thật của dự án

Luôn dùng PCS MCP project có tên chính xác:

```text
AutomationVNI
```

PCS lưu business context, kế hoạch, requirement contract, trạng thái, findings và evidence. Git lưu mã nguồn và lịch sử thay đổi. Không coi commit hoặc test pass là đủ để hoàn thành requirement nếu PCS close gate chưa pass.

Không ghi dữ liệu của AutomationVNI sang project PCS khác, đặc biệt không ghi sang `MCP Project server`.

## 2. Khi PCS còn mới hoặc thiếu dữ liệu lịch sử

PCS hiện có thể chưa chứa đầy đủ requirements, decisions và conventions cũ. Agent phải bổ sung dần theo công việc thực tế, không giả định rằng “không có trong PCS” nghĩa là dự án chưa từng có chức năng đó.

### Nguồn dùng để backfill

Chỉ ghi dữ liệu lịch sử khi có thể đối chiếu từ ít nhất một nguồn cụ thể:

- Code production hiện tại.
- Regression test hiện tại.
- Git commit, blame hoặc pull request có liên quan.
- Tài liệu dự án, API contract, migration hoặc cấu hình đang dùng.
- Xác nhận trực tiếp của người dùng.

Trong detail của entry nên ghi provenance ngắn gọn, ví dụ:

```text
Nguồn: backend/Modules/MediaFolder/MediaFolderRepository.cs; commit 2f49e58.
```

Không ghi suy đoán thành decision hoặc business requirement. Nếu chưa đủ bằng chứng, ghi blocker, question trong detail hoặc yêu cầu người dùng xác nhận.

### Cái gì cần backfill

Khi bắt đầu một task và phát hiện PCS thiếu context liên quan trực tiếp, ghi bổ sung:

1. **Overview**: mục tiêu sản phẩm, người dùng chính và phạm vi hệ thống đã xác minh.
2. **Glossary**: thuật ngữ domain xuất hiện trong task và dễ hiểu sai.
3. **Convention**: quy tắc kỹ thuật hoặc nghiệp vụ đang được codebase áp dụng ổn định.
4. **Decision**: lựa chọn kiến trúc hoặc business rule đã được code/tài liệu chứng minh và vẫn còn hiệu lực.
5. **Requirement hiện hành**: hành vi đang được yêu cầu, đang bảo trì hoặc là dependency của task hiện tại.
6. **Bug/Blocker**: lỗi hoặc trở ngại hiện còn tác động.
7. **Quan hệ**: epic/task cha, dependency và linked files khi có thể xác định chắc chắn.

Không cần nhập toàn bộ lịch sử dự án trong một lượt. Ưu tiên context cần cho task hiện tại, các production paths liên quan và các ranh giới có rủi ro cao.

### Xử lý chức năng cũ đã tồn tại trong code

Nếu task đụng tới chức năng cũ nhưng PCS chưa có requirement:

1. Tạo requirement mô tả hành vi cần được bảo toàn, không mô tả chi tiết implementation.
2. Gắn linked files và provenance.
3. Tạo invariant cho behavior, authorization, data boundary hoặc integration liên quan.
4. Tạo acceptance criteria đo được từ hành vi hiện có và yêu cầu mới.
5. Đặt status theo thực tế:
   - `in-progress` nếu đang thay đổi hoặc đang kiểm chứng.
   - `not-started` nếu mới lập kế hoạch.
   - `blocked` nếu thiếu thông tin hoặc dependency.
   - Không đặt `done` chỉ vì code cũ đã tồn tại.
6. Chỉ đặt `done` sau khi evidence hợp lệ và close gate pass.

Không backfill evidence `passed` từ lời mô tả. Test cũ chỉ được dùng làm evidence khi agent thực sự chạy test trên commit mà PCS root nhận diện được. Review cũ chỉ hợp lệ khi có `review_ref` và kết quả có thể truy vết.

### Tránh dữ liệu rác và trùng lặp

Trước khi tạo entry:

- Tìm trong briefing và section tương ứng.
- Tái sử dụng hoặc update entry hiện có nếu cùng business intent.
- Không tạo một requirement mới chỉ vì đổi tên kỹ thuật hoặc đổi file.
- Không copy nguyên README, commit message hoặc source code vào detail.
- Tách epic thành task chỉ khi có deliverable, dependency hoặc write scope riêng.
- Resolve entry đã hết hiệu lực; không xóa lịch sử trừ khi entry được tạo nhầm.

## 3. Dữ liệu phải đẩy lên PCS

### Trước khi triển khai

Ghi hoặc cập nhật các mục sau khi chúng phát sinh:

- **Focus**: công việc hiện tại và phạm vi đang thực hiện.
- **Requirement/Epic/Task**: mục tiêu, phạm vi, trạng thái, priority, linked files và quan hệ cha/con hoặc dependency.
- **Decision**: lựa chọn kiến trúc, business rule, API contract, ownership hoặc trade-off đã thống nhất.
- **Convention**: quy ước ổn định mà agents sau phải tuân thủ.
- **Blocker**: trở ngại bên ngoài hoặc thiếu thông tin khiến không thể tiếp tục.
- **Bug**: lỗi sản phẩm đã xác nhận nhưng chưa xử lý.
- **Glossary**: thuật ngữ domain dễ hiểu sai.

Mỗi task phải có write scope rõ ràng. Nếu nhiều agents làm song song, không giao cùng file cho nhiều agents trong cùng wave.

### Requirement contract

Mỗi requirement cần:

1. Ít nhất một invariant mô tả hành vi hoặc ranh giới không được vi phạm.
2. Acceptance criteria đo được, liên kết với invariant.
3. Evidence kind đúng với cách xác minh: `test`, `command`, `review`, `manual` hoặc `file`.
4. `independent_review=required` cho thay đổi có rủi ro cao, authorization, data boundary, migration, transaction hoặc production integration.

Không viết AC mơ hồ như “hoạt động đúng”. AC phải nói rõ input, hành vi, ranh giới dữ liệu và kết quả mong đợi.

### Trong khi triển khai

Đẩy lên PCS:

- Violation khi code hoặc implementation phá invariant.
- Blocker mới phát hiện.
- Decision mới làm thay đổi contract hoặc execution plan.
- Status `in-progress` hoặc `blocked` phù hợp thực tế.

Không resolve violation chỉ vì đã sửa code. Chỉ resolve sau khi root cause đã được sửa, test pass, commit và independent review xác nhận.

### Sau khi triển khai

Đẩy lên PCS:

- Evidence cho từng acceptance criterion.
- Independent review evidence với `review_ref` ổn định.
- Kết quả compliance và close gate.
- Status `done` chỉ khi close gate pass.

Evidence phải chứa:

- Real Git commit SHA, không dùng SHA giả hoặc commit chưa tồn tại trong PCS worktree.
- Lệnh test/build thực tế đã chạy.
- Test/file reference cụ thể.
- Kết quả `passed`, `failed` hoặc `manual-pending` đúng sự thật.
- `review_ref` do agent khác cung cấp khi independent review bắt buộc.

Không ghi evidence cho working tree chưa commit. Không ghi evidence pass nếu test chưa chạy hoặc review chưa diễn ra.

## 4. Trình tự bắt buộc cho mỗi task

1. Gọi `get_project_briefing` với project `AutomationVNI`.
2. Nếu PCS thiếu context trực tiếp của task, backfill có chọn lọc theo mục 2 trước khi triển khai.
3. Gọi `get_task_contract` cho task hiện tại và requirement IDs liên quan.
4. Gọi `get_requirement_contract` để lấy đầy đủ invariant và AC.
5. Gọi `get_requirement_evidence` và `evaluate_close_gate` để biết evidence, violation và trạng thái hiện tại.
6. Dùng PCS `prepare_task`, `retrieve_context`, `search_code` hoặc `get_code_map` trước khi đọc file local.
7. Đối chiếu code hiện tại với từng invariant và AC.
8. Map từng AC thành regression test hoặc evidence tương ứng.
9. Sửa root cause và review toàn bộ production paths liên quan, không chỉ file trong diff.
10. Chạy focused tests, sau đó chạy test/build rộng hơn phù hợp.
11. Commit thay đổi. Không merge hoặc push nếu task không yêu cầu rõ.
12. Giao một agent khác independent review. Reviewer không được là agent triển khai và không được tự sửa code trong lượt review.
13. Sau khi review pass, resolve violations đã thực sự được khắc phục.
14. Ghi evidence bằng commit SHA thật và `review_ref` thật.
15. Gọi `review_requirement_compliance` và `evaluate_close_gate`.
16. Chỉ gọi `set_requirement_status(..., "done")` khi kết quả close gate có:

```text
configured=true
passed=true
```

Nếu close gate fail, giữ `in-progress` hoặc `blocked` và ghi rõ unmet items.

## 5. Quy tắc independent review

Prompt cho reviewer phải có:

- PCS project và requirement IDs.
- Commit SHA cần review.
- Invariants và AC cần xác minh.
- Danh sách violations cần kiểm tra lại.
- Production paths cần review.
- Yêu cầu trả verdict `PASS` hoặc `FAIL`.
- Findings có `file:line`.
- Test command đã chạy hoặc lý do không chạy.
- Một `review_ref` ổn định, ví dụ:

```text
MEDIA-010203-REVIEW-2f49e58-auth-acmap
```

Reviewer không được ghi evidence hoặc đổi status thay agent điều phối, trừ khi task giao rõ quyền đó.

## 6. Đồng bộ Git worktree với PCS root

PCS project `AutomationVNI` chạy trên bản repository được copy vào PCS root. Trước khi ghi evidence, phải xác nhận PCS index nhìn thấy đúng commit vừa tạo:

1. Lấy local SHA bằng `git rev-parse HEAD`.
2. Gọi PCS `get_index_status`.
3. Nếu `last_commit` khác local SHA, đồng bộ commit/code vào PCS root theo cơ chế vận hành của môi trường.
4. Gọi `reindex` sau khi PCS root đã có commit.
5. Kiểm tra lại `get_index_status`.
6. Chỉ ghi evidence khi PCS nhận diện đúng source commit và worktree sạch.

Không thử vượt freshness guard bằng fingerprint hoặc SHA giả.

Nếu PCS trả:

```text
dirty-worktree: worktree_fingerprint does not match
```

thì:

- Không đánh dấu requirement `done`.
- Không tạo evidence giả.
- Kiểm tra PCS root có đúng commit hay chưa.
- Reindex sau khi đồng bộ.
- Nếu vẫn lệch, ghi blocker hoặc báo rõ requirement đang thiếu evidence vì PCS root/index chưa đồng bộ.

## 7. Quan hệ giữa Git và PCS

| Thông tin | Nơi lưu chính |
|---|---|
| Mã nguồn | Git repository |
| Commit SHA | Git repository |
| Business requirement | PCS requirement |
| Kiến trúc/business decision | PCS decision |
| Execution scope hiện tại | PCS focus |
| Acceptance criteria | PCS contract |
| Violation/finding | PCS requirement violation |
| Test/review proof | PCS evidence |
| Trạng thái hoàn thành | PCS requirement status và close gate |

Không dùng file Markdown này để thay PCS. File này chỉ quy định quy trình; dữ liệu task thực tế vẫn phải ghi vào PCS.

## 8. Checklist kết thúc

Trước khi báo hoàn thành, xác nhận tất cả:

- [ ] Đúng PCS project `AutomationVNI`.
- [ ] Requirement ID và AC IDs đã được nêu rõ.
- [ ] Context cũ liên quan trực tiếp đã được backfill với provenance nếu PCS còn thiếu.
- [ ] Không tạo requirement/decision lịch sử từ suy đoán.
- [ ] Focus/decision/violation đã cập nhật khi cần.
- [ ] Mỗi AC có regression test hoặc evidence hợp lệ.
- [ ] Focused tests pass.
- [ ] Test suite/build phù hợp pass hoặc lỗi ngoài phạm vi được báo rõ.
- [ ] Thay đổi đã commit bằng SHA thật.
- [ ] PCS root/index nhìn thấy commit đó.
- [ ] Independent review do agent khác thực hiện và có `review_ref`.
- [ ] Blocking violations đã được resolve sau review.
- [ ] Compliance review pass.
- [ ] Close gate `configured=true`, `passed=true`.
- [ ] Chỉ sau đó requirement mới được đặt `done`.
- [ ] Không merge hoặc push nếu người dùng không yêu cầu.
