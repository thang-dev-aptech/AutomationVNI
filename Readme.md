# VNI Automation

Hệ thống tự động hóa nội dung và đăng bài mạng xã hội: tạo bài (AI/mock), sinh media, duyệt workflow, lên lịch và publish (mock hoặc Facebook thật).

**Trạng thái:** MVP hoàn thiện — mock E2E pass, AI text + Facebook publish có provider abstraction và fallback.

## Tech stack

| Layer | Stack |
|-------|--------|
| Backend | .NET 10 Web API, ASP.NET Core Identity + JWT, EF Core, SQLite |
| Frontend | React 19, Vite 8, React Router, TanStack Query, Zustand, Axios |
| Storage | Local files (`backend/Storage/Files`, **không** dùng `wwwroot` cho media) |
| Jobs | Generation pipeline (text/image/overlay), Publish pipeline, Background scheduler |

## Cấu trúc thư mục

```
AutomationVNI/
├── backend/                 # .NET 10 Web API
│   ├── Modules/             # Post, MediaAsset, PublishLog, Auth, ...
│   ├── Shared/              # Middleware, scheduler, AI/social publish providers
│   ├── Data/                # DbContext, migrations, seeders
│   ├── Storage/Files/       # Media uploads (gitignored)
│   ├── smoke-test.sh        # Backend E2E curl script
│   └── README.md            # Chi tiết backend
├── ClientApp/               # React SPA (Vite)
│   └── src/modules/         # auth, posts, jobs, media, dashboard, ...
├── docs/
│   ├── DEPLOY_VPS.md        # Deploy VPS: Docker Compose, Caddy, Jenkins
│   └── DEPLOYMENT.md        # Hướng dẫn deploy production
└── _ai_agent/               # Prompts, database spec, testing checklists
    └── testing/
        ├── e2e_checklist.md
        └── production_readiness.md
```

## Quick start (dev)

### 1. Backend

```bash
cd backend
dotnet restore
dotnet ef database update
dotnet run
```

API mặc định: `http://localhost:5068` (`ASPNETCORE_ENVIRONMENT=Development`).

### 2. Frontend

```bash
cd ClientApp
cp .env.example .env    # tùy chọn
npm install
npm run dev
```

UI: `http://localhost:5173` — Vite proxy `/api` → backend.

**Hoặc** build FE vào backend SPA host:

```bash
cd ClientApp && npm run build
cd ../backend && dotnet run   # serve static từ wwwroot/dist
```

### 3. Dev seed

Bật trong `backend/appsettings.Development.json`:

```json
"DevSeed": { "Enabled": true }
```

Tạo idempotent: SocialChannel, PageContext, Category, roles, admin user.

Production: **`DevSeed.Enabled` phải `false`** (`appsettings.json`).

### 4. Admin dev login

| Field | Value |
|-------|-------|
| Email | `admin@vni.local` |
| Password | `Admin@123` |

**Chỉ dùng local dev.** Không deploy password này lên production.

## Smoke test

### Backend

```bash
# Terminal 1
cd backend && dotnet run

# Terminal 2
chmod +x backend/smoke-test.sh   # một lần
./backend/smoke-test.sh
```

Kỳ vọng: **PASSED** (mock publish, không cần AI/Facebook key).

### Frontend

Xem checklist chi tiết: [`_ai_agent/coding/frontend_smoke_test.md`](_ai_agent/coding/frontend_smoke_test.md)

```bash
cd ClientApp
npm run build
npm run lint
```

Sau đó chạy thủ công các bước login → dashboard → posts → jobs → publish (xem [`_ai_agent/testing/e2e_checklist.md`](_ai_agent/testing/e2e_checklist.md)).

## Cấu hình secrets

**Không commit secret thật** vào git. Dùng `dotnet user-secrets` (local) hoặc environment variables (production).

### JWT (bắt buộc)

```bash
cd backend
dotnet user-secrets set "Jwt:SecretKey" "your-local-jwt-secret-min-32-chars"
```

Production:

```bash
export Jwt__SecretKey="production-secret-min-32-chars"
```

### AI text — 9router (mặc định)

```bash
dotnet user-secrets set "AiProviders:Providers:9router:ApiKey" "YOUR_KEY"
export AiProviders__Providers__9router__ApiKey="YOUR_KEY"
```

### AI text — OpenAI

