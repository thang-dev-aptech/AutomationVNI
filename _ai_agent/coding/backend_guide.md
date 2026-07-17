# Backend Guide — Sinh code khi có Model mới

Tài liệu này hướng dẫn **quy trình cụ thể** để AI agent thêm một entity/model mới vào backend VNI Automation.

> **Đọc trước:** `system_prompt.md` (bối cảnh dự án), `sysPromptCoding_Backend.md` (convention kỹ thuật), `_ai_agent/database` (schema tham chiếu).

## Foundation đã scaffold sẵn

Project backend đã có sẵn:

```text
Shared/BaseEntity.cs, ApiResponse.cs, PagedFilterRequest.cs, PagedResult.cs
Shared/Repositories/IGenericRepository.cs, GenericRepository.cs, IUserContext.cs, HttpUserContext.cs
Data/AppDbContext.cs (Identity + SQLite)
Modules/Post/ — module mẫu tham chiếu
Modules/ApiLog/ApiLogModel.cs
Migrations/InitialCreate — đã apply
```

Agent thêm model mới **không cần tạo lại** foundation trên.

---

## 1. Khi nào dùng guide này

Dùng khi có yêu cầu thêm hoặc mở rộng **một bảng/entity nghiệp vụ mới**, ví dụ: `Post`, `Media`, `SocialChannel`, `Campaign`, `PublishLog`, ...

Không dùng guide này cho:
- ASP.NET Core Identity user/role (dùng Identity mặc định).
- Bảng log request/response hệ thống (module `ApiLog` riêng, xem mục 8).

---

## 2. Checklist nhanh

Khi nhận model mới, agent thực hiện **theo thứ tự**:

```text
[ ] 1. Đọc _ai_agent/database — xác nhận tên bảng, cột, quan hệ logic
[ ] 2. Tạo Entity (kế thừa BaseEntity)
[ ] 3. Tạo Enum (nếu có Status/Type/...)
[ ] 4. Đăng ký DbSet trong AppDbContext
[ ] 5. Cấu hình Fluent API (index, không FK constraint)
[ ] 6. Tạo DTOs (Create/Update/Response/Filter)
[ ] 7. Tạo Module Repository (kế thừa GenericRepository, bổ sung Filter/Create/Update DTO)
[ ] 8. Tạo Controller (chỉ orchestration, không query DB)
[ ] 9. Đăng ký DI trong Program.cs
[ ] 10. Chạy migration
[ ] 11. Cập nhật _ai_agent/database nếu schema thay đổi
[ ] 12. Liệt kê file đã tạo/sửa và endpoint mới
```

---

## 3. Input cần có trước khi code

Trước khi sinh code, agent phải xác định:

| Thông tin | Ví dụ | Nguồn |
|---|---|---|
| Tên module | `Post`, `SocialChannel` | business/database |
| Tên bảng | `Posts` | convention plural PascalCase |
| Field nghiệp vụ | `Title`, `Content`, `Status` | database spec |
| Quan hệ logic (không FK) | `UserId`, `CampaignId` | database spec |
| Enum cần thiết | `PostStatus`, `ChannelType` | field Status/Type |
| Endpoint cần có | CRUD + Filter | yêu cầu task |
| Field nhạy cảm | token, secret | không đưa vào Response DTO |

Nếu `_ai_agent/database` chưa có schema cho model mới → **đề xuất schema tối thiểu**, hỏi xác nhận hoặc ghi chú trong PR/commit message, rồi cập nhật file database sau khi implement.

---

## 4. Cấu trúc file cho một module mới

```text
backend/
  Modules/
    {ModuleName}/
      {ModuleName}Model.cs          # Entity
      {ModuleName}Dtos.cs           # Request/Response DTOs
      {ModuleName}Repository.cs     # Kế thừa GenericRepository, logic nghiệp vụ
      {ModuleName}Controller.cs     # API endpoints
      Enums/
        {ModuleName}Enums.cs        # (optional)
  Shared/
    BaseEntity.cs
    ApiResponse.cs
    PagedFilterRequest.cs
    PagedResult.cs
    Repositories/
      IGenericRepository.cs
      GenericRepository.cs
      IUserContext.cs
      HttpUserContext.cs
  Data/
    AppDbContext.cs
```

