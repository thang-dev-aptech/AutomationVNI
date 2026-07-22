import { isImageMime } from '../constants/mediaConstants'
import './MediaAssetCard.css'

export default function MediaAssetCard({
  asset,
  onView,
  onDetails,
  onDelete,
  canManage = false,
}) {
  const displayName = asset.originalFileName || asset.fileName
  const showPreview = asset.publicUrl && isImageMime(asset.mimeType)

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
      className="media-asset-card card"
      draggable={canManage}
      onDragStart={canManage ? handleDragStart : undefined}
    >
      <button
        type="button"
        className="media-asset-card-preview"
        onClick={() => onView(asset)}
        title={canManage ? 'Ấn để xem ảnh · kéo để chuyển thư mục' : 'Ấn để xem ảnh'}
      >
        {showPreview ? (
          <img src={asset.publicUrl} alt={asset.altText || displayName} loading="lazy" />
        ) : (
          <div className="media-asset-card-placeholder">
            {asset.mimeType || 'No preview'}
          </div>
        )}
      </button>
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
    </article>
  )
}
