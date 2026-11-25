# Bảng mutation chống hồi quy

Mỗi file `*.json` ở đây là một bảng mutation: một thay đổi nhỏ có chủ đích trong code sản phẩm mà
bộ test **phải** bắt được (làm đỏ). `scripts/run-mutations.sh` áp từng mutation trong một git worktree
riêng tạo từ `HEAD`, build, chạy đúng bộ lọc test của mục đó và phân loại kết quả. Cây làm việc chính
không bao giờ bị sửa.

```bash
scripts/run-mutations.sh --list                       # xem bảng
scripts/run-mutations.sh --only T1,M3                 # vài mục
scripts/run-mutations.sh --since main                 # chỉ mục có file nằm trong git diff --name-only main
scripts/run-mutations.sh --report /tmp/mut.md         # toàn bộ bảng + báo cáo (.md hoặc .json)
scripts/run-mutations.sh --jobs 2                     # số worktree song song (mặc định min(4, nproc/2))
```

Runner chỉ dùng nội dung đã commit ở `HEAD` — commit trước khi chạy. Worktree mặc định nằm dưới
`~/.cache/automationvni-mutations` (đổi bằng `--workdir` hoặc `MUTATION_TMPDIR`); trên btrfs/xfs bản
chép obj/bin dùng reflink nên gần như miễn phí. Mỗi job cần khoảng 1,3 GB đĩa khi không có reflink.

## Một mục trong bảng

```json
{
  "id": "T1",
  "description": "Ngưỡng dùng > thay vì >=",
  "file": "backend/Modules/Campaign/FullMedia/FitThreshold.cs",
  "original": "=> score >= minFitScore;",
  "replacement": "=> score > minFitScore;",
  "filter": "FullyQualifiedName~Backend.Tests.Modules.Campaign.FullMedia.FullMediaPipelineTests",
  "expect_red": ["ThresholdBoundary_0939Rejects_094Accepts"]
}
```

- `original` là chuỗi **chính xác** (kể cả thụt lề, `\n`). Phải xuất hiện đúng một lần; nếu nhiều lần
  thì thêm `"occurrence": 2` (đếm từ 1). Không có → `PATTERN-NOT-FOUND`.
- `filter` là biểu thức `dotnet test --filter`; nối nhiều lớp bằng `|`. Giữ hẹp (lớp chứa test kỳ vọng)
  để nhanh.
- `expect_red`: tên method test (hoặc `Lớp.Method`) mà mutation này phải làm đỏ; cần ít nhất một. Tham
  số theory không cần ghi.
- Tuỳ chọn: `note` (ghi chú tự do, runner bỏ qua), `extra_edits` (danh sách `{file, original, replacement, occurrence?}` khi mutation cần sửa
  nhiều chỗ), `timeout` (giây), `allow_unexpected: true` (chấp nhận đỏ ở test ngoài danh sách).

## Quy ước tên

Tiền tố theo nhóm, giữ nguyên id đã dùng trong review/commit để tra ngược:
`T*` pipeline + worker (FULLMEDIA t4), `F*` sửa review d2d6172, `m*` chấm lại chọn lọc, `P*` đường
chỉ-tạo-bài, `V*` Vision, `M*` bộ chấm logprobs. Biến thể cùng ý tưởng dùng hậu tố `b`, `c` hoặc `-a`,
`-b` (`T27b`, `F2-c`). Không đổi id đã có; mục bỏ đi thì đánh dấu `stale`, đừng xoá.

## Kết quả

| trạng thái | nghĩa | exit |
|---|---|---|
| `RED` | ít nhất một test trong `expect_red` đỏ | 0 |
| `RED-UNEXPECTED` | đỏ nhưng ở test khác danh sách — sửa `expect_red` hoặc xem lại | 1 (trừ `allow_unexpected`) |
| `GREEN` | mọi test xanh: **lỗ hổng test** | 1 |
| `BUILDERR` | không biên dịch được — không tính là đỏ, viết lại mutation | 1 |
| `PATTERN-NOT-FOUND` / `AMBIGUOUS` | code đã đổi; cập nhật `original` hoặc đánh dấu `stale` | 1 |
| `TIMEOUT` / `ERROR` | quá giờ / test host lỗi mà không có test đỏ | 1 |
| `EQUIVALENT` / `STALE` | đã đánh dấu, không chạy | 0 |

Trước khi chạy mutation, runner chạy hợp các bộ lọc trên `HEAD` chưa đột biến (baseline); nếu không xanh
thì dừng, vì khi đó "đỏ" không còn ý nghĩa.

## Mutant tương đương và mục cũ

- Mutant tương đương (không thể đổi hành vi quan sát được): `"equivalent": true` kèm `"reason"` giải
  thích vì sao (bắt buộc). Runner không chạy mục này.
- Code đã đổi khiến mutation không còn áp dụng: `"stale": true` kèm `"reason"`, liệt kê trong báo cáo
  review. Viết mutation thay thế cho hành vi mới nếu hành vi vẫn cần được bảo vệ.

## Khi nào chạy gì

- Đang sửa code có trong bảng: `--since <nhánh gốc>` hoặc `--only <id>` cho các mục liên quan.
- Thêm/đổi test của một nhóm: `--only` các mục của nhóm đó để chắc test mới vẫn bắt được mutation.
- Lần chốt trước review/commit cuối của một requirement, và khi đổi hạ tầng test: chạy **toàn bộ** bảng
  và đính báo cáo (`--report`).

## Build nhanh (bỏ Migrations)

Mặc định runner nạp `fast-build.targets` (qua `-p:CustomAfterMicrosoftCommonTargets`) để bỏ
`backend/Migrations/**` khỏi biên dịch — khoảng 77% số dòng của backend; một lần build mutation giảm từ
~21s xuống ~7s. Test không chạy migration. Test cần ModelSnapshot (`HasPendingModelChanges`) sẽ đỏ ở
bước baseline nếu lọt vào bộ lọc; khi đó chạy `--full-build`. Runner tự dùng build đầy đủ nếu mutation
nằm trong `backend/Migrations/`. `-p:RunAnalyzers=false` cũng chỉ áp cho runner (analyzer không đổi IL).
`dotnet build`/`dotnet test` bình thường không bị ảnh hưởng.
