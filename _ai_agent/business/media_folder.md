# Folder media + kéo-thả ảnh vào folder

Cho phép phân loại kho Media bằng thư mục (lồng nhiều cấp) và kéo-thả ảnh vào folder.
Use case chính: tạo folder "Logo" để gom logo, khi cấu hình PageContext lọc theo folder cho
dễ tìm.

> Liên quan: `coding/backend_guide.md` (quy trình thêm module), module `Category` (mẫu tham
> chiếu), `sinh_anh_tu_caption.md`.

## Quyết định đã chốt
- **MediaFolder là entity riêng** (không dùng lại Category để tránh lẫn với danh mục bài viết).
- **Lồng nhiều cấp** qua `ParentFolderId`.
- **PageContext picker logo lọc theo folder**.
- Kéo-thả bằng **HTML5 drag-drop gốc**, không thêm thư viện.

## A. Backend

### Module mới `Modules/MediaFolder/`
| File | Nội dung |
|---|---|
| `MediaFolderModel.cs` | `Name`, `Description?`, `ParentFolderId Guid?`, `SortOrder int` (: BaseEntity) |
| `MediaFolderDtos.cs` | Create/Update/Filter/Response; Response kèm `AssetCount`, `HasChildren`, `Depth` |
| `MediaFolderRepository.cs` | `: GenericRepository<MediaFolderModel>` + `FilterAsync`, `CreateAsync`, `UpdateAsync`; validate parent tồn tại + chống lặp cây (parent ≠ chính nó/hậu duệ) |
| `MediaFolderController.cs` | CRUD (BaseController) + `GET /api/mediafolder/tree` |

### Sửa MediaAsset
- `MediaAssetModel.cs`: thêm `Guid? FolderId` + index.
- `MediaAssetDtos.cs`: thêm `FolderId` vào Create/Update/Filter/Response.
- `MediaAssetRepository.cs`: `FilterAsync` lọc theo `FolderId`; thêm `MoveAsync(ids[], folderId)`; `ToResponse` trả `FolderId`.
- `MediaAssetController.cs`: `Upload` nhận thêm `folderId`; thêm `POST /api/mediaasset/move` `{ ids[], folderId }` (bulk, cho kéo-thả).

### Hạ tầng
- `Data/AppDbContext.cs`: `DbSet<MediaFolderModel>` + Fluent (index `Name`, `ParentFolderId`, `IsDeleted`, `FolderId`; KHÔNG FK).
- `Program.cs`: đăng ký `MediaFolderRepository`.
- Migration `AddMediaFolders` (bảng mới + cột `MediaAsset.FolderId`).
- Cập nhật `_ai_agent/database`.

## B. Frontend

### Tạo mới
- `services/mediaFolderApi.js`, `hooks/useMediaFolders.js` (tree, create, rename, delete, move).
- `components/MediaFolderTree.jsx` (+ css): cây expand/collapse; mục cố định "Tất cả" + "Chưa phân loại"; mỗi node là drop target.
- `components/MediaFolderFormModal.jsx`: tạo / đổi tên folder (chọn folder cha).

### Sửa
- `MediaPage.jsx`: layout 2 cột (sidebar cây trái + grid phải); chọn folder → set `folderId` filter; nút "Tạo folder".
- `MediaAssetCard.jsx` / `MediaGrid.jsx`: card `draggable`, `onDragStart` set assetId.
- Drop lên folder → gọi `move` → refetch.
- `MediaUploadForm.jsx`: thêm select folder (mặc định = folder đang mở).
- Popup Chi tiết: thêm select "Chuyển thư mục" (fallback khi không kéo-thả được).
- `PageContextFormModal.jsx`: thêm select lọc folder phía trên select logo; `logoOptions` lọc theo folder (client-side từ `useMediaAssetAll`).

## C. Edge cases
- Xóa folder còn ảnh → ảnh về "Chưa phân loại" (`FolderId = null`); còn thư mục con → chặn, xử lý con trước.
- Đổi parent tạo vòng lặp → repository từ chối.
- Kéo ảnh vào đúng folder đang chứa → no-op.
- Quyền: chỉ `canManageMedia` mới tạo/sửa/xóa/di chuyển; người xem chỉ lọc.
- Lọc gồm thư mục con: v1 chỉ ảnh trực tiếp; toggle "gồm thư mục con" để bản sau.

## D. Thứ tự triển khai (mỗi bước build/test được)
```
[x] 1. Backend: MediaFolder module + MediaAsset.FolderId + migration
[x] 2. Backend: endpoint move + upload folderId + filter folder
[x] 3. FE: folder tree sidebar + filter (chưa kéo-thả)
[x] 4. FE: kéo-thả ảnh → move; upload chọn folder
[x] 5. FE: PageContext picker lọc folder
[x] 6. Cập nhật _ai_agent/database + doc
```

## Trạng thái
- Đã duyệt plan (2026-07-22). Đã implement xong 6 bước; backend + frontend build pass,
  migration `AddMediaFolders` đã apply. Chưa test runtime end-to-end (cần chạy app).

### File đã tạo/sửa
Backend tạo: `Modules/MediaFolder/{MediaFolderModel,MediaFolderDtos,MediaFolderRepository,MediaFolderController}.cs`
Backend sửa: `Data/AppDbContext.cs`, `Program.cs`, `Modules/MediaAsset/{MediaAssetModel,MediaAssetDtos,MediaAssetRepository,MediaAssetController}.cs`, migration `AddMediaFolders`.
FE tạo: `media/services/mediaFolderApi.js`, `media/hooks/useMediaFolders.js`, `media/components/{MediaFolderTree,MediaFolderFormModal}.jsx`.
FE sửa: `media/pages/MediaPage.jsx` (+css), `media/components/{MediaAssetCard,MediaGrid,MediaUploadForm}.jsx`, `media/services/mediaAssetApi.js`, `media/hooks/useMediaAssets.js`, `page-contexts/components/PageContextFormModal.jsx`.