Nếu project chưa có thư mục `Modules/` hoặc `Shared/` → đã scaffold sẵn, các model sau reuse.

**Generic Repository đã có sẵn** — model mới chỉ cần tạo Entity + DTO + Module Repository + Controller.

---

## 5. Bước 1 — Entity (Model)

### 5.1. Kế thừa BaseEntity

Mọi entity nghiệp vụ **bắt buộc** kế thừa `BaseEntity`:

```csharp
public abstract class BaseEntity
{
    public Guid Id { get; set; }
    public string? ExtraJson { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public string? DeletedBy { get; set; }
}
```

### 5.2. Template entity mới

Ví dụ model `Post`:

```csharp
namespace Backend.Modules.Post;

public class PostModel : BaseEntity
{
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public PostStatus Status { get; set; } = PostStatus.Draft;

    // Quan hệ logic — KHÔNG dùng navigation property + FK constraint
    public Guid UserId { get; set; }
    public Guid? CampaignId { get; set; }
}
```

### 5.3. Quy tắc entity

- `Id`: `Guid`, tự sinh khi `Create`.
- Không dùng `[ForeignKey]`, `HasOne/WithMany` tạo FK constraint.
- Cột join (`UserId`, `PostId`, ...) nên có index nếu filter thường xuyên.
- String required: init `= string.Empty`, nullable dùng `?`.
- Enum: lưu `int` (mặc định EF Core).

---

## 6. Bước 2 — Enum (nếu có)

Gom enum của module vào một file:

```csharp
namespace Backend.Modules.Post.Enums;

public enum PostStatus
{
    Draft = 1,
    PendingReview = 2,
    Approved = 3,
    Published = 4,
    Rejected = 5
}
```

Đặt tên rõ nghĩa, bắt đầu từ `1` (tránh nhầm với default `0` nếu cần phân biệt).

---

## 7. Bước 3 — DbContext

### 7.1. Thêm DbSet

```csharp
public DbSet<PostModel> Posts => Set<PostModel>();
```

### 7.2. Fluent API (OnModelCreating)

```csharp
modelBuilder.Entity<PostModel>(entity =>
{
    entity.ToTable("Posts");
    entity.HasKey(e => e.Id);

    // Index cho cột join/filter — KHÔNG tạo FK constraint
    entity.HasIndex(e => e.UserId);
    entity.HasIndex(e => e.Status);
    entity.HasIndex(e => e.IsDeleted);

    entity.Property(e => e.Title).HasMaxLength(500);
});
```

**Không** dùng:
```csharp
entity.HasOne<...>().WithMany().HasForeignKey(...); // ❌
```

---

## 8. Bước 4 — DTOs

Một file `{ModuleName}Dtos.cs` cho toàn bộ DTO của module:

```csharp
namespace Backend.Modules.Post;

public class CreatePostRequest
{
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public Guid? CampaignId { get; set; }
}

public class UpdatePostRequest
{
    public string? Title { get; set; }
    public string? Content { get; set; }
    public PostStatus? Status { get; set; }
}

public class PostFilterRequest
{
    public string? Keyword { get; set; }
    public int Index { get; set; } = 1;
    public int Size { get; set; } = 20;
    public PostStatus? Status { get; set; }
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
}

public class PostResponse
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public PostStatus Status { get; set; }
    public Guid UserId { get; set; }
    public DateTime CreatedAt { get; set; }
}
```

### Quy tắc DTO

| Loại | Rule |
|---|---|
| `CreateXRequest` | Chỉ field client được gửi khi tạo |
| `UpdateXRequest` | Field nullable = optional patch |
| `XResponse` | Không có secret, hash, token |
| `XFilterRequest` | Luôn có `Keyword`, `Index`, `Size` |
| `XDetailResponse` | Dùng khi cần thêm field so với list response |

Mapping entity ↔ DTO làm trong Repository (method private `ToResponse`).

---

## 9. Bước 5 — Module Repository (Generic Repository)

Module Repository **kế thừa** `GenericRepository<TEntity>` — CRUD cơ bản đã có sẵn, chỉ bổ sung logic nghiệp vụ.

### 9.1. Generic Repository đã cung cấp

