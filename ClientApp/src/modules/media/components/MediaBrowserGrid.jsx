import { useState } from 'react'
import LoadingState from '@/shared/components/LoadingState'
import ErrorState from '@/shared/components/ErrorState'
import EmptyState from '@/shared/components/EmptyState'
import { getErrorMessage } from '@/shared/utils/apiHelpers'
import MediaFolderCard from './MediaFolderCard'
import MediaAssetCard from './MediaAssetCard'
import './MediaBrowserGrid.css'

export default function MediaBrowserGrid({
  folders = [],
  files = [],
  isLoading = false,
  isError = false,
  error = null,
  onRetry,
  onFolderClick,
  onFolderContextMenu,
  onFolderDrop,
  onFileView,
  onFileDetails,
  onFileDelete,
  onFileContextMenu,
  onFileDrop,
  onGridContextMenu,
  onGridFileDrop,
  canManage = false,
  isRootLevel = false,
  folderBreadcrumb = null,
  pageIndex = 1,
  totalPages = 1,
  onPageChange,
  filePageIndex = 1,
  totalFilePages = 1,
  onFilePageChange,
  isUploading = false,
}) {
  const [isDragOverGrid, setIsDragOverGrid] = useState(false)
  const hasFolders = folders.length > 0
  const hasFiles = files.length > 0

  const handleWhitespaceContextMenu = (event) => {
    if (!canManage || !onGridContextMenu) return
    if (event.target.closest('.media-folder-card, .media-asset-card')) return
    event.preventDefault()
    onGridContextMenu(event)
  }

  const handleGridDragEnter = (event) => {
    if (event.target !== event.currentTarget) return
    if (!canManage || !event.dataTransfer.types.includes('Files')) return
    event.preventDefault()
    event.dataTransfer.dropEffect = 'copy'
    setIsDragOverGrid(true)
  }

  const handleGridDragOver = (event) => {
    if (!canManage || !event.dataTransfer.types.includes('Files')) return
    event.preventDefault()
    event.dataTransfer.dropEffect = 'copy'
  }

  const handleGridDragLeave = (event) => {
    if (event.target === event.currentTarget) {
      setIsDragOverGrid(false)
    }
  }

  const handleGridDrop = (event) => {
    if (!canManage || !event.dataTransfer.files?.length || !onGridFileDrop) return
    // Only handle drops on the grid wrapper itself, not on child elements
    if (event.target !== event.currentTarget) return
    event.preventDefault()
    setIsDragOverGrid(false)

    const formData = new FormData()
    Array.from(event.dataTransfer.files).forEach((file) => {
      formData.append('files', file)
    })
    onGridFileDrop(formData)
  }

  const renderPager = (index, pages, onChange) => (pages > 1 ? (
    <div className="media-page-pager">
      <button
        type="button"
        className="btn btn-secondary"
        disabled={index <= 1}
        onClick={() => onChange?.(index - 1)}
      >
        Trước
      </button>
      <span>
        Trang {index} / {pages}
      </span>
      <button
        type="button"
        className="btn btn-secondary"
        disabled={index >= pages}
        onClick={() => onChange?.(index + 1)}
      >
        Sau
      </button>
    </div>
  ) : null)

  const folderPager = renderPager(pageIndex, totalPages, onPageChange)
  const filePager = renderPager(filePageIndex, totalFilePages, onFilePageChange)

  const folderHeader = (
    <div className="media-browser-section-header media-page-pager-host">
      <h3 className="media-browser-section-title">Thư mục</h3>
      {folderBreadcrumb}
      {folderPager}
    </div>
  )

  const wrapGrid = (children) => (
    <div
      className={`media-browser-grid${isDragOverGrid ? ' media-browser-grid-dragover' : ''}`}
      onContextMenu={handleWhitespaceContextMenu}
      onDragEnter={handleGridDragEnter}
      onDragOver={handleGridDragOver}
      onDragLeave={handleGridDragLeave}
      onDrop={handleGridDrop}
    >
      {(isUploading || isDragOverGrid) && (
        <div className="media-browser-dropzone-banner">
          {isUploading && <span className="state-spinner" />}
          <span>{isUploading ? 'Đang tải ảnh lên...' : 'Thả ảnh để upload'}</span>
        </div>
      )}
      {children}
    </div>
  )

  if (isLoading) {
    return wrapGrid(
      <>
        {folderHeader}
        <LoadingState />
      </>,
    )
  }

  if (isError) {
    return wrapGrid(
      <>
        {folderHeader}
        <ErrorState message={getErrorMessage(error)} onRetry={onRetry} />
      </>,
    )
  }

  if (!hasFolders && !hasFiles) {
    return wrapGrid(
      <>
        {folderHeader}
        <EmptyState message="Chưa có nội dung nào" />
      </>,
    )
  }

  return wrapGrid(
    <>
      <div className="media-browser-section">
        {folderHeader}
        {hasFolders && (
          <div className="media-folder-cards-grid">
            {folders.map((folder) => (
              <MediaFolderCard
                key={folder.id}
                folder={folder}
                isRootLevel={isRootLevel}
                onClick={() => onFolderClick?.(folder)}
                onContextMenu={onFolderContextMenu}
                onDrop={onFolderDrop}
                onFileDrop={onFileDrop}
                canManage={canManage}
              />
            ))}
          </div>
        )}
      </div>

      {hasFiles && (
        <div className="media-browser-section">
          <div className="media-browser-section-header media-page-pager-host">
            <h3 className="media-browser-section-title">Tệp</h3>
            {filePager}
          </div>
          <div className="media-grid">
            {files.map((asset) => (
              <MediaAssetCard
                key={asset.id}
                asset={asset}
                onView={onFileView}
                onDetails={onFileDetails}
                onDelete={onFileDelete}
                onContextMenu={onFileContextMenu}
                canManage={canManage}
              />
            ))}
          </div>
        </div>
      )}
    </>,
  )
}
