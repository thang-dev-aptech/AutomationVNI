import { useState } from 'react'
import { useMediaFolderChildren } from '../hooks/useMediaFolders'

const PICKER_PAGE_SIZE = 100

/**
 * Node của picker: chỉ gọi API con khi tự expand (MEDIA-07), không tải cả cây.
 * `excludeFolderId` (folder đang sửa) bị khoá — không cho chọn lẫn không cho expand,
 * nên toàn bộ nhánh con của nó không bao giờ lộ ra để chọn (chặn cycle mà không cần biết
 * trước danh sách descendant).
 */
function PickerNode({ folder, depth, socialChannelId, value, onChange, excludeFolderId }) {
  const [expanded, setExpanded] = useState(false)
  const isExcluded = folder.id === excludeFolderId
  const isSelected = value === folder.id

  const childrenQuery = useMediaFolderChildren({
    socialChannelId,
    parentFolderId: folder.id,
    size: PICKER_PAGE_SIZE,
    enabled: expanded && !isExcluded,
  })
  const children = childrenQuery.data?.items ?? []
  const indent = 8 + depth * 16
  const childIndent = 8 + (depth + 1) * 16

  return (
    <li>
      <div
        className={`media-folder-row${isSelected ? ' is-active' : ''}${isExcluded ? ' is-disabled' : ''}`}
        style={{ paddingLeft: indent }}
      >
        <button
          type="button"
          className="media-folder-toggle"
          disabled={isExcluded}
          onClick={() => setExpanded((v) => !v)}
          aria-label={expanded ? 'Thu gọn' : 'Mở rộng'}
        >
          {folder.hasChildren ? (expanded ? '▾' : '▸') : '·'}
        </button>
        <button
          type="button"
          className="media-folder-name media-folder-picker-select"
          disabled={isExcluded}
          onClick={() => onChange(folder.id)}
          title={isExcluded ? 'Không thể chọn chính thư mục đang sửa' : folder.name}
        >
          📁 {folder.name}
        </button>
      </div>
      {expanded && !isExcluded && (
        <ul className="media-folder-children">
          {childrenQuery.isLoading && (
            <li className="media-folder-row-status" style={{ paddingLeft: childIndent }}>Đang tải...</li>
          )}
          {childrenQuery.isError && (
            <li className="media-folder-row-status" style={{ paddingLeft: childIndent }}>
              Lỗi tải thư mục con.{' '}
              <button type="button" className="media-folder-retry" onClick={() => childrenQuery.refetch()}>
                Thử lại
              </button>
            </li>
          )}
          {!childrenQuery.isLoading && !childrenQuery.isError && children.length === 0 && (
            <li className="media-folder-row-status" style={{ paddingLeft: childIndent }}>Không có thư mục con</li>
          )}
          {children.map((child) => (
            <PickerNode
              key={child.id}
              folder={child}
              depth={depth + 1}
              socialChannelId={socialChannelId}
              value={value}
              onChange={onChange}
              excludeFolderId={excludeFolderId}
            />
          ))}
        </ul>
      )}
    </li>
  )
}

/**
 * Picker chọn parent/move-destination cho một MediaFolder, luôn scope theo một Page
 * (`socialChannelId`) — không có prop nào cho phép hiển thị folder Page khác.
 * Chỉ tải một cấp mỗi lần (root khi mount, cấp dưới khi node tự expand); không gọi
 * GET /api/MediaFolder/tree. Server (EnsureNoCycleAsync/UpdateAsync) vẫn là chốt chặn
 * cuối cùng cho cycle/cross-Page — picker chỉ chặn ở UI để trải nghiệm không cho chọn sai.
 */
export default function MediaFolderPickerTree({
  socialChannelId,
  value = null,
  onChange,
  excludeFolderId = null,
}) {
  const rootQuery = useMediaFolderChildren({
    socialChannelId,
    parentFolderId: null,
    size: PICKER_PAGE_SIZE,
    enabled: Boolean(socialChannelId),
  })
  const roots = rootQuery.data?.items ?? []

  if (!socialChannelId) {
    return <p className="media-folder-picker-hint">Chọn Page trước để chọn thư mục cha.</p>
  }

  return (
    <div className="media-folder-picker">
      <button
        type="button"
        className={`media-folder-row is-fixed${value === null ? ' is-active' : ''}`}
        onClick={() => onChange(null)}
      >
        <span className="media-folder-name">— Thư mục gốc —</span>
      </button>

      {rootQuery.isLoading && <p className="media-folder-picker-hint">Đang tải thư mục...</p>}
      {rootQuery.isError && (
        <p className="media-folder-picker-hint">
          Không tải được danh sách thư mục.{' '}
          <button type="button" className="media-folder-retry" onClick={() => rootQuery.refetch()}>
            Thử lại
          </button>
        </p>
      )}

      {!rootQuery.isLoading && !rootQuery.isError && (
        <ul className="media-folder-roots">
          {roots.map((folder) => (
            <PickerNode
              key={folder.id}
              folder={folder}
              depth={0}
              socialChannelId={socialChannelId}
              value={value}
              onChange={onChange}
              excludeFolderId={excludeFolderId}
            />
          ))}
        </ul>
      )}
    </div>
  )
}
