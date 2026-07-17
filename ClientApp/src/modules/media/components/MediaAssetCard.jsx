import StatusBadge from '@/shared/components/StatusBadge'
import { formatDateTime, formatFileSize } from '@/shared/utils/apiHelpers'
import { getMediaSourceMeta, isImageMime } from '../constants/mediaConstants'
import './MediaAssetCard.css'

export default function MediaAssetCard({ asset, onEdit, onDelete, canManage = false }) {
  const sourceMeta = getMediaSourceMeta(asset.source)
  const displayName = asset.originalFileName || asset.fileName
  const showPreview = asset.publicUrl && isImageMime(asset.mimeType)

  return (
    <article className="media-asset-card card">
      <div className="media-asset-card-preview">
        {showPreview ? (
          <img src={asset.publicUrl} alt={asset.altText || displayName} loading="lazy" />
        ) : (
          <div className="media-asset-card-placeholder">
            {asset.mimeType || 'No preview'}
          </div>
        )}
      </div>
      <div className="media-asset-card-body">
        <h3 className="media-asset-card-title" title={displayName}>
          {displayName}
        </h3>
        <div className="media-asset-card-meta">
          <StatusBadge label={sourceMeta.label} tone={sourceMeta.tone} />
          <span>{formatFileSize(asset.fileSize)}</span>
        </div>
        <div className="media-asset-card-date">
          {formatDateTime(asset.createdAt)}
        </div>
        <div className="media-asset-card-actions">
          {asset.publicUrl && (
            <a
              href={asset.publicUrl}
              target="_blank"
              rel="noreferrer"
              className="btn btn-ghost btn-sm"
            >
              Xem
            </a>
          )}
          {canManage && (
            <>
              <button type="button" className="btn btn-ghost btn-sm" onClick={() => onEdit(asset)}>
                Sửa
              </button>
              <button type="button" className="btn btn-danger btn-sm" onClick={() => onDelete(asset)}>
                Xóa
              </button>
            </>
          )}
        </div>
      </div>
    </article>
  )
}
