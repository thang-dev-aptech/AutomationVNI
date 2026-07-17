# Production Readiness Notes — VNI Automation

Ghi nhận yêu cầu và hạn chế trước khi đưa MVP lên production. **Không** coi checklist này là đủ để go-live — dùng cùng [`e2e_checklist.md`](e2e_checklist.md) và [`docs/DEPLOYMENT.md`](../../docs/DEPLOYMENT.md).

---

## Required before production

### Security & secrets

| Item | Trạng thái MVP | Hành động |
|------|----------------|-----------|
| JWT secret từ env/secret manager | Partial | `Jwt__SecretKey` bắt buộc; không để trống production |
| Token encryption/decryption service | **TODO** | `SocialChannel.AccessToken` lưu plain; cần encrypt-at-rest + decrypt khi publish |
| Không commit secrets | Done | `appsettings.json` placeholder only; user-secrets/env |
| ApiLog payload sanitization | Done | password/token/secret redacted |
| Rate limiting auth endpoints | **TODO** | Brute-force protection login/register |
| HTTPS everywhere | **TODO** | TLS termination + HSTS |

### Infrastructure

| Item | Trạng thái MVP | Hành động |
|------|----------------|-----------|
| SQLite → production DB | Optional | SQLite OK cho single-node nhỏ; scale → PostgreSQL/SQL Server + migration strategy |
| Database backup | **TODO** | Scheduled backup `vni_automation.db` hoặc managed DB snapshots |
| File storage volume | Partial | `Storage/Files` cần persistent volume; plan CDN cho public media |
| Secret manager | **TODO** | Azure Key Vault, AWS Secrets Manager, hoặc K8s secrets |
| Logging retention | **TODO** | ApiLog table growth; rotation/archival policy |
| Health checks / monitoring | **TODO** | `/health`, metrics, alerting on job failures |

### Facebook & media

| Item | Trạng thái MVP | Hành động |
|------|----------------|-----------|
| Facebook App + permissions | **TODO** | `pages_manage_posts`, review process |
| Public media URL for photos | Partial | Local preview không public; cần CDN/`PublicUrl` HTTPS |
| Long-lived Page token refresh | **TODO** | `TokenExpiresAt` + refresh flow |
| `UseRealFacebook=false` default | Done | Bật có chủ đích khi staging validated |

### Application config

| Item | Production value |
|------|------------------|
| `DevSeed.Enabled` | `false` |
| `Scheduler.Enabled` | `true` only if auto-publish needed |
| `Seed:AdminPassword` | empty / disable seed |
| CORS | Restrict to production FE origin only |

### Dependencies

| Item | Ghi chú |
|------|---------|
| Package vulnerability monitoring | `dotnet list package --vulnerable` — theo dõi `Microsoft.OpenApi`, `SQLitePCLRaw` transitive |
| ImageSharp | Updated 3.1.12; monitor advisories |

---

## Known limitations (MVP)

### By design

- **DevSeed** chỉ Development — không bật production
- **Mock fallback default** — text AI, image/overlay, publish khi thiếu config/key
- **No FK constraints** — referential integrity ở application layer
- **No wwwroot media** — files qua API; cần CDN cho Facebook photo URL
- **Single-node scheduler** — `BackgroundService` in-process; không distributed lock

### Not implemented / partial

- **Full analytics** — dashboard stats cơ bản, không BI/export
- **RAG / media embeddings search** — model có `MediaEmbedding`; production vector search chưa hoàn thiện
- **Real image AI** — image generation vẫn mock placeholder PNG
- **Multi-platform publish** — Facebook only cho real publish; LinkedIn/Instagram enum only
- **Token encryption** — `DEV_ENCRYPTED_TOKEN` placeholder; no real encryption service
- **Email notifications** — không có
- **Audit trail UI** — ApiLog có; chưa có admin viewer đầy đủ
- **Horizontal scaling** — SQLite + local file storage = single instance

### Operational risks

| Risk | Mitigation |
|------|------------|
| Scheduler duplicate publish | Idempotency keys on PublishLog; review before multi-instance |
| Large ApiLog table | Retention job, exclude sensitive payloads already sanitized |
| AI provider outage | Mock fallback dev; production may want fail-fast flag |
| Facebook rate limits | `PublishStatus.RateLimited` + retry; monitor |

---

## Pre-go-live checklist (summary)

```
[ ] DevSeed.Enabled = false
[ ] Jwt:SecretKey from secret manager
[ ] HTTPS + CORS production origins
[ ] Database backup configured
[ ] File storage persistent volume
[ ] Token encryption implemented OR accepted risk documented
[ ] Facebook app reviewed + Page token valid
[ ] Public media URL strategy for photo posts
[ ] Scheduler tested on staging with process-due + real interval
[ ] e2e_checklist.md signed off
[ ] dotnet list package --vulnerable reviewed
[ ] Admin password not default dev credentials
```

---

## Staging test order (recommended)

1. Deploy with `UseRealFacebook=false`, no AI keys → smoke-test.sh
2. Enable AI key on staging → test `/api/ai/test-text-generation`
3. Enable `UseRealFacebook=true` + real Page token → `process-real` on test post
4. Enable scheduler → schedule test post
5. FE full checklist with production-like CORS/origin

---

## Document references

- [Root README](../../README.md)
- [Backend README](../../backend/README.md)
- [Deployment guide](../../docs/DEPLOYMENT.md)
- [E2E checklist](e2e_checklist.md)
- [Frontend smoke test](../coding/frontend_smoke_test.md)
