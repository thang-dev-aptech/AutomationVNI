# Deployment Guide — VNI Automation

Hướng dẫn triển khai backend + frontend. Xem tổng quan tại [README.md](../README.md).

## Prerequisites

- .NET 10 SDK
- Node.js 20+ (build frontend)
- Reverse proxy với HTTPS (production)
- Secret manager hoặc env vars (không dùng file config có secret)

## Build artifacts

```bash
# Frontend → backend/wwwroot/dist
cd ClientApp
npm ci
npm run build

# Backend
cd ../backend
dotnet restore
dotnet publish -c Release -o ./publish
```

Chạy published app:

```bash
cd backend/publish
ASPNETCORE_ENVIRONMENT=Production \
Jwt__SecretKey="YOUR_PRODUCTION_JWT_SECRET_MIN_32_CHARS" \
ConnectionStrings__Default="Data Source=/var/data/vni_automation.db" \
./backend
```

## Production configuration checklist

| Setting | Production value | Ghi chú |
|---------|------------------|---------|
| `ASPNETCORE_ENVIRONMENT` | `Production` | |
| `Jwt:SecretKey` | env / secret manager | **Bắt buộc**, min 32 ký tự |
| `DevSeed:Enabled` | `false` | |
| `Seed:AdminPassword` | empty hoặc tắt seed | |
| `Scheduler:Enabled` | `true` nếu cần auto-publish | Mặc định `false` |
| `SocialPublish:UseRealFacebook` | `true` khi sẵn sàng | Mặc định `false` (mock) |
| `AiProviders:Providers:*:ApiKey` | env per provider | Để trống = mock text |
| `ConnectionStrings:Default` | path DB production | SQLite hoặc đổi DB (xem readiness) |

## Secrets (environment variables)

ASP.NET Core nested config dùng `__`:

```bash
export Jwt__SecretKey="YOUR_PRODUCTION_JWT_SECRET"
export ConnectionStrings__Default="Data Source=/var/data/vni_automation.db"

# AI (optional)
export AiProviders__DefaultProvider="9router"
export AiProviders__Providers__9router__ApiKey="YOUR_KEY"
export AiProviders__Providers__openai__ApiKey="YOUR_KEY"

# Facebook publish (optional)
export SocialPublish__UseRealFacebook="true"

# Scheduler
export Scheduler__Enabled="true"
export Scheduler__IntervalSeconds="30"
export Scheduler__BatchSize="10"
```

**Không** đặt token/password trong `appsettings.json` trên server.

## Database migration

```bash
cd backend
dotnet ef database update
```

SQLite file production: mount volume persistent (vd. `/var/data/vni_automation.db`).

Backup định kỳ trước khi migrate.

## HTTPS & reverse proxy

- Terminate TLS tại Nginx/Caddy/IIS
- Forward `X-Forwarded-Proto`, `X-Forwarded-For`
- Cấu hình CORS production: chỉ allow origin FE thật (sửa `Program.cs` policy hoặc env-based)

## File storage

- `FileStorage:RootPath` → volume persistent (vd. `/var/vni/storage`)
- Đảm bảo process có quyền read/write
- **Không** dùng `wwwroot` cho media uploads
- Facebook photo publish cần `MediaAsset.PublicUrl` HTTPS public (CDN) — preview localhost không dùng được

## Facebook production

1. Tạo Facebook App + Page permissions (`pages_manage_posts`, `pages_read_engagement`, …)
2. App review nếu publish public
3. SocialChannel: `ExternalPageId` + Page Access Token (long-lived)
4. `SocialPublish:UseRealFacebook=true`
5. Test: `POST /api/publishlog/{id}/process-real` trước khi bật scheduler

Token `DEV_ENCRYPTED_TOKEN` không bao giờ publish thật.

## Post-deploy verification

```bash
# Health: login
curl -X POST https://your-host/api/auth/login \
  -H "Content-Type: application/json" \
  -d '{"email":"YOUR_ADMIN","password":"YOUR_PASSWORD"}'

# Backend smoke (against staging with mock publish)
BASE_URL=https://your-staging-host ./backend/smoke-test.sh
```

Xem checklist đầy đủ: [`_ai_agent/testing/e2e_checklist.md`](../_ai_agent/testing/e2e_checklist.md).

## Known MVP limitations

Xem [`_ai_agent/testing/production_readiness.md`](../_ai_agent/testing/production_readiness.md).
