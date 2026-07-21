import StatusBadge from '@/shared/components/StatusBadge'
import { formatDateTime, formatFileSize } from '@/shared/utils/apiHelpers'
import { getMediaSourceMeta, isImageMime } from '../constants/mediaConstants'
import './MediaAssetCard.css'

export default function MediaAssetCard({
  asset,
  onEdit,
  onDelete,
  onAnalyze,
  isAnalyzing = false,
  canManage = false,
}) {
  const sourceMeta = getMediaSourceMeta(asset.source)
  const displayName = asset.originalFileName || asset.fileName
  const showPreview = asset.publicUrl && isImageMime(asset.mimeType)
  const keywords = asset.keywords ?? []

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
        {keywords.length > 0 && (
          <div className="media-asset-card-keywords">
            {keywords.map((kw) => (
              <span key={kw} className="ai-media-keyword-chip">{kw}</span>
            ))}
          </div>
        )}
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
              {onAnalyze && isImageMime(asset.mimeType) && (
                <button
                  type="button"
                  className="btn btn-ghost btn-sm"
                  disabled={isAnalyzing}
                  title="AI phân tích ảnh và gắn 5-7 keyword"
                  onClick={() => onAnalyze(asset)}
                >
                  {isAnalyzing ? '⏳ AI...' : '✨ Gắn nhãn AI'}
                </button>
              )}
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