`IGenericRepository<TEntity>` / `GenericRepository<TEntity>`:

```text
GetAllAsync()
GetByIdAsync(Guid id)
CreateAsync(TEntity entity)
MultiCreateAsync(IEnumerable<TEntity> entities)
UpdateAsync(TEntity entity)
MultiUpdateAsync(IEnumerable<TEntity> entities)
SoftDeleteAsync(Guid id)
MultiSoftDeleteAsync(IEnumerable<Guid> ids)
QueryActive()                    # IQueryable chưa soft delete
PaginateAsync(query, index, size) # protected helper
ApplyCreateAudit / ApplyUpdateAudit / ApplySoftDeleteAudit
GetCurrentUserId / GetCurrentUserName
```

Module **không viết lại** các method trên trừ khi cần override hành vi.

### 9.2. Module Repository — method bổ sung

```text
FilterAsync(XFilterRequest request)           # QueryActive + filter + PaginateAsync
CreateAsync(CreateXRequest request)           # Map DTO → entity → base.CreateAsync
MultiCreateAsync(IEnumerable<CreateXRequest>)
UpdateAsync(Guid id, UpdateXRequest request)  # Patch field → ApplyUpdateAudit
ToResponse(XModel entity)                     # static, map → Response DTO
```

### 9.3. Template Module Repository

```csharp
public class PostRepository : GenericRepository<PostModel>, IGenericRepository<PostModel>
{
    public PostRepository(AppDbContext context, IUserContext userContext)
        : base(context, userContext) { }

    public async Task<PagedResult<PostResponse>> FilterAsync(
        PostFilterRequest request, CancellationToken ct = default)
    {
        var query = QueryActive();

        if (!string.IsNullOrWhiteSpace(request.Keyword))
        {
            var keyword = request.Keyword.Trim();
            query = query.Where(x =>
                x.Title.Contains(keyword) || x.Content.Contains(keyword));
        }

        if (request.Status.HasValue)
            query = query.Where(x => x.Status == request.Status.Value);

        var paged = await PaginateAsync(query, request.Index, request.Size, ct);
        return new PagedResult<PostResponse>
        {
            Items = paged.Items.Select(ToResponse).ToList(),
            Total = paged.Total,
            Index = paged.Index,
            Size = paged.Size
        };
    }

    public async Task<PostModel> CreateAsync(
        CreatePostRequest request, CancellationToken ct = default)
    {
        var entity = new PostModel
        {
            Title = request.Title.Trim(),
            Content = request.Content.Trim(),
            CampaignId = request.CampaignId,
            UserId = GetCurrentUserId(),
            Status = PostStatus.Draft
        };

        return await base.CreateAsync(entity, ct);
    }

    public async Task<PostModel?> UpdateAsync(
        Guid id, UpdatePostRequest request, CancellationToken ct = default)
    {
        var entity = await GetByIdAsync(id, ct);
        if (entity is null) return null;

        if (request.Title is not null) entity.Title = request.Title.Trim();
        if (request.Content is not null) entity.Content = request.Content.Trim();
        if (request.Status.HasValue) entity.Status = request.Status.Value;

        ApplyUpdateAudit(entity);
        await Context.SaveChangesAsync(ct);
        return entity;
    }

    public static PostResponse ToResponse(PostModel entity) => new()
    {
        Id = entity.Id,
        Title = entity.Title,
        Content = entity.Content,
        Status = entity.Status,
        UserId = entity.UserId,
        CampaignId = entity.CampaignId,
        CreatedAt = entity.CreatedAt,
        UpdatedAt = entity.UpdatedAt
    };
}
```

### 9.4. Module chỉ cần CRUD thuần

Nếu module không có Filter/Create/Update đặc biệt, inject trực tiếp:

```csharp
public class SimpleController(IGenericRepository<SimpleModel> repo) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken ct)
    {
        var items = await repo.GetAllAsync(ct);
        return Ok(ApiResponse.Ok(items));
    }
}
```

Không cần tạo `{Name}Repository.cs` riêng.

### 9.5. Validate quan hệ logic

Vì **không có FK constraint**, Module Repository phải tự kiểm tra:

