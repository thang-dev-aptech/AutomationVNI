# PROMPT: Redesign giao diện VNI Automation theo bộ nhận diện "VNI Education" + chuẩn responsive

> Đưa nguyên prompt này cho Codex. Prompt đã bám sát đúng codebase thực tế (đường dẫn file, tên biến, tên class có thật). Yêu cầu Codex tuân thủ tuyệt đối phần **RÀNG BUỘC** để không phá vỡ chức năng.

---

## 1. Bối cảnh dự án (đọc kỹ trước khi làm)

- Frontend: **React 19 + Vite**, `ClientApp/`. Không dùng Tailwind/UI-lib — **CSS thuần + CSS variables**.
- Hệ design token nằm ở `:root` trong `ClientApp/src/index.css`. Toàn bộ component tham chiếu qua các biến `--color-*`, `--radius`, `--shadow`, `--sidebar-width`. **Đây là nguồn chân lý duy nhất — đổi màu ở đây là lan ra toàn app.**
- Layout khung: `ClientApp/src/app/layouts/MainLayout.jsx` (+ `.css`) = sidebar tối cố định 260px + `main-shell` (Topbar + content). `Topbar.jsx` (+ `.css`).
- Các file CSS cần đụng tới (đã tồn tại):
  - `ClientApp/src/index.css` — tokens + primitives (`.btn`, `.card`, `.badge`, `.alert`, `.form-group`, `table`).
  - `ClientApp/src/app/layouts/MainLayout.css`, `Topbar.css`, `AuthLayout.css`.
  - `ClientApp/src/shared/components/`: `Modal.css`, `PageHeader.css`, `StatePanel.css`, `Toast.css`, `ChannelMultiSelect.css`.
  - `ClientApp/src/modules/dashboard/components/DashboardComponents.css`, `dashboard/pages/DashboardPage.css`.
  - `ClientApp/src/modules/social-channels/pages/PlatformsPage.css`, `components/PlatformCard.css`.
  - `ClientApp/src/modules/media/components/MediaAssetCard.css`, `media/pages/MediaPage.css`.
  - `ClientApp/src/modules/jobs/components/JobsTables.css`.
- Logo nguồn: `Thiet-ke-chua-co-ten-28.png` (đặt file logo vào `ClientApp/public/` khi dùng trong app).

## 2. Mục tiêu

1. Thay bảng màu generic (xanh Tailwind + slate đen) bằng **bộ nhận diện thương hiệu VNI Education** (xanh dương / cam / xanh lá).
2. Nâng cấp **responsive chuẩn cho MỌI thiết bị** (mobile 360px → tablet → desktop → màn rộng), thay vì kiểu "sidebar xếp chồng" hiện tại.
3. Giữ nguyên 100% chức năng, cấu trúc component, tên class hiện có. Đây là **restyle + responsive**, KHÔNG viết lại logic.

## 3. Bảng màu thương hiệu (lấy từ logo)

Ba màu lõi từ 3 ô của logo + wordmark:

| Vai trò | Hex | Ghi chú |
|---|---|---|
| **Brand Blue** (ô chữ V + wordmark) | `#2D6CB6` | Màu **primary** chính của toàn app |
| Blue hover/đậm | `#245A99` | trạng thái hover/active |
| Blue nhạt (nền badge/hover) | `#E8F1FA` | |
| **Brand Orange** (ô chữ N) | `#F08A22` | Màu **accent/secondary**, dùng cho CTA phụ, highlight, biểu đồ |
| Orange đậm | `#D8761A` | hover |
| Orange nhạt | `#FDEBD6` | nền badge warning-ish |
| **Brand Green** (ô chữ I + mũ) | `#33A457` | Màu **success**, trạng thái hoàn tất/hoạt động |
| Green đậm | `#2A8A49` | hover |
| Green nhạt | `#E4F5EA` | nền badge success |

Màu hệ thống (giữ chuẩn ngữ nghĩa, chỉ đồng bộ tông):
- Danger `#DC2626`, danger nhạt `#FEE2E2`.
- Nền app `--color-bg`: đổi sang **`#F5F7FA`** (xám hơi ấm, trung tính, không đấu màu với tri-color).
- Surface `#FFFFFF`, border `#E3E8EF`.
- Text `#1F2A37`, text-muted `#5B6675`.

**Sidebar** (đang là `#0f172a` đen-xanh): đổi sang **brand blue đậm** làm nền để gắn với thương hiệu — nền `#1E4E82` (hoặc gradient `linear-gradient(180deg,#2D6CB6,#1E4E82)`), link active dùng nền trắng-mờ + viền/điểm nhấn **cam** để tạo tương phản thương hiệu. Chữ link `rgba(255,255,255,.78)`, hover → trắng.

