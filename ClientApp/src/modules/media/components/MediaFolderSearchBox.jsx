import { useState } from 'react'
import { getErrorMessage } from '@/shared/utils/apiHelpers'
import { useMediaFolderGlobalSearch } from '../hooks/useMediaFolders'

const RECENT_LIMIT = 5

/**
 * Global folder search: tìm thư mục theo tên trên tất cả các Page mà actor có quyền,
 * hiển thị Page name + full path để phân biệt folder trùng tên giữa các Page. Chọn
 * kết quả gọi onOpenFolder với cả folderId lẫn socialChannelId để điều hướng + đặt Page.
 * Quick access: danh sách vài folder vừa mở gần nhất (lưu trong bộ nhớ, không endpoint).
 */
export default function MediaFolderSearchBox({ onOpenFolder }) {
  const [keyword, setKeyword] = useState('')
  const [recent, setRecent] = useState([])

  const searchQuery = useMediaFolderGlobalSearch({ keyword })
  const results = searchQuery.data?.items ?? []

  const handleOpen = (folder) => {
    onOpenFolder?.(folder.id, folder.socialChannelId)
    setKeyword('')
    setRecent((prev) => [
      { id: folder.id, name: folder.name, pageName: folder.pageName, socialChannelId: folder.socialChannelId },
      ...prev.filter((r) => r.id !== folder.id),
    ].slice(0, RECENT_LIMIT))
  }

  return (
    <div className="media-folder-search">
      <input
        type="search"
        value={keyword}
        placeholder="Tìm thư mục..."
        aria-label="Tìm thư mục"
        onChange={(event) => setKeyword(event.target.value)}
      />

      {keyword && (
        <div className="media-folder-search-results">
          {searchQuery.isLoading && <p className="media-folder-picker-hint">Đang tìm...</p>}
          {searchQuery.isError && (
            <p className="media-folder-picker-hint">{getErrorMessage(searchQuery.error, 'Không tìm được thư mục')}</p>
          )}
          {!searchQuery.isLoading && !searchQuery.isError && results.length === 0 && (
            <p className="media-folder-picker-hint">Không tìm thấy thư mục nào khớp.</p>
          )}
          {results.map((folder) => (
            <button
              key={folder.id}
              type="button"
              className="media-folder-search-result"
              onClick={() => handleOpen(folder)}
            >
              <span className="media-folder-name">📁 {folder.name}</span>
              <div className="media-folder-search-path-wrapper">
                <span className="media-folder-search-path">{folder.fullPath}</span>
                {folder.pageName && <span className="media-folder-search-page-name">{folder.pageName}</span>}
              </div>
            </button>
          ))}
        </div>
      )}

      {!keyword && recent.length > 0 && (
        <div className="media-folder-search-recent">
          <span className="media-folder-search-recent-label">Truy cập gần đây</span>
          {recent.map((folder) => (
            <button
              key={folder.id}
              type="button"
              className="badge media-folder-search-recent-chip"
              onClick={() => handleOpen(folder)}
            >
              📁 {folder.name} {folder.pageName && `(${folder.pageName})`}
            </button>
          ))}
        </div>
      )}
    </div>
  )
}
