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
  canManage = false,
  isRootLevel = false,
}) {
  if (isLoading) return <LoadingState />
  if (isError) return <ErrorState message={getErrorMessage(error)} onRetry={onRetry} />

  const hasFolders = folders.length > 0
  const hasFiles = files.length > 0

  if (!hasFolders && !hasFiles) {
    return <EmptyState message="Chưa có nội dung nào" />
  }

  return (
    <div className="media-browser-grid">
      {hasFolders && (
        <div className="media-browser-section">
          <h3 className="media-browser-section-title">Thư mục</h3>
          <div className="media-folder-cards-grid">
            {folders.map((folder) => (
              <MediaFolderCard
                key={folder.id}
                folder={folder}
                isRootLevel={isRootLevel}
                onClick={() => onFolderClick?.(folder)}
                onContextMenu={onFolderContextMenu}
                onDrop={onFolderDrop}
                canManage={canManage}
              />
            ))}
          </div>
        </div>
      )}

      {hasFiles && (
        <div className="media-browser-section">
          <h3 className="media-browser-section-title">Tệp</h3>
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
    </div>
  )
}
