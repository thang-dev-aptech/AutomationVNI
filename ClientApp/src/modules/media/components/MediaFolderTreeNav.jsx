import { useRef, useState } from 'react'
import { useMediaFolderChildren } from '../hooks/useMediaFolders'

const TREE_PAGE_SIZE = 100

/**
 * TreeNode của sidebar tree: lazy-loads children khi được expand.
 * Khác với PickerTree, TreeNav:
 * - onOpen để navigate (không expand)
 * - onCreateChild/onRename/onDelete cho quản lý
 * - onMoveAsset cho drag-drop assets vào folder
 * - expandedFolderIds từ hook để persist trạng thái expand
 */
function TreeNode({
  folder,
  depth,
  socialChannelId,
  currentFolderId,
  expandedFolderIds,
  onToggleExpand,
  onOpen,
  onMoveAsset,
  onCreateChild,
  onRename,
  onDelete,
  canManage,
}) {
  const isExpanded = expandedFolderIds.has(folder.id)
  const isActive = currentFolderId === folder.id
  const [dragOver, setDragOver] = useState(false)

  // Từng mở rộng ít nhất 1 lần thì giữ query "enabled" luôn — thu gọn/mở lại chỉ
  // ẩn/hiện <ul>, không re-fetch (staleTime:0 sẽ fetch lại mỗi lần enabled bật lại
  // nếu để enabled đi theo đúng isExpanded).
  const hasExpandedOnceRef = useRef(isExpanded)
  if (isExpanded) hasExpandedOnceRef.current = true

  const childrenQuery = useMediaFolderChildren({
    socialChannelId,
    parentFolderId: folder.id,
    size: TREE_PAGE_SIZE,
    enabled: hasExpandedOnceRef.current,
  })

  const children = childrenQuery.data?.items ?? []
  const hasChildren = folder.hasChildren || children.length > 0

  const handleDrop = (event) => {
    event.preventDefault()
    event.stopPropagation()
    setDragOver(false)
    const assetId = event.dataTransfer.getData('text/media-asset-id')
    if (assetId) onMoveAsset?.(assetId, folder.id)
  }

  const stopPropagation = (handler) => (event) => {
    event.stopPropagation()
    handler?.()
  }

  const indent = 8 + depth * 16

  return (
    <li>
      <div
        className={`media-folder-row${isActive ? ' is-active' : ''}${dragOver ? ' is-dragover' : ''}`}
        style={{ paddingLeft: indent }}
        onDragOver={(event) => {
          event.preventDefault()
          setDragOver(true)
        }}
        onDragLeave={() => setDragOver(false)}
        onDrop={handleDrop}
      >
        {hasChildren && (
          <button
            type="button"
            className="media-folder-toggle"
            onClick={() => onToggleExpand(folder.id)}
            aria-label={`${isExpanded ? 'Thu gọn' : 'Mở rộng'} thư mục ${folder.name}`}
            aria-expanded={isExpanded}
          >
            {isExpanded ? '▾' : '▸'}
          </button>
        )}
        {!hasChildren && <span className="media-folder-toggle">·</span>}

        <button
          type="button"
          className="media-folder-name"
          onClick={() => onOpen(folder.id)}
          title={folder.name}
        >
          📁 {folder.name}
        </button>

        {canManage && (
          <span className="media-folder-tools">
            <button
              type="button"
              title="Tạo thư mục con"
              onClick={stopPropagation(() => onCreateChild?.(folder.id))}
            >
              ＋
            </button>
            <button
              type="button"
              title="Đổi tên"
              onClick={stopPropagation(() => onRename?.(folder))}
            >
              ✎
            </button>
            <button
              type="button"
              title="Xóa"
              onClick={stopPropagation(() => onDelete?.(folder))}
            >
              🗑
            </button>
          </span>
        )}
      </div>

      {isExpanded && (
        <ul className="media-folder-children">
          {childrenQuery.isLoading && (
            <li className="media-folder-row-status" style={{ paddingLeft: 8 + (depth + 1) * 16 }}>
              Đang tải...
            </li>
          )}
          {childrenQuery.isError && (
            <li className="media-folder-row-status" style={{ paddingLeft: 8 + (depth + 1) * 16 }}>
              Lỗi tải thư mục con.{' '}
              <button
                type="button"
                className="media-folder-retry"
                onClick={() => childrenQuery.refetch()}
              >
                Thử lại
              </button>
            </li>
          )}
          {!childrenQuery.isLoading && !childrenQuery.isError && children.length === 0 && (
            <li className="media-folder-row-status" style={{ paddingLeft: 8 + (depth + 1) * 16 }}>
              Không có thư mục con
            </li>
          )}
          {children.map((child) => (
            <TreeNode
              key={child.id}
              folder={child}
              depth={depth + 1}
              socialChannelId={socialChannelId}
              currentFolderId={currentFolderId}
              expandedFolderIds={expandedFolderIds}
              onToggleExpand={onToggleExpand}
              onOpen={onOpen}
              onMoveAsset={onMoveAsset}
              onCreateChild={onCreateChild}
              onRename={onRename}
              onDelete={onDelete}
              canManage={canManage}
            />
          ))}
        </ul>
      )}
    </li>
  )
}

/**
 * Sidebar tree navigation: persistent expandable tree với root tải eager.
 * Không giao diện picker/modal — chỉ là navigation tree cho sidebar.
 * expandedFolderIds quản lý từ useMediaFolderExplorer (hook-expand-state task).
 */
export default function MediaFolderTreeNav({
  socialChannelId,
  currentFolderId = null,
  expandedFolderIds = new Set(),
  onToggleExpand,
  onOpen,
  onMoveAsset,
  onCreateChild,
  onRename,
  onDelete,
  canManage = false,
}) {
  const rootQuery = useMediaFolderChildren({
    socialChannelId,
    parentFolderId: null,
    size: TREE_PAGE_SIZE,
    enabled: Boolean(socialChannelId),
  })

  const roots = rootQuery.data?.items ?? []

  if (!socialChannelId) {
    return <p style={{ fontSize: '0.85rem', color: 'var(--color-text-muted)' }}>Chọn Page để xem thư mục</p>
  }

  return (
    <div className="media-folder-tree-nav">
      {rootQuery.isLoading && (
        <p style={{ fontSize: '0.85rem', color: 'var(--color-text-muted)' }}>Đang tải thư mục...</p>
      )}
      {rootQuery.isError && (
        <p style={{ fontSize: '0.85rem', color: 'var(--color-text-muted)' }}>
          Không tải được danh sách thư mục.{' '}
          <button
            type="button"
            className="media-folder-retry"
            onClick={() => rootQuery.refetch()}
          >
            Thử lại
          </button>
        </p>
      )}

      {!rootQuery.isLoading && !rootQuery.isError && (
        <ul className="media-folder-roots">
          {roots.map((folder) => (
            <TreeNode
              key={folder.id}
              folder={folder}
              depth={0}
              socialChannelId={socialChannelId}
              currentFolderId={currentFolderId}
              expandedFolderIds={expandedFolderIds}
              onToggleExpand={onToggleExpand}
              onOpen={onOpen}
              onMoveAsset={onMoveAsset}
              onCreateChild={onCreateChild}
              onRename={onRename}
              onDelete={onDelete}
              canManage={canManage}
            />
          ))}
        </ul>
      )}
    </div>
  )
}
