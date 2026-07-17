# VNI Automation — Backend API

.NET 10 Web API cho hệ thống tự động hóa nội dung và đăng bài mạng xã hội (mock MVP).

## Tech stack

- .NET 10 Web API
- ASP.NET Core Identity + JWT
- SQLite + Entity Framework Core (code-first)
- Repository pattern + module controllers
- Local file storage (`Storage/Files`, **không** dùng `wwwroot`)
- Background scheduler cho bài `Scheduled`
- Mock pipelines: text/image/render generation, Facebook publish

## Yêu cầu

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- `python3` (cho `smoke-test.sh`)

## Chạy backend

```bash
cd backend
dotnet restore
dotnet ef database update   # lần đầu hoặc sau migration mới
dotnet run
```

Mặc định Development: `http://localhost:5068` (xem `Properties/launchSettings.json`).

## Database migration

```bash
cd backend
dotnet ef migrations add <TenMigration>   # khi đổi model
dotnet ef database update
```

SQLite file: `Data/vni_automation.db` (theo `ConnectionStrings:Default`).

## Dev seed

Bật trong `appsettings.Development.json`:

```json
"DevSeed": { "Enabled": true, ... }
```

Production (`appsettings.json`): **`DevSeed.Enabled` phải là `false`**.

Seed idempotent tạo:

| Entity | ID mặc định |
|--------|-------------|
| SocialChannel | `00000000-0000-0000-0000-000000000001` |
| PageContext | `00000000-0000-0000-0000-000000000002` |
| Category | `00000000-0000-0000-0000-000000000003` |

Cần `ASPNETCORE_ENVIRONMENT=Development` để seed + dev password có hiệu lực.

### Login admin dev (chỉ local)

| Field | Value |
|-------|-------|
| Email | `admin@vni.local` |
| Password | `Admin@123` |

**Chỉ dùng cho môi trường dev.** Không deploy password này lên production.

```bash
POST /api/auth/login
{ "email": "admin@vni.local", "password": "Admin@123" }
```

## AI text generation (OpenAI-compatible)

Provider-agnostic config tại `AiProviders`. Mặc định: `9router`. Fallback mock khi **không có ApiKey** hoặc AI call lỗi.

### Cấu hình (không commit key thật)

`appsettings.json` chứa BaseUrl/model; `ApiKey` để trống.

### Set API key — 9router

```bash
dotnet user-secrets set "AiProviders:Providers:9router:ApiKey" "YOUR_KEY"
```

Hoặc env:

```bash
export AiProviders__Providers__9router__ApiKey="YOUR_KEY"
```

### Set API key — OpenAI

```bash
dotnet user-secrets set "AiProviders:Providers:openai:ApiKey" "YOUR_KEY"
```

### Đổi provider mặc định

```bash
dotnet user-secrets set "AiProviders:DefaultProvider" "openai"
```

Hoặc sửa `AiProviders:DefaultProvider` trong config (`9router` | `openai`).

### Test endpoint (auth required)

```bash
POST /api/ai/test-text-generation
Authorization: Bearer <token>
Content-Type: application/json

{
  "title": "Tuyển sinh 2026",
  "category": "Tuyển sinh",
  "brandContext": "VNI Automation",
  "tone": "Thân thiện, chuyên nghiệp"
}
```

- Có ApiKey → gọi AI thật, `source: "ai"`.
- Không có ApiKey → trả mock preview + hướng dẫn set key.

Pipeline `generationjob/{id}/process` (text) tự dùng AI khi key có; ngược lại giữ mock (smoke test pass không cần key).

## Facebook publish (real / mock)

Mặc định `SocialPublish:UseRealFacebook = false` — smoke test và dev luôn dùng **mock publish**.

### Bật Facebook publish thật

```bash
dotnet user-secrets set "SocialPublish:UseRealFacebook" "true"
```

Hoặc env: `SocialPublish__UseRealFacebook=true`

### Chuẩn bị SocialChannel

| Field | Giá trị |
|-------|---------|
| `Platform` | `Facebook` (1) |
| `ExternalPageId` | Facebook Page ID |
| `AccessToken` | Page Access Token (user-secrets, không commit) |

