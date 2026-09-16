import { useState } from 'react'
import EmptyState from '@/shared/components/EmptyState'
import ErrorState from '@/shared/components/ErrorState'
import LoadingState from '@/shared/components/LoadingState'
import { getErrorMessage } from '@/shared/utils/apiHelpers'
import './MediaFolderExplorer.css'

function readAssetId(event) {
  return event.dataTransfer.getData('text/media-asset-id') || null
}

function folderCountsLabel(folder) {
  const childCount = folder.childFolderCount ?? 0
  const assetCount = folder.directAssetCount ?? folder.assetCount ?? 0
  return `${childCount} thư mục con · ${assetCount} ảnh`
}

function FolderCard({
  folder,
  viewMode,
  isActive,
  canManage,
  onOpen,
  onMoveAsset,
  onCreateChild,
  onRename,
  onDelete,
}) {
  const [dragOver, setDragOver] = useState(false)
  const hasChildren = Boolean(folder.hasChildren)

  const handleDrop = (event) => {
    event.preventDefault()
    event.stopPropagation()
    setDragOver(false)
    const assetId = readAssetId(event)
    if (assetId) onMoveAsset?.(assetId, folder.id)
  }

  const className = viewMode === 'grid'
    ? `media-folder-card${isActive ? ' is-active' : ''}${dragOver ? ' is-dragover' : ''}`
    : `media-folder-row${isActive ? ' is-active' : ''}${dragOver ? ' is-dragover' : ''}`

  // Toàn bộ card/row là vùng bấm để mở folder (không chỉ riêng tên) — nút quản lý bên trong
  // stopPropagation để không vô tình mở folder khi bấm xóa/đổi tên/tạo con.
  const stopAnd = (handler) => (event) => { event.stopPropagation(); handler?.() }

  return (
    <li>
      <div
        className={className}
        role="button"
        tabIndex={0}
        onClick={() => onOpen(folder.id)}
        onKeyDown={(event) => {
          if (event.key === 'Enter' || event.key === ' ') { event.preventDefault(); onOpen(folder.id) }
        }}
        onDragOver={(event) => { event.preventDefault(); setDragOver(true) }}
        onDragLeave={() => setDragOver(false)}
        onDrop={handleDrop}
      >
        <span className={viewMode === 'grid' ? 'media-folder-card-name' : 'media-folder-name'}>
          📁 {folder.name}
        </span>
        <span
          className={viewMode === 'grid' ? 'media-folder-card-meta' : 'media-folder-count'}
          data-testid={`folder-counts-${folder.id}`}
        >
          {folderCountsLabel(folder)}
        </span>
        {hasChildren ? (
          <span
            className="media-folder-has-children"
            data-testid={`folder-has-children-${folder.id}`}
          >
            có thư mục con
          </span>
        ) : null}
        {canManage && (
          <span className="media-folder-tools">
            <button type="button" title="Tạo thư mục con" onClick={stopAnd(() => onCreateChild?.(folder.id))}>＋</button>
            <button type="button" title="Đổi tên" onClick={stopAnd(() => onRename?.(folder))}>✎</button>
            <button type="button" title="Xóa" onClick={stopAnd(() => onDelete?.(folder))}>🗑</button>
          </span>
        )}
      </div>
    </li>
  )
}

