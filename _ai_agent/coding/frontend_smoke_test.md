# Frontend Smoke Test Checklist — VNI Automation MVP

Checklist kiểm tra end-to-end FE sau khi build/lint pass.  
**Môi trường dev mặc định:** `http://localhost:5068` (API + SPA cùng origin) hoặc Vite dev proxy theo `ClientApp/.env`.

## Chuẩn bị

- [ ] Backend đang chạy (`dotnet run` trong `backend/`)
- [ ] FE đã build hoặc chạy `npm run dev` trong `ClientApp/`
- [ ] Có tài khoản seed (ít nhất Admin). Ví dụ: `admin@vni.local` / `Admin@123`
- [ ] (Tuỳ chọn) Có user các role: ContentManager, Reviewer, Viewer để test RBAC

## 1. Login Admin

- [ ] Mở `/login`
- [ ] Đăng nhập Admin thành công → redirect `/dashboard`
- [ ] Topbar hiển thị email user
- [ ] Sidebar hiển thị đủ menu theo role Admin

## 2. Dashboard

- [ ] `/dashboard` load stat cards (posts, channels, media, jobs…)
- [ ] Recent posts panel hiển thị (hoặc empty state nếu chưa có bài)
- [ ] Job health / Channel health hiển thị (hoặc fallback "Chưa có dữ liệu")
- [ ] Nút **Làm mới** hoạt động, không crash khi API lỗi từng phần
- [ ] Quick links điều hướng đúng module

## 3. Platforms — Tạo Social Channel

- [ ] Vào `/platforms`
- [ ] Admin thấy nút **Kết nối kênh**
- [ ] Tạo channel mới (page name, platform, token…) → toast success
- [ ] Channel xuất hiện trong bảng
- [ ] Sửa channel → toast success
- [ ] (Tuỳ chọn) Xóa channel → confirm → toast success

## 4. Posts — Tạo bài

- [ ] Vào `/posts/create`
- [ ] Form load kênh (loading state khi đang tải)
- [ ] Nếu chưa có kênh → empty state + link Platforms
- [ ] Tạo post mới → redirect post detail
- [ ] Post xuất hiện trong `/posts` list

## 5. Post Detail

- [ ] `/posts/:id` hiển thị metadata (status, lịch đăng, created/updated)
- [ ] Workflow panel hiển thị action phù hợp status + role
- [ ] Generation status panel load (hoặc empty/error có retry)
- [ ] Timeline hiển thị nếu có dữ liệu
- [ ] Media panel hiển thị cover / media khác

## 6. Text Generation (qua Jobs)

- [ ] Sau khi tạo post, backend queue generation job (status Queued/Generating)
- [ ] Vào `/jobs` tab **Generation Jobs** → thấy job liên quan post
- [ ] Admin: **Process** job text → confirm → toast success
- [ ] Quay lại post detail → generation status cập nhật / content có dữ liệu

## 7. Image Generation / Render (qua Jobs)

- [ ] Nếu flow Full AI: có job image/overlay sau text
- [ ] Admin process/retry job image tại `/jobs` nếu failed/pending
- [ ] Post detail / media panel phản ánh ảnh sinh ra (nếu backend mock trả URL)

## 8. Media — Upload / Thêm

- [ ] Vào `/media`
- [ ] ContentManager/Admin: **Thêm media** (URL-based create) → toast success
- [ ] Media xuất hiện trong grid
- [ ] Sửa / xóa media → confirm + toast (Admin/ContentManager)

## 9. Gắn media vào post

- [ ] Post detail → **Chọn media** → chọn asset làm cover
- [ ] Cover hiển thị preview
- [ ] Gỡ media → confirm → toast success

## 10. Submit Review

- [ ] Post ở trạng thái Draft/Ready → ContentManager/Admin thấy **Gửi duyệt**
- [ ] Confirm → status chuyển **Chờ duyệt** (WaitingReview)

## 11. Approve

- [ ] Đăng nhập Reviewer hoặc Admin
- [ ] Post WaitingReview → **Duyệt** → confirm → status **Đã duyệt**

## 12. Schedule

- [ ] Post Approved → **Lên lịch đăng** → chọn datetime → lưu
- [ ] Status **Đã lên lịch**, scheduled time hiển thị đúng format `vi-VN` / `Asia/Ho_Chi_Minh`

## 13. Publish Now

- [ ] Post Approved hoặc Scheduled → **Đăng ngay** → confirm
- [ ] Status chuyển Publishing/Published (tuỳ backend)
- [ ] Publish log xuất hiện tại `/jobs` tab Publish Logs (nếu backend ghi log)

## 14. Jobs & Logs

- [ ] `/jobs` auto-refresh ~10s, nút **Làm mới** hoạt động
- [ ] Filter status/type hoạt động
- [ ] Xem **Error** modal cho job failed
- [ ] Publish Logs tab: đọc được log theo post/channel

## 15. Viewer — Read-only

- [ ] Login Viewer
- [ ] Dashboard stats read-only (không crash)
- [ ] Không thấy nút create/edit/delete post, media, channel
- [ ] Không thấy workflow approve/publish
- [ ] `/jobs` xem được nhưng không có Process/Retry/Cancel
- [ ] `/platforms` có thể bị forbidden hoặc read-only tùy rule — verify menu

## 16. Reviewer

- [ ] Login Reviewer
- [ ] Thấy posts/media/dashboard
- [ ] Approve/Reject/Schedule/Publish hoạt động trên post WaitingReview/Approved
- [ ] Không quản lý Platforms (menu ẩn hoặc forbidden)
- [ ] Không process jobs (chỉ Admin)

## 17. ContentManager

- [ ] Login ContentManager
- [ ] Tạo post, submit review, upload media
- [ ] Không approve/reject
- [ ] Không delete channel / process jobs
- [ ] Dashboard ưu tiên bài của mình trong recent (nếu có `userId`)

## 18. Auth persistence

- [ ] Sau login, **F5 refresh** trang bất kỳ → vẫn đăng nhập
- [ ] Token hết hạn / 401 → redirect `/login` sạch

## 19. Logout

- [ ] Nút **Đăng xuất** → redirect `/login`
- [ ] Không truy cập được route protected khi chưa login
- [ ] Quay lại `/dashboard` khi chưa login → redirect login

## 20. Route & UX polish

- [ ] URL không tồn tại (vd. `/abc`) → **NotFoundPage** (có layout nếu đã login)
- [ ] `/forbidden` hiển thị khi thiếu role (vd. Reviewer vào `/platforms`)
- [ ] Toast success/error xuất hiện nhất quán sau mutation
- [ ] Dangerous actions có confirm (delete, cancel job, publish now, cancel schedule…)
- [ ] Không có màn hình trắng khi API lỗi — luôn có ErrorState + retry

## Ghi chú khi fail

| Triệu chứng | Kiểm tra |
|---|---|
| 401 liên tục | JWT secret, clock skew, `VITE_API_BASE_URL` |
| CORS | Dev proxy hoặc CORS backend `Program.cs` |
| Empty dashboard stats | API filter trả `total`, network tab |
| Job process không đổi status | Backend worker/mock, role Admin |
| Media preview lỗi | `publicUrl` có truy cập được không |

## Lệnh verify build

```bash
cd ClientApp
npm run build
npm run lint
```

Kết quả mong đợi: **build pass**, **oxlint pass**.
