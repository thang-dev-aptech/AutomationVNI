import './MediaFolderCard.css'

export default function MediaFolderCard({
  folder,
  isRootLevel = false,
  onClick,
  onContextMenu,
  onDrop,
  canManage = false,
}) {
  const displayName = folder.name
  const subtitle = folder.pageName || (folder.socialChannelId ? `Page ${folder.socialChannelId}` : '')

  const handleDragOver = (event) => {
    event.preventDefault()
    event.dataTransfer.dropEffect = 'move'
    event.currentTarget.classList.add('media-folder-card-dragover')
  }

  const handleDragLeave = (event) => {
    if (event.currentTarget === event.target) {
      event.currentTarget.classList.remove('media-folder-card-dragover')
    }
  }

  const handleDrop = (event) => {
    event.preventDefault()
    event.currentTarget.classList.remove('media-folder-card-dragover')

    const assetId = event.dataTransfer.getData('text/media-asset-id')
    if (assetId && onDrop) {
      onDrop(assetId, folder.id)
    }
  }

  return (
    <article
      className="media-folder-card card"
      onClick={onClick}
      onContextMenu={(event) => {
        event.preventDefault()
        if (onContextMenu) {
          onContextMenu(event, folder)
        }
      }}
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