### Token đích cho `:root` (ghi đè trong `index.css`)
Codex phải cập nhật (và BỔ SUNG) các biến sau, đồng thời thêm biến accent/success mới:
```css
:root{
  --color-bg:#F5F7FA;
  --color-surface:#FFFFFF;
  --color-border:#E3E8EF;
  --color-text:#1F2A37;
  --color-text-muted:#5B6675;

  --color-primary:#2D6CB6;          /* brand blue  */
  --color-primary-hover:#245A99;
  --color-primary-soft:#E8F1FA;

  --color-accent:#F08A22;           /* brand orange */
  --color-accent-hover:#D8761A;
  --color-accent-soft:#FDEBD6;

  --color-success:#33A457;          /* brand green */
  --color-success-hover:#2A8A49;
  --color-success-soft:#E4F5EA;

  --color-warning:#D8761A;          /* dùng chung tông cam */
  --color-danger:#DC2626;

  --sidebar-bg:#1E4E82;
  --sidebar-width:260px;
  --radius:12px;
  --radius-sm:8px;
  --shadow:0 1px 3px rgba(16,32,58,.08);
  --shadow-md:0 6px 20px rgba(16,32,58,.10);
}
```
> Lưu ý: các file khác đang hardcode `#0f172a`, `#2563eb`, `#f4f6f9`, `#f8fafc`, `rgba(37,99,235,…)`… Codex phải rà **toàn bộ `ClientApp/src`** thay các giá trị hardcode này bằng biến tương ứng (dùng grep các chuỗi: `#0f172a`, `#2563eb`, `#1d4ed8`, `37, 99, 235`, `#f4f6f9`, `15, 23, 42`).

## 4. Nguyên tắc dùng màu (giữ giao diện sạch, không loè loẹt)

- **60/30/10**: nền/surface trung tính (60%), brand blue cho điều hướng & hành động chính (30%), cam là điểm nhấn hiếm (10% — CTA phụ, tab active indicator, chart, nhãn nổi bật). **Không** phủ cam/xanh lá thành mảng lớn.
- Xanh lá = ngữ nghĩa "thành công/đang chạy" (badge success, job completed, kênh active). Đừng dùng xanh lá làm nút hành động chung.
- Nút primary = blue. Nút secondary = outline. Cam chỉ cho 1 CTA nổi bật/trang (vd nút "Tạo hàng loạt", "Kết nối kênh").
- Accent line thương hiệu: cho phép dùng dải gradient tri-color (blue→orange→green) **mỏng 3–4px** ở đỉnh topbar hoặc dưới logo như "signature", dùng tiết chế.

## 5. Cập nhật design system (primitives trong `index.css`)

- **Typography**: giữ system font nhưng ưu tiên `'Inter', 'Segoe UI', system-ui, ...` (không thêm dependency — chỉ font-stack; nếu muốn web font thì phải self-host, KHÔNG chèn CDN nếu repo không cho phép). Thang cỡ: h1 1.75rem, section 1.05–1.1rem, body 0.95rem, meta 0.8rem. line-height 1.5.
- **`.btn`**: bo `--radius-sm`, cao tối thiểu 40px, focus-visible rõ (`outline:2px solid var(--color-primary); outline-offset:2px`). Thêm biến thể `.btn-accent` (nền cam) cho CTA nổi bật. Trạng thái hover/active/disabled đồng bộ token mới.
- **`.badge`**: `badge-info`→blue-soft, `badge-success`→green-soft, `badge-warning`→orange-soft, `badge-danger`→danger. Chữ dùng tông đậm tương ứng, đảm bảo contrast ≥ 4.5:1.
- **`.card`**: radius `--radius`, `--shadow`; hover card link nâng nhẹ dùng `--shadow-md`.
- **`table`**: header nền `--color-primary-soft`, chữ header blue đậm; hàng hover nền `#F8FAFC`; **mọi bảng phải nằm trong wrapper `overflow-x:auto`** để không vỡ layout mobile.
- **`.form-group` input/select/textarea**: focus ring theo primary mới (`box-shadow:0 0 0 3px var(--color-primary-soft)`), min-height 40px, cỡ chữ ≥16px trên mobile để iOS không auto-zoom.
- **`.alert`**: error/success đồng bộ token.

## 6. Responsive — YÊU CẦU BẮT BUỘC (đây là trọng tâm)

### Breakpoints thống nhất
- `≥1280px`: desktop rộng — có thể giới hạn content `max-width` hợp lý, sidebar 260px.
- `1024–1279px`: desktop chuẩn.
- `768–1023px` (tablet): sidebar thu gọn thành **off-canvas drawer** (ẩn mặc định, mở bằng nút hamburger ở Topbar, có backdrop mờ, đóng khi chọn link/nhấn backdrop/nhấn Esc).
- `<768px` (mobile): drawer full như trên; Topbar hiện nút hamburger bên trái + tiêu đề; content padding giảm (16px).
- Nhỏ nhất phải hỗ trợ **360px** không vỡ, không tràn ngang (`body`/layout không được scroll ngang).

