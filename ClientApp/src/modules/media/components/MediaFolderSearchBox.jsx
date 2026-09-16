import { useState } from 'react'
import { getErrorMessage } from '@/shared/utils/apiHelpers'
import { useMediaFolderSearch } from '../hooks/useMediaFolders'

const RECENT_LIMIT = 5

/**
 * MEDIA-05: tìm thư mục theo tên trong Page hiện tại, hiển thị full path để phân biệt
 * tên trùng; chọn kết quả mở đúng folder qua onOpenFolder. Quick access: danh sách vài
 * folder vừa mở gần nhất trong phiên hiện tại (không có backend endpoint riêng cho việc
 * này, và business text để "UX được thống nhất" — giữ tối giản, chỉ lưu trong bộ nhớ).
 *
 * `boundPageId`/`pageChanged` theo đúng pattern useMediaFolderExplorer: đổi Page xoá
 * keyword ngay trong render hiện tại (không phải effect), nên response cũ đến muộn của
 * Page trước sẽ rơi vào query key khác và không bao giờ được set lại lên UI (MEDIA-05-AC2).
 */
export default function MediaFolderSearchBox({ socialChannelId, onOpenFolder }) {
  const [boundPageId, setBoundPageId] = useState(socialChannelId)
  const [keyword, setKeyword] = useState('')
  const [recent, setRecent] = useState([])

  const pageChanged = socialChannelId !== boundPageId
  if (pageChanged) {
    setBoundPageId(socialChannelId)
    setKeyword('')
    setRecent([])
  }
  const activeKeyword = pageChanged ? '' : keyword

  const searchQuery = useMediaFolderSearch({ socialChannelId, keyword: activeKeyword })
  const results = searchQuery.data?.items ?? []

  const handleOpen = (folder) => {
    onOpenFolder?.(folder.id)
    setKeyword('')
    setRecent((prev) => [
      { id: folder.id, name: folder.name },
      ...prev.filter((r) => r.id !== folder.id),
    ].slice(0, RECENT_LIMIT))
  }

  if (!socialChannelId) return null

  return (
    <div className="media-folder-search">
      <input
        type="search"
        value={activeKeyword}
        placeholder="Tìm thư mục trong Page..."
        aria-label="Tìm thư mục trong Page"
        onChange={(event) => setKeyword(event.target.value)}
      />

      {activeKeyword && (
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
              <span className="media-folder-search-path">{folder.fullPath}</span>
            </button>
          ))}
        </div>
      )}

      {!activeKeyword && recent.length > 0 && (
        <div className="media-folder-search-recent">
          <span className="media-folder-search-recent-label">Truy cập gần đây</span>
          {recent.map((folder) => (
            <button
              key={folder.id}
              type="button"
              className="badge media-folder-search-recent-chip"
              onClick={() => handleOpen(folder)}
            >
              📁 {folder.name}
            </button>
          ))}
        </div>
      )}
    </div>
  )
}