```bash
# Cập nhật token qua API hoặc DB — không log/commit token
PUT /api/socialchannel/{id}
```

**Lưu ý token:**
- `DEV_ENCRYPTED_TOKEN` (dev seed) → **không** gọi Facebook thật, fallback mock.
- Token encryption chưa có — `AccessToken` lưu plain trong DB; TODO khi có encryption service.
- Không trả `AccessToken` trong API response (đã ẩn).

### Media URL cho photo publish

Facebook `/photos` cần **HTTPS public URL**. Preview local (`localhost`) không dùng được → fallback `/feed` text-only.

Đặt `MediaAsset.PublicUrl` = URL public (CDN/ngrok) nếu muốn đăng kèm ảnh.

### Endpoints

| Endpoint | Mô tả |
|----------|--------|
| `POST /api/publishlog/{id}/process` | Mock khi `UseRealFacebook=false`; real khi bật + token hợp lệ |
| `POST /api/publishlog/{id}/process-real` | Force real Facebook (Admin/ContentManager) |

Lỗi token/permission → `Post.Status = NeedFix`. Transient → `Failed` + retry.

## Meta OAuth — Connect Facebook / Instagram / Groups

Đồng bộ theo **tài khoản Meta** (`SocialConnection`): Pages, Instagram Business, Groups (best-effort) vào `SocialChannel`.

### Cấu hình Meta Developer

1. Tạo app Meta, bật **Facebook Login**
2. Valid OAuth Redirect URI: `http://localhost:5068/api/meta/callback`
3. Scopes mặc định: `public_profile`, `pages_show_list`, `pages_read_engagement`, `pages_manage_posts`, `instagram_basic`, `groups_access_member_info`
4. User test phải là Admin/Developer của app (Development mode)
5. Groups API có thể fail nếu thiếu permission — sync Pages/IG vẫn thành công

### Secrets (user-secrets — không commit)

```bash
dotnet user-secrets set "MetaOAuth:AppId" "YOUR_META_APP_ID"
dotnet user-secrets set "MetaOAuth:AppSecret" "YOUR_META_APP_SECRET"
```

Production env:

```bash
export MetaOAuth__AppId="..."
export MetaOAuth__AppSecret="..."
export MetaOAuth__RedirectUri="https://api.yourdomain.com/api/meta/callback"
export MetaOAuth__FrontendSuccessUri="https://app.yourdomain.com/platforms?metaConnected=success"
export MetaOAuth__FrontendErrorUri="https://app.yourdomain.com/platforms?metaConnected=error"
```

### Endpoints

| Endpoint | Auth | Mô tả |
|----------|------|--------|
| `GET /api/meta/connect-url` | Admin, ContentManager | Trả `{ url }` redirect Meta OAuth |
| `GET /api/meta/callback` | Anonymous | `/me` + Pages + Groups → upsert connection/channels → redirect FE |
| `GET /api/socialconnection` | Admin, ContentManager, Viewer | Tài khoản + kênh con (Page/IG/Group) |
| `DELETE /api/socialconnection/{id}` | Admin, ContentManager | Soft disconnect account + tắt channels |

Frontend: **+ Connect → Meta** trên `/platforms`. Re-sync = OAuth lại (làm mới token).

Callback **không** ghi ApiLog. Upsert theo `Platform + ExternalPageId`, gắn `SocialConnectionId`.

Publish Group: chưa hỗ trợ (sync/hiển thị trước).

## Smoke test

```bash
# Terminal 1
cd backend && dotnet run

# Terminal 2
chmod +x smoke-test.sh   # một lần
./smoke-test.sh
```

Biến môi trường tùy chọn: `BASE_URL`, `ADMIN_EMAIL`, `ADMIN_PASSWORD`, `DEFAULT_CHANNEL_ID`, `DEFAULT_CATEGORY_ID`.

Script **không in** JWT token hay password ra console.

Flow: login → verify seed channel → create post → text/image/render mock → approve → publish mock → verify Published + media preview.

## File storage

| Setting | Mặc định |
|---------|----------|
| `FileStorage:RootPath` | `Storage/Files` (relative to project root) |
| Upload | `POST /api/mediaasset/upload` |
| Preview | `GET /api/mediaasset/{id}/preview` (anonymous) |
| Download | `GET /api/mediaasset/{id}/download` |

