# Luồng 1: Full AI Generation (Sinh Text & Ảnh tự động)

Luồng này tập trung vào việc tách biệt tác vụ sinh **Text** và sinh **Ảnh** ngay trên mô hình ( request gửi đi gồm cả logo và CTA cho mỗi page) để chạy song song (**Parallel Processing**) nhằm tối ưu thời gian.

```mermaid
graph TD
    A[Nhập Input: Tiêu đề, Category, Lịch đăng] --> B{Lưu vào DB & Đẩy vào Queue}
    B --> C[Background Worker Xử lý]

    C --> D[Dựa vào Page Context sinh Prompt]

    D --> E(Prompt 1: Text)
    D --> F(Prompt 2: Image)

    %% Nhánh sinh Text
    E --> G[Gọi API LLM: GPT/Claude]
    G --> H[Nhận Nội dung bài đăng]

    %% Nhánh sinh Ảnh
    F --> I[Gọi API Image Gen: DALL-E/Midjourney / Nano Banana]
    I --> J[Nhận Ảnh AI nền trơn]
    J --> K[Backend xử lý: Overlay Logo & CTA bằng AI]
    K --> L[Lưu file vào Storage]

    %% Gom luồng
    H --> M{Hợp nhất: Post + Media}
    L --> M

    M --> N[Lưu trạng thái 'Sẵn sàng' vào DB]
    N --> O{Scheduler kiểm tra giờ đăng}

    O -- Đến giờ --> P[Gọi API Social Media]
    O -- Chưa đến giờ --> O

    P -- Thành công --> Q[Cập nhật DB: Published]
    P -- Lỗi timeout/Token --> R[Retry Policy / Đẩy lại vào Queue]
```

---

# Luồng 2: AI Content + Tự động tìm kiếm RAG (Retrieval-Augmented Generation)

Luồng này tận dụng kho **Media** sẵn có. Sau khi **LLM** viết xong phần **Text**, hệ thống sẽ nhúng (**embedding**) nội dung đó thành **Vector** và query thẳng vào cơ sở dữ liệu (sử dụng các extension như **pgvector**) để tìm bức ảnh có khoảng cách ngữ nghĩa gần nhất.

```mermaid
graph TD
    A[Nhập Input: Tiêu đề, Category, Lịch đăng] --> B{Lưu vào DB & Đẩy vào Queue}
    B --> C[Background Worker Xử lý]

    %% Sinh Text
    C --> D[Dựa vào Page Context sinh Prompt Text]
    D --> E[Gọi API LLM: GPT/Claude]
    E --> F[Nhận Nội dung bài đăng]

    %% Tìm kiếm Media
    F --> G[Trích xuất Keywords / Sinh Vector Embedding từ Text]
    G --> H[(Query pgvector: Tìm mức độ tương đồng)]

    H --> I{Tìm thấy ảnh phù hợp >= 80%?}

    %% Nhánh có ảnh
    I -- Có --> J[Lấy danh sách Media Asset nội bộ]

    %% Nhánh Fallback (Không có ảnh)
    I -- Không --> K[Fallback: Chuyển sang kích hoạt Luồng 1 sinh ảnh AI]

    J --> L{Hợp nhất: Post + Media Nội bộ}
    K --> M{Hợp nhất: Post + Media AI}

    L --> N[Lưu trạng thái 'Sẵn sàng' vào DB]
    M --> N

    N --> O{Scheduler kiểm tra giờ đăng}

    O -- Đến giờ --> P[Gọi API Social Media]
    P -- Thành công --> Q[Cập nhật DB: Published]
```

---

# Điểm lưu ý trong cấu trúc kỹ thuật

## 1. Fallback Mechanism (Cơ chế dự phòng) trong Luồng 2

Nút điều kiện **"Tìm thấy ảnh phù hợp >= 80%"** là rất quan trọng.

Nếu kho nội bộ không có ảnh nào khớp, hệ thống phải tự động gọi lại mô đun sinh ảnh bằng AI của **Luồng 1** để bài đăng không bị thiếu hình.

---

## 2. Transaction Integrity

Quá trình lưu **Posts** và trạng thái **Media** cần tuân thủ cấu trúc chuẩn hóa (**3NF**) để đảm bảo không bị rác dữ liệu nếu một nhánh (**Text** hoặc **Ảnh**) gọi API bị lỗi giữa chừng.

---

## 3. Queue / Retry

Tác vụ **Gọi API Social Media** phải luôn đi kèm cơ chế **Retry** (ví dụ thử lại **3 lần cách nhau 5 phút**) vì API của **Facebook/LinkedIn** thường xuyên có tình trạng **rate limit** hoặc gián đoạn mạng tạm thời.