```csharp
private async Task EnsureCampaignExistsAsync(Guid campaignId, CancellationToken ct)
{
    var exists = await Context.Set<CampaignModel>()
        .AnyAsync(x => x.Id == campaignId && !x.IsDeleted, ct);
    if (!exists)
        throw new InvalidOperationException("Campaign không tồn tại.");
}
```

---

## 10. Bước 6 — Controller

Controller **không** query database trực tiếp.

```csharp
[ApiController]
[Route("api/[controller]")]
public class PostController(PostRepository repository) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken ct)
    {
        var items = await repository.GetAllAsync(ct);
        return Ok(ApiResponse.Ok(items.Select(PostRepository.ToResponse).ToList()));
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        var entity = await repository.GetByIdAsync(id, ct);
        if (entity is null)
            return NotFound(ApiResponse.Fail("NOT_FOUND", "Không tìm thấy bài viết"));
        return Ok(ApiResponse.Ok(PostRepository.ToResponse(entity)));
    }

    [HttpPost("filter")]
    public async Task<IActionResult> Filter([FromBody] PostFilterRequest request, CancellationToken ct)
    {
        var result = await repository.FilterAsync(request, ct);
        return Ok(ApiResponse.Ok(result));
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreatePostRequest request, CancellationToken ct)
    {
        var entity = await repository.CreateAsync(request, ct);
        return CreatedAtAction(nameof(GetById), new { id = entity.Id },
            ApiResponse.Ok(PostRepository.ToResponse(entity), "Tạo thành công"));
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdatePostRequest request, CancellationToken ct)
    {
        var entity = await repository.UpdateAsync(id, request, ct);
        if (entity is null)
            return NotFound(ApiResponse.Fail("NOT_FOUND", "Không tìm thấy bài viết"));
        return Ok(ApiResponse.Ok(PostRepository.ToResponse(entity), "Cập nhật thành công"));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> SoftDelete(Guid id, CancellationToken ct)
    {
        var deleted = await repository.SoftDeleteAsync(id, ct);
        if (!deleted)
            return NotFound(ApiResponse.Fail("NOT_FOUND", "Không tìm thấy bài viết"));
        return Ok(ApiResponse.Ok("Xóa thành công"));
    }
}
```

### Endpoint convention gợi ý

| Method | Route | Mô tả |
|---|---|---|
| GET | `/api/{module}` | GetAll |
| GET | `/api/{module}/{id}` | GetById |
| POST | `/api/{module}/filter` | Filter + phân trang |
| POST | `/api/{module}` | Create |
| PUT | `/api/{module}/{id}` | Update |
| DELETE | `/api/{module}/{id}` | SoftDelete |

---

## 11. Bước 7 — Đăng ký DI (Program.cs)

```csharp
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("Default")));

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<IUserContext, HttpUserContext>();

// Generic Repository — fallback cho module CRUD thuần
builder.Services.AddScoped(typeof(IGenericRepository<>), typeof(GenericRepository<>));

// Module Repository — có logic nghiệp vụ riêng
builder.Services.AddScoped<PostRepository>();

builder.Services.AddControllers();
builder.Services.AddAuthentication(...);
builder.Services.AddAuthorization();
```

**Quy tắc DI:**
- Luôn đăng ký `IGenericRepository<>` + `GenericRepository<>`.
- Module có Filter/Create/Update DTO → đăng ký thêm `{Name}Repository`.
- Controller inject `{Name}Repository` (không inject `IGenericRepository<>` nếu module có repo riêng).

---

## 12. Bước 8 — Migration

```bash
cd backend
dotnet ef migrations add AddPostModule
dotnet ef database update
```

Quy tắc:
- Một migration gom các thay đổi liên quan cùng module nếu làm một lần.
- Không sửa tay migration trừ khi cần thêm index.
- Không thêm FK constraint vào migration.

---

## 13. Bước 9 — Cập nhật `_ai_agent/database`

Sau khi entity chốt, cập nhật file database với schema thực tế:

```text
## Posts
- Id: Guid (PK)
- Title: string(500)
- Content: text
- Status: int (PostStatus enum)
- UserId: Guid (index, no FK)
- CampaignId: Guid? (index, no FK)
- + BaseEntity fields
```