```bash
dotnet user-secrets set "AiProviders:Providers:openai:ApiKey" "YOUR_KEY"
dotnet user-secrets set "AiProviders:DefaultProvider" "openai"
```

Không có ApiKey → pipeline text **fallback mock** (smoke test vẫn pass).

Test: `POST /api/ai/test-text-generation` (Admin/ContentManager).

### Facebook publish thật

Mặc định mock (`SocialPublish:UseRealFacebook=false`).

```bash
dotnet user-secrets set "SocialPublish:UseRealFacebook" "true"
export SocialPublish__UseRealFacebook="true"
```

Chuẩn bị SocialChannel:

- `Platform` = Facebook
- `ExternalPageId` = Facebook Page ID
- `AccessToken` = Page Access Token (set qua API, **không** commit)

`DEV_ENCRYPTED_TOKEN` (dev seed) → **không** gọi Facebook thật.

Endpoint force real: `POST /api/publishlog/{id}/process-real` (Admin/ContentManager).

### Meta OAuth — Connect Facebook / Instagram

Cấu hình app Meta (Facebook Login) và set secrets qua user-secrets:

```bash
dotnet user-secrets set "MetaOAuth:AppId" "YOUR_META_APP_ID"
dotnet user-secrets set "MetaOAuth:AppSecret" "YOUR_META_APP_SECRET"
dotnet user-secrets set "MetaOAuth:ConfigId" "YOUR_LOGIN_CONFIG_ID"
```

Redirect URI trong Meta Developer Console:

`http://localhost:5068/api/meta/callback`

Luồng UI: **Platforms → + Connect → Meta** → đăng nhập Meta → callback sync Pages/Instagram/Groups vào `SocialChannel` gắn `SocialConnection`.

| Endpoint | Mô tả |
|----------|--------|
| `GET /api/meta/connect-url` | Trả OAuth URL (Admin/ContentManager) |
| `GET /api/meta/callback` | Callback Meta (anonymous) → redirect FE |
| `GET /api/socialconnection` | Danh sách tài khoản + kênh con |

Scopes mặc định gồm `groups_access_member_info` (Groups sync best-effort; thiếu quyền thì bỏ qua, vẫn sync Pages).

Production: set `MetaOAuth:RedirectUri`, `FrontendSuccessUri`, `FrontendErrorUri` cho domain thật.

Chi tiết: [`backend/README.md`](backend/README.md).

### Scheduler

| Env | `Scheduler:Enabled` | Ghi chú |
|-----|---------------------|---------|
| Production default | `false` | Bật khi cần auto-publish scheduled posts |
| Development | `true` | Trong `appsettings.Development.json` |

Manual trigger: `POST /api/publishlog/process-due`.

## File storage

- Path: `backend/Storage/Files/` (config `FileStorage:RootPath`)
- Upload: `POST /api/mediaasset/upload`
- Preview: `GET /api/mediaasset/{id}/preview`
- **Không** lưu media trong `wwwroot` — dùng API preview/download

## Quy ước kỹ thuật

- **Không FK constraint** ở database — cột `Guid` tham chiếu logic; repository tự validate
- **Soft delete** mặc định (`IsDeleted`)
- ApiLog redact password/token/secret trong payload

## Tài liệu liên quan

| Tài liệu | Mô tả |
|----------|--------|
| [`backend/README.md`](backend/README.md) | API, migration, providers, endpoints |
| [`docs/DEPLOY_VPS.md`](docs/DEPLOY_VPS.md) | Deploy lên VPS bằng Docker Compose + Jenkins, xử lý lỗi OAuth Threads |
| [`docs/DEPLOYMENT.md`](docs/DEPLOYMENT.md) | Deploy production (tổng quan) |
| [`docs/GIT_PUSH.md`](docs/GIT_PUSH.md) | Checklist trước khi push Git |
| [`_ai_agent/testing/e2e_checklist.md`](_ai_agent/testing/e2e_checklist.md) | Checklist E2E đầy đủ |
| [`_ai_agent/testing/production_readiness.md`](_ai_agent/testing/production_readiness.md) | TODO trước production |
| [`_ai_agent/coding/frontend_smoke_test.md`](_ai_agent/coding/frontend_smoke_test.md) | FE smoke test chi tiết |

## Verify build

```bash
cd backend && dotnet build
cd ClientApp && npm run build && npm run lint
```