File lưu ngoài `wwwroot`. Không commit thư mục upload vào git nếu chứa dữ liệu thật.

## Scheduler

| Setting | Production default | Development |
|---------|-------------------|-------------|
| `Scheduler:Enabled` | `false` | `true` |
| `IntervalSeconds` | `30` | `30` |
| `BatchSize` | `10` | `10` |

Manual trigger: `POST /api/publishlog/process-due` (Admin/ContentManager).

## Endpoint chính

| Nhóm | Endpoint |
|------|----------|
| Auth | `POST /api/auth/login`, `POST /api/auth/register` |
| Post | CRUD + workflow: `submit-review`, `approve`, `schedule`, `publish-now` |
| Generation | `queue-text/image/render`, `generationjob/{id}/process` |
| Media | `upload`, `preview`, `download` |
| Publish | `publishlog/{id}/process`, `process-real`, `process-due` |
| Reference | `socialchannel`, `pagecontext`, `category` |

Chi tiết: `backend.http` (REST Client).

## Quy ước kỹ thuật

- **Không FK constraint** ở database — chỉ cột `Guid` tham chiếu logic; repository tự validate.
- **Không wwwroot** cho file media — dùng `Storage/Files` + API preview/download.
- **Soft delete** mặc định (`IsDeleted`).
- **ApiLog** sanitize password/token/secret trong request/response payload.

## Secrets — user-secrets & environment

**Không commit secret thật** vào `appsettings.json` hoặc git.

### User secrets (local dev)

```bash
cd backend
dotnet user-secrets set "Jwt:SecretKey" "your-local-jwt-secret-min-32-chars"
dotnet user-secrets set "Seed:AdminPassword" "YourLocalAdminPassword"
```

User secrets override `appsettings.*.json` khi chạy local.

### Environment variables (production / CI)

ASP.NET Core dùng `__` (double underscore) cho nested keys:

```bash
export Jwt__SecretKey="production-secret-min-32-chars"
# Seed admin (optional — production leave empty)
export Seed__AdminEmail="admin@company.com"
export Seed__AdminPassword="YOUR_ADMIN_PASSWORD"
export ConnectionStrings__Default="Data Source=/var/data/vni.db"

# AI provider keys
export AiProviders__Providers__9router__ApiKey="YOUR_KEY"
export AiProviders__Providers__openai__ApiKey="YOUR_KEY"
export AiProviders__DefaultProvider="9router"

# Facebook publish (optional)
export SocialPublish__UseRealFacebook="true"
```

### Checklist production

- [ ] `DevSeed.Enabled = false`
- [ ] `Scheduler.Enabled` bật chỉ khi cần auto-publish
- [ ] `Jwt:SecretKey` từ env/secrets (không để trống)
- [ ] `Seed:AdminPassword` rỗng hoặc tắt seed admin
- [ ] Không log token/password (ApiLog đã redact)

## Package vulnerabilities

Chạy kiểm tra:

```bash
dotnet list package --vulnerable
```

Ghi nhận hiện tại (transitive, chưa có bản fix an toàn trong scope MVP):

| Package | Severity | Ghi chú |
|---------|----------|---------|
| `SixLabors.ImageSharp` | Moderate | Đã nâng `3.1.12`; theo dõi advisory |
| `Microsoft.OpenApi` | High | Transitive từ `Microsoft.AspNetCore.OpenApi` |
| `SQLitePCLRaw.lib.e_sqlite3` | High | Transitive từ EF Core SQLite |

TODO: nâng `Microsoft.AspNetCore.OpenApi` khi bản patch fix OpenApi transitive; theo dõi EF Core SQLite updates.

## Cấu trúc thư mục

```
backend/
  Modules/          # API modules (Post, MediaAsset, PublishLog, ...)
  Shared/           # BaseEntity, middleware, scheduler, dev seed
  Data/             # AppDbContext, migrations, IdentitySeeder
  Storage/Files/    # Uploaded/generated media (gitignored nội dung)
  smoke-test.sh     # E2E mock test
  backend.http      # REST Client examples
```
