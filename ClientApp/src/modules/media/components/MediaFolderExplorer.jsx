import { useState } from 'react'
import MediaFolderTreeNav from './MediaFolderTreeNav'
import './MediaFolderExplorer.css'

function readAssetId(event) {
  return event.dataTransfer.getData('text/media-asset-id') || null
}

export default function MediaFolderExplorer({
  socialChannelId,
  channels = [],
  onSocialChannelChange,
  currentFolderId = null,
  selection = 'all',
  selectAll,
  selectUnassigned,
  canManage = false,
  openFolder,
  onMoveAsset,
  onCreateChild,
  onRename,
  onDelete,
  expandedFolderIds = new Set(),
  toggleFolderExpanded,
}) {
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

      <MediaFolderTreeNav
        socialChannelId={socialChannelId}
        currentFolderId={currentFolderId}
        expandedFolderIds={expandedFolderIds}
        onToggleExpand={toggleFolderExpanded}
        onOpen={openFolder}
        onMoveAsset={onMoveAsset}
        onCreateChild={onCreateChild}
        onRename={onRename}
        onDelete={onDelete}
        canManage={canManage}
      />
    </section>
  )
}