export default function MediaFolderExplorer({
  socialChannelId,
  channels = [],
  onSocialChannelChange,
  currentFolderId = null,
  selection = 'all',
  items = [],
  ancestors = [],
  totalPages = 1,
  pageIndex = 1,
  isLoading = false,
  isError = false,
  error = null,
  onRetry,
  openFolder,
  openRoot,
  openBreadcrumb,
  selectAll,
  selectUnassigned,
  goToPage,
  canManage = false,
  onMoveAsset,
  onCreateChild,
  onRename,
  onDelete,
}) {
  const [viewMode, setViewMode] = useState('grid')
  const [unassignedDragOver, setUnassignedDragOver] = useState(false)

  const handleDropUnassigned = (event) => {
    event.preventDefault()
    setUnassignedDragOver(false)
    const assetId = readAssetId(event)
    if (assetId) onMoveAsset?.(assetId, null)
  }

  return (
    <section className="media-folder-explorer" data-testid="media-folder-explorer">
      <div className="form-group media-folder-explorer-page">
        <label htmlFor="media-folder-page">Page</label>
        <select
          id="media-folder-page"
          aria-label="Page"
          value={socialChannelId || ''}
          onChange={(event) => onSocialChannelChange?.(event.target.value)}
        >
          <option value="">— Chọn Page —</option>
          {channels.map((channel) => (
            <option key={channel.id} value={channel.id}>
              {channel.pageName}
            </option>
          ))}
        </select>
      </div>

      <nav className="media-folder-tree" aria-label="Bộ lọc media">
        <button
          type="button"
          className={`media-folder-row is-fixed${selection === 'all' ? ' is-active' : ''}`}
          aria-current={selection === 'all' ? 'true' : undefined}
          onClick={selectAll}
        >
          <span className="media-folder-name">🗂️ Tất cả</span>
        </button>
        <div
          className={`media-folder-row is-fixed${selection === 'unassigned' ? ' is-active' : ''}${unassignedDragOver ? ' is-dragover' : ''}`}
          aria-current={selection === 'unassigned' ? 'true' : undefined}
          onClick={selectUnassigned}
          onDragOver={(event) => { event.preventDefault(); setUnassignedDragOver(true) }}
          onDragLeave={() => setUnassignedDragOver(false)}
          onDrop={handleDropUnassigned}
          role="button"
          tabIndex={0}
        >
          <span className="media-folder-name">📥 Chưa phân loại</span>
        </div>
      </nav>

      <nav className="media-folder-breadcrumb" aria-label="Đường dẫn thư mục">
        <button
          type="button"
          className={`media-folder-breadcrumb-item${!currentFolderId ? ' is-current' : ''}`}
          onClick={openRoot}
        >
          Thư mục gốc
        </button>
        {ancestors.map((item, index) => (
          <span key={item.id}>
            <span className="media-folder-breadcrumb-sep">/</span>
            <button
              type="button"
              className={`media-folder-breadcrumb-item${index === ancestors.length - 1 ? ' is-current' : ''}`}
              onClick={() => openBreadcrumb?.(item.id)}
            >
              {item.name}
            </button>
          </span>
        ))}
      </nav>

      <div className="media-folder-explorer-toolbar">
        <button
          type="button"
          className={`btn btn-ghost btn-sm${viewMode === 'grid' ? ' is-active' : ''}`}
          onClick={() => setViewMode('grid')}
        >
          Lưới
        </button>
        <button
          type="button"
          className={`btn btn-ghost btn-sm${viewMode === 'list' ? ' is-active' : ''}`}
          onClick={() => setViewMode('list')}
        >
          Danh sách
        </button>
      </div>

      {!socialChannelId ? (
        <EmptyState message="Chọn Page để duyệt thư mục" />
      ) : isLoading ? (
        <LoadingState message="Đang tải thư mục..." />
      ) : isError ? (
        <ErrorState message={getErrorMessage(error, 'Không tải được thư mục')} onRetry={onRetry} />
      ) : items.length === 0 ? (
        <EmptyState message="Chưa có thư mục con" />
      ) : (
        <ul className={viewMode === 'grid' ? 'media-folder-explorer-grid' : 'media-folder-explorer-list'}>
          {items.map((folder) => (
            <FolderCard
              key={folder.id}
              folder={folder}
              viewMode={viewMode}
              isActive={selection === folder.id || currentFolderId === folder.id}
              canManage={canManage}
              onOpen={openFolder}
              onMoveAsset={onMoveAsset}
              onCreateChild={onCreateChild}
              onRename={onRename}
              onDelete={onDelete}
            />
          ))}
        </ul>
      )}

      {socialChannelId && !isLoading && !isError && totalPages > 1 && (
        <div className="media-folder-explorer-pager">
          <button
            type="button"
            className="btn btn-ghost"
            disabled={pageIndex <= 1}
            onClick={() => goToPage?.(pageIndex - 1)}
          >
            Trước
          </button>
          <span>Trang {pageIndex}/{totalPages}</span>
          <button
            type="button"
            className="btn btn-ghost"
            disabled={pageIndex >= totalPages}
            onClick={() => goToPage?.(pageIndex + 1)}
          >
            Sau
          </button>
        </div>
      )}
    </section>
  )
}
