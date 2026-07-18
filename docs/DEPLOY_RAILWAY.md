# Deploy VNI Automation lên Railway

Deploy **1 image duy nhất**: backend .NET 10 phục vụ cả API lẫn React SPA (build sẵn trong `wwwroot/dist`). Một URL HTTPS cho tất cả — web, `/api/*`, và `/api/meta/callback`.

Файлы liên quan: [`Dockerfile`](../Dockerfile), [`.dockerignore`](../.dockerignore).

---

## 1. Tạo project trên Railway

1. [railway.app](https://railway.app) → **New Project → Deploy from GitHub repo** → chọn repo này.
2. Railway tự phát hiện `Dockerfile` ở gốc repo và build. Không cần cấu hình build command.
3. Vào service → **Settings → Networking → Generate Domain** → nhận URL dạng `https://<app>.up.railway.app`.

> Ghi lại domain này — dùng cho biến môi trường Meta và whitelist redirect URI.

---

## 2. Gắn Volume (BẮT BUỘC — nếu không sẽ mất dữ liệu mỗi lần redeploy)

SQLite `.db` và file media lưu trên đĩa. Container filesystem là ephemeral → phải mount volume.

- Service → **Settings → Volumes → New Volume** → **Mount path: `/data`**.

App được trỏ vào `/data` qua 2 biến `ConnectionStrings__Default` và `FileStorage__RootPath` (mục 3).

---

## 3. Biến môi trường (Variables)

.NET đọc config lồng nhau bằng dấu `__` (2 gạch dưới). Dán các biến sau vào **Variables**:

```bash
ASPNETCORE_ENVIRONMENT=Production

# Dữ liệu nằm trên volume /data
ConnectionStrings__Default=Data Source=/data/vni_automation.db
FileStorage__RootPath=/data/Storage/Files

# JWT — sinh chuỗi ngẫu nhiên >= 32 ký tự (vd: openssl rand -base64 48)
Jwt__SecretKey=<REPLACE_random_min_32_chars>
Jwt__Issuer=VNI.Automation
Jwt__Audience=VNI.Automation.Client
Jwt__AccessTokenMinutes=60

# Tài khoản admin khởi tạo (seed lần đầu)
Seed__AdminEmail=admin@your-domain.com
Seed__AdminPassword=<REPLACE_strong_password>

# Scheduler đăng bài theo lịch
Scheduler__Enabled=true

# Meta OAuth — RedirectUri phải TRÙNG KHỚP whitelist ở Meta Dashboard
MetaOAuth__AppId=1766797254560229
MetaOAuth__AppSecret=<REPLACE_app_secret>
MetaOAuth__RedirectUri=https://<app>.up.railway.app/api/meta/callback
MetaOAuth__FrontendSuccessUri=https://<app>.up.railway.app/platforms?metaConnected=success
MetaOAuth__FrontendErrorUri=https://<app>.up.railway.app/platforms?metaConnected=error
MetaOAuth__GraphVersion=v20.0

# Scopes (mảng → key có index). Thêm quyền đăng bài khi cần:
MetaOAuth__Scopes__0=public_profile
MetaOAuth__Scopes__1=pages_show_list
MetaOAuth__Scopes__2=pages_read_engagement
MetaOAuth__Scopes__3=pages_manage_posts

# AI (nếu dùng sinh nội dung/ảnh thật)
AiProviders__Providers__9router__ApiKey=<optional>
AiImageProviders__Providers__gemini__ApiKey=<optional>

# Đăng Facebook thật (mặc định false = mock)
SocialPublish__UseRealFacebook=true
```

> `PORT` do Railway tự inject — **không cần set**. Dockerfile đã bind Kestrel vào `$PORT`.

Sau khi lưu biến, Railway tự redeploy.

---

## 4. Cập nhật Meta App Dashboard

1. [developers.facebook.com](https://developers.facebook.com) → app **VNI Automation Dev** → **Facebook Login → Settings**.
2. **Valid OAuth Redirect URIs** → thêm:
   `https://<app>.up.railway.app/api/meta/callback`
3. **App Settings → Basic**:
   - **App Domains**: `<app>.up.railway.app`
   - **Privacy Policy URL**: `https://<app>.up.railway.app/privacy` (cần tạo trang này — xem mục dưới)
   - **Data Deletion**: URL/instructions (bắt buộc khi chuyển Live)

---

## 5. Kiểm tra sau deploy

- Mở `https://<app>.up.railway.app/` → thấy giao diện VNI Automation.
- Đăng nhập bằng `Seed__AdminEmail` / `Seed__AdminPassword`.
- **Platforms → + Connect → Meta** → login Facebook → chọn page → về app thấy connection.
- Logs của service (Railway → Deployments → View Logs) không có exception.

---

## 6. Còn thiếu cho App Review (public — Đường B)

Deploy này là **điều kiện cần**. Để BẤT KỲ ai connect được, còn phải:

- [ ] **Business Verification** (Meta Business Manager → Security Center) — cần giấy phép kinh doanh.
- [ ] Trang **Privacy Policy** public (`/privacy`) — chưa có, cần tạo.
- [ ] **Data Deletion** callback/instructions — chưa có endpoint, cần thêm.
- [ ] Chuyển app sang **Live** + submit **App Review** xin Advanced Access cho `pages_show_list`, `pages_read_engagement`, `pages_manage_posts` kèm screencast + tài khoản test.

---

## Ghi chú kỹ thuật

- **Vì sao Docker?** .NET 10 còn mới; build qua Docker (pin image `10.0`) chắc ăn hơn buildpack tự động.
- **Font**: image cài `fonts-dejavu-core` để overlay chữ lên ảnh (ImageSharp) chạy được headless.
- **Proxy**: app đã bật `UseForwardedHeaders` (X-Forwarded-Proto) để nhận đúng scheme https sau proxy Railway — tránh redirect loop và dựng đúng URL tuyệt đối.
- **Scale sau này**: doc database khuyến nghị chuyển SQLite → PostgreSQL và file → object storage khi tải lớn.
