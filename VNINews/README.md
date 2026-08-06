# VNINews — giao diện trang tin tintuc.vni.edu.vn

Giao diện công khai cho trang tin của VNI Education. Hiện là **HTML tĩnh với nội
dung mẫu** để duyệt bố cục; phần backend đưa tin thật lên chưa làm.

```
VNINews/
├── index.html          Trang chủ: dải mốc tuyển sinh + tin chính + danh sách
├── tin.html            Trang bài viết
└── assets/
    ├── styles.css      Toàn bộ CSS (dùng chung 2 trang)
    ├── mark.svg        Dấu hiệu nhận diện trên đầu trang
    └── favicon.svg     Icon tab trình duyệt
```

## Xem thử

```bash
cd VNINews && python3 -m http.server 8099
# mở http://localhost:8099
```

Không cần build, không cần npm. Đó là chủ ý — xem phần dưới.

## Vì sao HTML thuần, không phải React

**Trình thu thập của Facebook không chạy JavaScript.** Trang dựng bằng React sẽ
trả về một thẻ `<div id="root">` rỗng; Facebook đọc được đúng chừng đó, nên bài
share lên fanpage ra một thẻ xám không tiêu đề, không ảnh, không mô tả — mà share
lên fanpage chính là lý do tồn tại của cả trang này.

Vì vậy toàn bộ nội dung và các thẻ `og:*` phải có sẵn trong HTML ngay lúc server
trả về. Hệ quả kèm theo: Google index được ngay, và trang mở tức thì trên máy yếu
vì không phải tải bundle.

## Quy ước để backend điền nội dung

Backend chỉ cần thay phần thân, giữ nguyên cấu trúc class.

### Ảnh bìa

Mặc định là **khối màu theo chủ đề**, không phải ảnh của toà soạn — ảnh gốc thuộc
bản quyền báo, đăng lại lên trang mình là rủi ro rõ ràng hơn cả phần chữ.

| Class | Dùng cho |
|---|---|
| `cover--policy` | chính sách, chỉ thị, quy định |
| `cover--admission` | tuyển sinh, lọc ảo, nguyện vọng |
| `cover--score` | điểm chuẩn, điểm thi |
| `cover--research` | nghiên cứu, khoa học |
| `cover--default` | còn lại |

Muốn dùng ảnh thật sau này thì đặt `style="background-image:url(...)"` đè lên, cấu
trúc không đổi.

### Dải mốc tuyển sinh

```html
<div class="slot done">   <!-- đã qua: mờ + gạch ngang -->
<div class="slot next">   <!-- mốc gần nhất chưa qua — CHỈ MỘT thẻ có class này -->
<div class="slot">        <!-- các mốc sau -->
```

Mốc phải lấy từ **một bảng lịch riêng**, không bóc tự động từ thân bài. Sai một
con số ở đây là phụ huynh lỡ hạn thật.

### Khối dẫn nguồn

`.source` ở cuối bài **luôn phải có nội dung**. Đây là ranh giới giữa điểm tin có
trách nhiệm và lấy bài của người khác. Bài nào không xác định được nguồn thì không
đăng.

### Thẻ `og:*`

Mỗi bài phải có đủ `og:title`, `og:description`, `og:image`, `og:url`. Thiếu
`og:image` thì thẻ share ra ô xám và tỉ lệ bấm rơi thẳng.

## Triển khai lên VPS

Trang tĩnh nên nginx phục vụ trực tiếp, không cần proxy:

```nginx
server {
    listen 443 ssl http2;
    server_name tintuc.vni.edu.vn;

    ssl_certificate     /etc/letsencrypt/live/tintuc.vni.edu.vn/fullchain.pem;
    ssl_certificate_key /etc/letsencrypt/live/tintuc.vni.edu.vn/privkey.pem;

    root /var/www/vninews;
    index index.html;

    # Bài viết: /tin/<slug> → /tin/<slug>.html
    location /tin/ {
        try_files $uri $uri.html $uri/index.html =404;
    }

    # CSS/ảnh đặt tên có hash khi build thì cache dài được; hiện chưa có nên để ngắn.
    location /assets/ {
        expires 1h;
        add_header Cache-Control "public";
    }

    # HTML KHÔNG được cache lâu: sửa bài xong mà Facebook và độc giả vẫn thấy bản cũ
    # thì không có cách nào ép làm mới ngoài đổi URL.
    location ~ \.html$ {
        add_header Cache-Control "public, max-age=300";
    }
}

server {
    listen 80;
    server_name tintuc.vni.edu.vn;
    return 301 https://$host$request_uri;
}
```

**Bắt buộc có HTTPS.** Facebook từ chối lấy `og:image` qua HTTP, nên thẻ share sẽ
mất ảnh dù thẻ meta viết đúng.

Sau khi đổi nội dung một bài đã share, dùng
[Sharing Debugger](https://developers.facebook.com/tools/debug/) bấm *Scrape Again*
— Facebook cache thẻ og rất lâu và không tự làm mới.

## Còn lại

- [ ] Backend sinh HTML từ bài đã duyệt và ghi vào `/var/www/vninews`
- [ ] Bảng mốc tuyển sinh để dựng dải lịch
- [ ] Ảnh `og:image` cho từng bài (hiện là khối màu, chưa có ảnh thật)
- [ ] Trang chuyên mục (`/chuyen-muc/...` đã có trên thanh điều hướng nhưng chưa có trang)
- [ ] `sitemap.xml` và `robots.txt`
