import { useState } from 'react'
import { getErrorMessage } from '@/shared/utils/apiHelpers'
import { useMediaFolderGlobalSearch } from '../hooks/useMediaFolders'
import { useMediaAssets } from '../hooks/useMediaAssets'

const RECENT_LIMIT = 5
const FILE_RESULT_LIMIT = 5

/**
 * Search chung: gõ 1 ô tìm cả thư mục (mọi Page actor có quyền) lẫn tệp (theo tên/alt/tags,
 * không giới hạn Page/folder đang xem), gộp thành 1 dropdown. Chọn thư mục → điều hướng vào đó
 * (onOpenFolder, cần cả folderId lẫn socialChannelId để suy Page). Chọn tệp → điều hướng tới
 * folder chứa nó (onOpenFile, cần asset kèm folderId/socialChannelId do backend join sẵn) rồi
 * hiện trong lưới bên dưới — tệp chưa phân loại thì onOpenFile tự chuyển sang bộ lọc "Chưa phân
 * loại". `value`/`onChange` được điều khiển từ MediaPage vì cùng 1 từ khoá còn lọc trực tiếp
 * lưới tệp bên dưới (không cần bấm vào kết quả mới thấy).
 * Quick access: danh sách vài folder vừa mở gần nhất (lưu trong bộ nhớ, không endpoint).
 */
export default function MediaFolderSearchBox({ value, onChange, onOpenFolder, onOpenFile }) {
  const [recent, setRecent] = useState([])

  const trimmed = value.trim()
  const folderQuery = useMediaFolderGlobalSearch({ keyword: value })
  const folderResults = folderQuery.data?.items ?? []

  const fileQuery = useMediaAssets(
    { keyword: trimmed, index: 1, size: FILE_RESULT_LIMIT },
    { enabled: Boolean(trimmed) },
  )
  const fileResults = trimmed ? (fileQuery.data?.items ?? []) : []
  const fileTotal = fileQuery.data?.total ?? 0

  const handleOpenFolder = (folder) => {
    onOpenFolder?.(folder.id, folder.socialChannelId)
    onChange?.('')
    setRecent((prev) => [
      { id: folder.id, name: folder.name, pageName: folder.pageName, socialChannelId: folder.socialChannelId },
      ...prev.filter((r) => r.id !== folder.id),
    ].slice(0, RECENT_LIMIT))
  }

  const handleOpenFile = (asset) => {
    onOpenFile?.(asset)
    onChange?.('')
  }

  return (
    <div className="media-folder-search">
      <input
        type="search"
        value={value}
        placeholder="Tìm thư mục hoặc tệp..."
        aria-label="Tìm thư mục hoặc tệp"
        onChange={(event) => onChange?.(event.target.value)}
      />

      {value && (
        <div className="media-folder-search-results">
          <p className="media-folder-search-section-label">Thư mục</p>
          {folderQuery.isLoading && <p className="media-folder-picker-hint">Đang tìm...</p>}
          {folderQuery.isError && (
            <p className="media-folder-picker-hint">{getErrorMessage(folderQuery.error, 'Không tìm được thư mục')}</p>
          )}
          {!folderQuery.isLoading && !folderQuery.isError && folderResults.length === 0 && (
            <p className="media-folder-picker-hint">Không tìm thấy thư mục nào khớp.</p>
          )}
          {folderResults.map((folder) => (
            <button
              key={folder.id}
              type="button"
              className="media-folder-search-result"
              onClick={() => handleOpenFolder(folder)}
            >
              <span className="media-folder-name">📁 {folder.name}</span>
              <div className="media-folder-search-path-wrapper">
                <span className="media-folder-search-path">{folder.fullPath}</span>
                {folder.pageName && <span className="media-folder-search-page-name">{folder.pageName}</span>}
              </div>
            </button>
          ))}

          <p className="media-folder-search-section-label">Tệp</p>
          {fileQuery.isLoading && <p className="media-folder-picker-hint">Đang tìm...</p>}
          {fileQuery.isError && (
            <p className="media-folder-picker-hint">{getErrorMessage(fileQuery.error, 'Không tìm được tệp')}</p>
          )}
          {!fileQuery.isLoading && !fileQuery.isError && fileResults.length === 0 && (
            <p className="media-folder-picker-hint">Không tìm thấy tệp nào khớp.</p>
          )}
          {fileResults.map((asset) => (
            <button
              key={asset.id}
              type="button"
              className="media-folder-search-result media-folder-search-file-result"
              onClick={() => handleOpenFile(asset)}
            >
              <img className="media-folder-search-file-thumb" src={asset.previewUrl} alt="" />
              <div className="media-folder-search-path-wrapper">
                <span className="media-folder-name">{asset.originalFileName || asset.fileName}</span>
                {!asset.folderId && <span className="media-folder-search-path">Chưa phân loại</span>}
              </div>
            </button>
          ))}
          {!fileQuery.isLoading && fileTotal > FILE_RESULT_LIMIT && (
            <p className="media-folder-picker-hint">
              +{fileTotal - FILE_RESULT_LIMIT} tệp khác khớp — xem lưới bên dưới.
            </p>
          )}
        </div>
      )}

      {!value && recent.length > 0 && (
        <div className="media-folder-search-recent">
          <span className="media-folder-search-recent-label">Truy cập gần đây</span>
          {recent.map((folder) => (
            <button
              key={folder.id}
              type="button"
              className="badge media-folder-search-recent-chip"
              onClick={() => handleOpenFolder(folder)}
            >
              📁 {folder.name} {folder.pageName && `(${folder.pageName})`}
            </button>
          ))}
        </div>
      )}
    </div>
  )
}