### Sidebar → Drawer (thay hành vi "xếp chồng" hiện tại)
- `MainLayout.jsx`: thêm state `isSidebarOpen` (dùng `useState`), truyền xuống Topbar 1 nút toggle. Thêm class `sidebar--open` + phần tử `.sidebar-backdrop`. Đóng drawer tự động khi route đổi (dùng `useLocation`).
- `MainLayout.css`: từ ≤1023px, `.sidebar` `position:fixed; transform:translateX(-100%)`, khi `.sidebar--open` → `translateX(0)`, có `transition`, `z-index` trên content, backdrop `rgba(16,32,58,.5)`. Desktop giữ grid 2 cột như cũ.
- **Chỉ được sửa JSX ở mức thêm nút hamburger + state toggle drawer.** Không đổi danh sách `NAV_ITEMS`, không đổi logic permission.

### Topbar
- Thêm nút hamburger (chỉ hiện <1024px). Ở mobile, hàng user/email co gọn: ẩn email dài hoặc rút gọn ellipsis; giữ nút Đăng xuất. Không stack lộn xộn như media query 700px hiện tại — làm gọn 1 hàng, tiêu đề trái, actions phải.

### Grid & nội dung
- Dashboard stat cards & health grid: dùng `grid-template-columns:repeat(auto-fit,minmax(min(100%,220px),1fr))` để tự xuống hàng, mobile 1 cột.
- PageHeader: tiêu đề + actions xuống 2 hàng ở mobile, actions full-width nút bấm dễ chạm.
- Modal: mobile chiếm gần full (`max-width:min(560px,100%)`, `max-height:92vh`, bo góc trên, đáy an toàn `env(safe-area-inset-bottom)` nếu cần); footer nút full-width khi hẹp.
- **Touch target ≥44×44px** cho mọi nút/link/icon-button trên mobile.
- Bảng (Jobs, Platforms, PublishLog…): bọc `overflow-x:auto`; cân nhắc ẩn cột phụ ở <768px bằng class utility (chỉ CSS, không đổi cấu trúc bảng).

## 7. Accessibility

- Contrast text/nền ≥ **4.5:1** (đặc biệt chú ý chữ trắng trên **cam** `#F08A22` → chỉ dùng chữ trắng cho chữ ≥ bold/lớn, ngược lại dùng chữ tối; badge cam nền nhạt + chữ cam đậm `#B45309`/`#8A4B10`).
- `:focus-visible` rõ ràng trên nền sáng lẫn drawer.
- Drawer: `aria-hidden` khi đóng, nút hamburger có `aria-label` + `aria-expanded`, khoá scroll `body` khi drawer mở, trap/đóng bằng Esc.
- Tôn trọng `prefers-reduced-motion` cho transition drawer/hover.

## 8. RÀNG BUỘC (không được vi phạm)

- KHÔNG thêm dependency mới (không Tailwind, không UI-lib, không CDN font/script). Giữ CSS thuần + biến.
- KHÔNG đổi tên class/DOM hiện có ngoài việc thêm class phục vụ drawer/responsive. Các component tham chiếu class theo string → đổi tên sẽ vỡ.
- KHÔNG sửa logic nghiệp vụ, API, store, router roles, hook. Chỉ CSS + phần JSX tối thiểu cho hamburger/drawer trong `MainLayout.jsx` & `Topbar.jsx`.
- KHÔNG hardcode màu mới rải rác — mọi màu phải qua biến `--color-*`.
- Giữ dark sidebar readable; giữ toàn bộ trạng thái loading/empty/error/toast hoạt động.

## 9. Sản phẩm bàn giao & tiêu chí nghiệm thu

Codex trả về:
1. Diff các file CSS + `MainLayout.jsx`/`Topbar.jsx` theo yêu cầu trên.
2. `:root` mới trong `index.css` khớp mục 3.
3. Danh sách các giá trị hardcode đã thay bằng biến (kèm file).

Nghiệm thu:
- [ ] Toàn app dùng đúng palette VNI (blue primary, cam accent, green success), không còn `#2563eb`/`#0f172a` sót lại.
- [ ] 360px / 768px / 1024px / 1440px: không scroll ngang, không đè chồng, không chữ tràn.
- [ ] Mobile/tablet: sidebar là drawer mở bằng hamburger, có backdrop, đóng bằng backdrop/Esc/đổi route.
- [ ] Mọi bảng scroll ngang gọn trong khung; touch target ≥44px.
- [ ] Contrast đạt AA; focus-visible rõ; tôn trọng reduced-motion.
- [ ] `npm run build` và `npm run lint` trong `ClientApp/` pass, không lỗi.
