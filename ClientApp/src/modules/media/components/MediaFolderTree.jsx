import { useMemo, useState } from 'react'

/** Đọc assetId từ sự kiện kéo-thả (card set qua dataTransfer). */
function readAssetId(event) {
  return event.dataTransfer.getData('text/media-asset-id') || null
}

function FolderNode({
  folder,
  childrenMap,
  depth,
  selection,
  onSelect,
  canManage,
  onMoveAsset,
  onCreateChild,
  onRename,
  onDelete,
}) {
  const [expanded, setExpanded] = useState(true)
  const [dragOver, setDragOver] = useState(false)
  const children = childrenMap.get(folder.id) ?? []
  const isSelected = selection === folder.id

  const handleDrop = (event) => {
    event.preventDefault()
    event.stopPropagation()
    setDragOver(false)
    const assetId = readAssetId(event)
    if (assetId) onMoveAsset(assetId, folder.id)
  }

  return (
    <li>
      <div
        className={`media-folder-row${isSelected ? ' is-active' : ''}${dragOver ? ' is-dragover' : ''}`}
        style={{ paddingLeft: 8 + depth * 16 }}
        onClick={() => onSelect(folder.id)}
        onDragOver={(event) => { event.preventDefault(); setDragOver(true) }}
        onDragLeave={() => setDragOver(false)}
        onDrop={handleDrop}
      >
        <button
          type="button"
          className="media-folder-toggle"
          onClick={(event) => { event.stopPropagation(); setExpanded((v) => !v) }}
          aria-label={expanded ? 'Thu gọn' : 'Mở rộng'}
        >
          {folder.hasChildren ? (expanded ? '▾' : '▸') : '·'}
        </button>
        <span className="media-folder-name" title={folder.name}>📁 {folder.name}</span>
        <span className="media-folder-count">{folder.assetCount}</span>
        {canManage && (
          <span className="media-folder-tools">
            <button type="button" title="Tạo thư mục con" onClick={(e) => { e.stopPropagation(); onCreateChild(folder.id) }}>＋</button>
            <button type="button" title="Đổi tên" onClick={(e) => { e.stopPropagation(); onRename(folder) }}>✎</button>
            <button type="button" title="Xóa" onClick={(e) => { e.stopPropagation(); onDelete(folder) }}>🗑</button>
          </span>
        )}
      </div>
      {expanded && children.length > 0 && (
        <ul className="media-folder-children">
          {children.map((child) => (
            <FolderNode
              key={child.id}
              folder={child}
              childrenMap={childrenMap}
              depth={depth + 1}
              selection={selection}
              onSelect={onSelect}
              canManage={canManage}
              onMoveAsset={onMoveAsset}
              onCreateChild={onCreateChild}
              onRename={onRename}
              onDelete={onDelete}
            />
          ))}
        </ul>
      )}
    </li>
  )
}

export default function MediaFolderTree({
  folders = [],
  selection = 'all',
  onSelect,
  canManage = false,
  onMoveAsset,
  onCreateChild,
  onRename,
  onDelete,
}) {
  const [unassignedDragOver, setUnassignedDragOver] = useState(false)

  const { roots, childrenMap } = useMemo(() => {
    const map = new Map()
    folders.forEach((f) => {
      const key = f.parentFolderId ?? null
      if (!map.has(key)) map.set(key, [])
      map.get(key).push(f)
    })
    return { roots: map.get(null) ?? [], childrenMap: map }
  }, [folders])

  const handleDropUnassigned = (event) => {
    event.preventDefault()
    setUnassignedDragOver(false)
    const assetId = readAssetId(event)
    if (assetId) onMoveAsset(assetId, null)
  }

  return (
    <nav className="media-folder-tree">
      <button
        type="button"
        className={`media-folder-row is-fixed${selection === 'all' ? ' is-active' : ''}`}
        onClick={() => onSelect('all')}
      >
        <span className="media-folder-name">🗂️ Tất cả</span>
      </button>
      <div
        className={`media-folder-row is-fixed${selection === 'unassigned' ? ' is-active' : ''}${unassignedDragOver ? ' is-dragover' : ''}`}
        onClick={() => onSelect('unassigned')}
        onDragOver={(event) => { event.preventDefault(); setUnassignedDragOver(true) }}
        onDragLeave={() => setUnassignedDragOver(false)}
        onDrop={handleDropUnassigned}
        role="button"
        tabIndex={0}
      >
        <span className="media-folder-name">📥 Chưa phân loại</span>
      </div>

      <ul className="media-folder-roots">
        {roots.map((folder) => (
          <FolderNode
            key={folder.id}
            folder={folder}
            childrenMap={childrenMap}
            depth={0}
            selection={selection}
            onSelect={onSelect}
            canManage={canManage}
            onMoveAsset={onMoveAsset}
            onCreateChild={onCreateChild}
            onRename={onRename}
            onDelete={onDelete}
          />
        ))}
      </ul>
    </nav>
  )
}
