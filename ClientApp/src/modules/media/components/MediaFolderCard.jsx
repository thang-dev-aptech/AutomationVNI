import './MediaFolderCard.css'

export default function MediaFolderCard({
  folder,
  isRootLevel = false,
  onClick,
  onContextMenu,
  onDrop,
  onFileDrop,
  canManage = false,
}) {
  const displayName = folder.name
  const subtitle = folder.pageName || (folder.socialChannelId ? `Page ${folder.socialChannelId}` : '')

  const handleDragEnter = (event) => {
    event.stopPropagation()
  }

  const handleDragOver = (event) => {
    event.stopPropagation()
    event.preventDefault()
    event.dataTransfer.dropEffect = 'move'
    event.currentTarget.classList.add('media-folder-card-dragover')
  }

  const handleDragLeave = (event) => {
    event.stopPropagation()
    if (event.currentTarget === event.target) {
      event.currentTarget.classList.remove('media-folder-card-dragover')
    }
  }

  const handleDrop = (event) => {
    event.stopPropagation()
    event.preventDefault()
    event.currentTarget.classList.remove('media-folder-card-dragover')

    // Check for real files first (OS drag-drop)
    if (event.dataTransfer.files?.length > 0 && onFileDrop) {
      const formData = new FormData()
      Array.from(event.dataTransfer.files).forEach((file) => {
        formData.append('files', file)
      })
      formData.append('folderId', folder.id)
      onFileDrop(formData)
    } else {
      // Fall back to internal asset move
      const assetId = event.dataTransfer.getData('text/media-asset-id')
      if (assetId && onDrop) {
        onDrop(assetId, folder.id)
      }
    }
  }

  return (
    <article
      className="media-folder-card card"
      onClick={onClick}
      onContextMenu={(event) => {
        event.preventDefault()
        event.stopPropagation()
        if (onContextMenu) {
          onContextMenu(event, folder)
        }
      }}
      onDragEnter={canManage ? handleDragEnter : undefined}
      onDragOver={canManage ? handleDragOver : undefined}
      onDragLeave={canManage ? handleDragLeave : undefined}
      onDrop={canManage ? handleDrop : undefined}
    >
      <div className="media-folder-card-icon">📁</div>
      <div className="media-folder-card-content">
        <h3 className="media-folder-card-name">{displayName}</h3>
        {subtitle && <p className="media-folder-card-subtitle">{subtitle}</p>}
      </div>
      {folder.childCount !== undefined && (
        <div className="media-folder-card-count">{folder.childCount}</div>
      )}
    </article>
  )
}