Giữ file này là **single source of truth** cho agent lần sau.

---

## 14. Module đặc biệt: ApiLog

Bảng log request/response không theo CRUD thông thường. Tạo module `ApiLog` riêng:

- **Không** expose endpoint CRUD công khai cho client.
- Ghi log qua middleware hoặc action filter.
- Sanitize payload trước khi lưu (không log password, token, secret).
- Truncate payload lớn.

Field tham chiếu: xem mục 10 trong `sysPromptCoding_Backend.md`.

---

## 15. Naming convention tổng hợp

| Thành phần | Convention | Ví dụ |
|---|---|---|
| Entity class | `{Name}Model` | `PostModel` |
| DbSet / Table | Plural | `Posts` |
| Repository (module) | `{Name}Repository : GenericRepository<{Name}Model>` | `PostRepository` |
| Generic Repository | `IGenericRepository<T>` / `GenericRepository<T>` | `IGenericRepository<PostModel>` |
| Controller | `{Name}Controller` | `PostController` |
| DTO file | `{Name}Dtos.cs` | `PostDtos.cs` |
| Create DTO | `Create{Name}Request` | `CreatePostRequest` |
| Update DTO | `Update{Name}Request` | `UpdatePostRequest` |
| Response DTO | `{Name}Response` | `PostResponse` |
| Filter DTO | `{Name}FilterRequest` | `PostFilterRequest` |
| Enum file | `{Name}Enums.cs` | `PostEnums.cs` |
| Async method | suffix `Async` | `GetByIdAsync` |

---

## 16. Output agent phải báo cáo sau khi xong

Sau mỗi task thêm model, agent liệt kê:

```text
## Model: Post

### Files created
- backend/Modules/Post/PostModel.cs
- backend/Modules/Post/PostDtos.cs
- backend/Modules/Post/PostRepository.cs
- backend/Modules/Post/PostController.cs
- backend/Modules/Post/Enums/PostEnums.cs

### Files modified
- backend/Data/AppDbContext.cs
- backend/Program.cs
- _ai_agent/database

### Shared (đã có sẵn, không tạo lại)
- backend/Shared/Repositories/IGenericRepository.cs
- backend/Shared/Repositories/GenericRepository.cs

### Endpoints
- GET    /api/post
- GET    /api/post/{id}
- POST   /api/post/filter
- POST   /api/post
- PUT    /api/post/{id}
- DELETE /api/post/{id}

### Migration
- AddPostModule

### Notes
- UserId lấy từ authenticated context, không từ client
- Validate CampaignId tồn tại trước khi create
```

---

## 17. Anti-patterns — KHÔNG làm

| ❌ Không | ✅ Thay bằng |
|---|---|
| Query DB trong Controller | Gọi Repository |
| Trả Entity trực tiếp | Map sang Response DTO |
| Hard delete | Soft delete |
| FK constraint trong migration | Cột Guid + index + validate trong Repository |
| Navigation property với cascade | Query riêng khi cần join |
| Trust `CreatedBy` từ client | Lấy từ `GetCurrentUserId()` |
| Log password/token | Sanitize trước khi ghi ApiLog |
| Viết lại CRUD trong Module Repository | Kế thừa GenericRepository, chỉ bổ sung Filter/Create/Update DTO |
| Tạo BaseRepository riêng | Dùng GenericRepository có sẵn trong Shared/Repositories |
| Tự đổi sang PostgreSQL/SQL Server | Giữ SQLite theo stack |

---

## 18. Quan hệ với các tài liệu khác

```text
system_prompt.md              → Bối cảnh sản phẩm, mục tiêu nghiệp vụ
sysPromptCoding_Backend.md    → Convention kỹ thuật, security, style
backend_guide.md (file này)   → Quy trình step-by-step khi thêm model
_ai_agent/database            → Schema reference, cập nhật sau mỗi model
_ai_agent/business/           → Yêu cầu nghiệp vụ chi tiết (nếu có)
```

**Thứ tự đọc khi nhận task mới:**
1. `system_prompt.md`
2. `_ai_agent/database` + business docs
3. `sysPromptCoding_Backend.md`
4. `backend_guide.md` (file này)
5. Code hiện có trong `backend/`
