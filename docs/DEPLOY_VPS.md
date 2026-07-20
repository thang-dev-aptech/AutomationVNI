# Deploy VNI Automation lên VPS bằng Docker

Deploy **1 image duy nhất**: backend .NET 10 phục vụ cả API lẫn React SPA (build sẵn vào `wwwroot/dist`). Một domain HTTPS cho tất cả — web, `/api/*`, `/api/meta/callback`, `/api/threads/callback`.

File liên quan: [`Dockerfile`](../Dockerfile), [`docker-compose.yml`](../docker-compose.yml), [`.dockerignore`](../.dockerignore), [`.env.example`](../.env.example).

---

## 1. Chuẩn bị VPS

- Docker Engine + Docker Compose plugin
- Một domain đã trỏ A record về IP của VPS
- Reverse proxy có TLS (nginx hoặc Caddy) — **bắt buộc**, không phải tuỳ chọn: Threads OAuth từ chối mọi redirect URI không phải HTTPS

## 2. Lấy code và tạo file env

```bash
git clone https://github.com/thang-dev-aptech/AutomationVNI.git
cd AutomationVNI
cp .env.example .env.production
```

Mở `.env.production` và điền. Bắt buộc phải có:

| Biến | Ghi chú |
|---|---|
| `Jwt__SecretKey` | Sinh mới: `openssl rand -base64 48` |
| `Seed__AdminPassword` | Mật khẩu admin đăng nhập lần đầu |
| `MetaOAuth__AppId` / `AppSecret` | App Dashboard → App settings → Basic |
| `ThreadsOAuth__AppId` / `AppSecret` | **Cùng trang đó nhưng kéo xuống** phần Threads App ID — là cặp khác |
| `*__RedirectUri`, `*__FrontendSuccessUri`, `*__FrontendErrorUri` | Thay `<domain>` bằng domain thật |
| `AiProviders__Providers__openai__ApiKey` | Xem mục AI bên dưới |

`.env.production` đã nằm trong `.gitignore` — kiểm tra lại bằng `git check-ignore -v .env.production` trước khi commit bất cứ thứ gì.

## 3. Chạy

```bash
docker compose --env-file .env.production up -d --build
```

Cờ `--env-file` là **bắt buộc**. Compose chỉ tự đọc file tên đúng `.env`; thiếu cờ này container khởi động với config rỗng và chết ngay ở bước kiểm tra `Jwt:SecretKey`.

Container lắng nghe ở `127.0.0.1:8080` — chỉ loopback, không expose thẳng ra internet.

## 4. Reverse proxy + TLS

Caddy là cách ngắn nhất vì tự xin và gia hạn Let's Encrypt. `/etc/caddy/Caddyfile`:

```caddy
your-domain.com {
    reverse_proxy 127.0.0.1:8080
}
```

Nếu dùng nginx, nhớ forward 2 header sau — app đọc chúng để dựng đúng URL `https://` cho callback OAuth. Thiếu chúng thì redirect URI sinh ra sẽ là `http://` và Meta từ chối:

```nginx
proxy_set_header X-Forwarded-Proto $scheme;
proxy_set_header X-Forwarded-For   $proxy_add_x_forwarded_for;
```

## 5. Đăng ký URI trong Meta App Dashboard

Hai danh sách **tách biệt**, khai nhầm chỗ sẽ báo lỗi `1349168` (URL bị chặn):

- **Threads** → Settings → **URL gọi lại chuyển hướng**: `https://your-domain.com/api/threads/callback`
- **Facebook Login** → Settings → **Valid OAuth Redirect URIs**: `https://your-domain.com/api/meta/callback`

Khớp tuyệt đối từng ký tự, không thêm `/` ở cuối. Trong Threads Settings nhớ bật **Web OAuth Login**.

App Settings → Basic → **App Domains**: `your-domain.com`.

## 6. Kiểm tra sau deploy

```bash
docker compose logs -f app          # không có exception
docker compose ps                   # healthcheck = healthy
```

- Mở `https://your-domain.com/` → thấy giao diện
- Đăng nhập bằng `Seed__AdminEmail` / `Seed__AdminPassword`
- **Platforms → + Connect → Meta** → chọn Page → quay về thấy connection
- **Platforms → + Connect → Threads** → xem mục 8 nếu lỗi

## 7. Dữ liệu và backup

SQLite và file upload nằm trên 2 Docker volume, sống qua các lần redeploy:

```bash
docker volume ls | grep vni
docker run --rm -v vni-data:/data -v $(pwd):/backup alpine \
  tar czf /backup/vni-db-$(date +%F).tar.gz /data
```

Volume vẫn nằm trên cùng một VPS — hỏng ổ là mất. Đặt lịch copy file backup ra ngoài.

## 8. Lỗi Threads thường gặp

| Mã | Nguyên nhân | Xử lý |
|---|---|---|
| `4476002` | Dùng Meta App ID thay vì Threads App ID | Lấy đúng cặp ở phần "Threads App ID" |
| `1349168` | Redirect URI chưa whitelist, hoặc khai nhầm sang Facebook Login | Thêm vào **Threads → URL gọi lại chuyển hướng** |
| `1349245` | Tài khoản chưa accept lời mời tester | App Dashboard → App roles → Threads Testers → mời; rồi app Threads → Settings → Account → Website permissions → Invites → Accept. Admin app cũng phải mời riêng |

## 9. Còn thiếu để user thường dùng được

Deploy xong app vẫn ở **Development mode** — chỉ tester connect được. Để mở cho mọi người:

- [ ] Trang **Privacy Policy** public — chưa có
- [ ] **Data Deletion** callback — chưa có endpoint
- [ ] **Business Verification** (Meta Business Manager → Security Center) — cần giấy phép kinh doanh
- [ ] Submit **App Review** kèm screencast cho từng permission, rồi chuyển app sang Live

---

## Ghi chú kỹ thuật

- **AI text provider**: `appsettings.json` mặc định là `9router` trỏ `http://127.0.0.1:20128` — chỉ có trên máy dev, trong container sẽ fail. `.env.example` vì vậy đặt `AiProviders__DefaultProvider=openai`. Muốn giữ 9router thì phải trỏ nó sang endpoint public.
- **Font**: image cài `fonts-dejavu-core` để ImageSharp overlay chữ lên ảnh chạy được headless.
- **Token lưu plaintext**: `SocialChannelModel.AccessToken` chưa mã hoá (TODO ở `SocialPublishService`). Lộ database là lộ toàn bộ quyền đăng bài — cân nhắc trước khi chạy production thật.
- **Scale**: khi tải lớn nên chuyển SQLite → PostgreSQL và file → object storage.
