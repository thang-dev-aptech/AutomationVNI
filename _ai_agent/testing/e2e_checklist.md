# Final E2E Checklist — VNI Automation MVP

Checklist tổng hợp backend + frontend trước release/staging. Đánh dấu `[x]` khi pass.

**Môi trường dev mặc định:** Backend `http://localhost:5068`, Frontend `http://localhost:5173`.

---

## A. Build & tooling

- [ ] `cd backend && dotnet build` — 0 errors
- [ ] `cd ClientApp && npm run build` — pass
- [ ] `cd ClientApp && npm run lint` — pass
- [ ] `chmod +x backend/smoke-test.sh` (một lần)

---

## B. Backend smoke (automated)

- [ ] Backend đang chạy (`dotnet run`)
- [ ] `./backend/smoke-test.sh` → **PASSED**
- [ ] Không cần AI key / Facebook key (mock fallback)

---

## C. Auth & seed

- [ ] `DevSeed.Enabled=true` trong Development
- [ ] Login `admin@vni.local` / `Admin@123` → JWT
- [ ] Dev seed entities tồn tại:
  - [ ] SocialChannel `00000000-0000-0000-0000-000000000001`
  - [ ] PageContext `00000000-0000-0000-0000-000000000002`
  - [ ] Category `00000000-0000-0000-0000-000000000003`

---

## D. Reference data (FE hoặc API)

- [ ] Tạo / verify SocialChannel (Platforms)
- [ ] Tạo / verify Category (nếu dùng UI)
- [ ] Tạo / verify PageContext (nếu dùng UI)

---

## E. Post creation & generation pipeline

- [ ] Create post (socialChannelId seed, generationFlow FullAI)
- [ ] Queue + process **text generation**
  - [ ] Mock path (no AI key)
  - [ ] *(Optional)* AI path khi có `AiProviders:Providers:*:ApiKey`
- [ ] Queue + process **image generation** (mock PNG)
- [ ] Queue + process **image render / overlay**
- [ ] `GET /api/post/{id}/generation-status` phản ánh steps
- [ ] Post có `Content` sau text step

---

## F. Media

- [ ] Post media list có cover
- [ ] `GET /api/mediaasset/{id}/preview` → HTTP 200
- [ ] *(FE)* Upload media tại `/media`
- [ ] *(FE)* Gắn media vào post detail

---

## G. Review workflow

- [ ] Submit review *(chỉ khi Draft/Ready — sau FullAI pipeline thường đã WaitingReview)*
- [ ] Approve (Admin/Reviewer) → `Approved`
- [ ] *(Optional)* Reject → reason

---

## H. Publish — mock (default)

- [ ] `SocialPublish:UseRealFacebook=false`
- [ ] Publish now → PublishLog Pending
- [ ] `POST /api/publishlog/{id}/process` → Success
- [ ] Post `status=Published`, `publishedUrl` mock
- [ ] *(FE)* `/jobs` tab Publish Logs hiển thị log

---

## I. Publish — scheduled

- [ ] Schedule post (`scheduledAt` > UtcNow)
- [ ] Chờ scheduler **hoặc** `POST /api/publishlog/process-due`
- [ ] Post → Published, PublishLog Success

---

## J. Jobs monitor (FE)

- [ ] `/jobs` — Generation Jobs tab
- [ ] Filter status/type
- [ ] Admin: Process / Retry / Cancel / Fail
- [ ] Publish Logs tab
- [ ] Error modal cho failed jobs

---

## K. Dashboard (FE)

- [ ] `/dashboard` stat cards load
- [ ] Recent posts panel
- [ ] Job health / Channel health panels
- [ ] Refresh không crash khi partial API fail

---

## L. Role-based UI (FE)

- [ ] **Admin** — full access, process jobs
- [ ] **ContentManager** — create post/media, no approve
- [ ] **Reviewer** — approve/reject/schedule/publish, no platforms admin
- [ ] **Viewer** — read-only, no dangerous actions
- [ ] `/forbidden` khi thiếu quyền
- [ ] Logout + F5 persistence

Chi tiết RBAC: [`_ai_agent/coding/frontend_smoke_test.md`](../coding/frontend_smoke_test.md).

---

## M. Optional — AI real

- [ ] Set `AiProviders:Providers:9router:ApiKey` hoặc `openai:ApiKey` via user-secrets
- [ ] `POST /api/ai/test-text-generation` → `source: "ai"`
- [ ] Process text job → `OutputPayload` có `source: "ai"`

---

## N. Optional — Facebook real publish

> **Không chạy trong smoke test mặc định.** Cần token Page thật + staging.

- [ ] `SocialPublish:UseRealFacebook=true` (user-secrets)
- [ ] SocialChannel: Platform Facebook, Page ID thật, token thật (không `DEV_ENCRYPTED_TOKEN`)
- [ ] *(Photo)* `MediaAsset.PublicUrl` HTTPS public
- [ ] `POST /api/publishlog/{id}/process-real` → Success
- [ ] Post có `ExternalPostId` + `PublishedUrl` Facebook thật
- [ ] Lỗi token → Post `NeedFix`

---

## O. Regression notes

| Triệu chứng | Kiểm tra |
|-------------|----------|
| Smoke fail login | JWT secret, DevSeed, `appsettings.Development.json` |
| 401 FE | `VITE_API_BASE_URL`, token expiry |
| CORS | Vite proxy hoặc `DevCors` policy |
| Text mock thay vì AI | ApiKey rỗng — expected |
| Facebook mock URL | `UseRealFacebook=false` — expected |
| Schedule không publish | `Scheduler:Enabled`, `scheduledAt` đã qua |

---

## Sign-off

| Role | Name | Date | Notes |
|------|------|------|-------|
| Dev | | | |
| QA | | | |
