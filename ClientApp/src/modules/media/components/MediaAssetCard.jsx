import { hasTemplateLayout, isImageMime } from '../constants/mediaConstants'
import './MediaAssetCard.css'

export default function MediaAssetCard({
  asset,
  onView,
  onDetails,
  onDelete,
  onContextMenu,
  canManage = false,
  // Chế độ chọn (popup chọn ảnh): bấm = chọn/bỏ chọn thay cho xem; ẩn Chi tiết/Xóa, không kéo-thả.
  selectable = false,
  selected = false,
  onSelect,
}) {
  const displayName = asset.originalFileName || asset.fileName
  const isImage = isImageMime(asset.mimeType)
  const showPreview = asset.publicUrl && isImage
  const canDrag = canManage && !selectable
  const isTemplateReady = hasTemplateLayout(asset.tags)

  const handleDragStart = (event) => {
    event.dataTransfer.setData('text/media-asset-id', asset.id)
    event.dataTransfer.effectAllowed = 'move'

    // Bóng ma khi kéo: dùng thumbnail nhỏ (72px) thay vì cả card full-size cho dễ canh thả.
    if (showPreview) {
      const ghost = document.createElement('img')
      ghost.src = asset.publicUrl
      ghost.className = 'media-drag-ghost'
      document.body.appendChild(ghost)
      event.dataTransfer.setDragImage(ghost, 36, 36)
      // Gỡ khỏi DOM sau khi trình duyệt đã chụp ảnh kéo (ở tick kế tiếp).
      setTimeout(() => ghost.remove(), 0)
    }
  }

  return (
    <article
      className={[
        'media-asset-card card',
        selectable && 'is-selectable',
        selectable && selected && 'is-selected',
        selectable && !isImage && 'is-disabled',
      ].filter(Boolean).join(' ')}
      draggable={canDrag}
      onDragStart={canDrag ? handleDragStart : undefined}
      onContextMenu={(event) => {
        event.preventDefault()
        event.stopPropagation()
        if (onContextMenu) {
          onContextMenu(event, asset)
        }
      }}
    >
      <button
        type="button"
        className="media-asset-card-preview"
        onClick={selectable ? () => isImage && onSelect?.(asset) : () => onView(asset)}
        disabled={selectable && !isImage}
        aria-pressed={selectable && isImage ? selected : undefined}
        aria-label={selectable ? displayName : undefined}
        title={selectable
          ? (isImage ? 'Ấn để chọn ảnh' : 'Không phải ảnh nên không chọn được')
          : (canManage ? 'Ấn để xem ảnh · kéo để chuyển thư mục' : 'Ấn để xem ảnh')}
      >
        {showPreview ? (
          <img src={asset.publicUrl} alt={asset.altText || displayName} loading="lazy" />
        ) : (
          <div className="media-asset-card-placeholder">
            {asset.mimeType || 'No preview'}
          </div>
        )}
        {isTemplateReady && (
          <span className="media-asset-card-layout-badge" title="Đã quét Vùng An Toàn — dùng được cho bài Template">
            📐 Đã quét Layout
          </span>
        )}
        {selectable && selected && <span className="media-asset-card-check" aria-hidden="true">✓</span>}
      </button>
      {selectable ? (
        <div className="media-asset-card-actions">
          <span className="media-asset-card-pick-name">{displayName}</span>
          {!isImage && <span className="media-asset-card-pick-note">Không phải ảnh</span>}
        </div>
      ) : (
        <div className="media-asset-card-actions">
          <button type="button" className="btn btn-ghost btn-sm" onClick={() => onDetails(asset)}>
            Chi tiết
          </button>
          {canManage && (
            <button type="button" className="btn btn-danger btn-sm" onClick={() => onDelete(asset)}>
              Xóa
            </button>
          )}
        </div>
      )}
    </article>
  )
}
