import { formatDateTime, formatFileSize } from '@/shared/utils/apiHelpers'

export default function MusicTrackCard({ track, canManage, onDelete }) {
  return (
    <div className="card card-body" style={{ display: 'flex', flexDirection: 'column', gap: 10 }}>
      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-start', gap: 8 }}>
        <div>
          <strong>{track.displayName}</strong>
          <p style={{ margin: '4px 0 0', fontSize: '0.82rem', color: 'var(--text-muted, #888)' }}>
            {formatFileSize(track.fileSize)} · {formatDateTime(track.createdAt)}
          </p>
        </div>
        {canManage && (
          <button
            type="button"
            className="btn btn-danger btn-sm"
            onClick={() => onDelete(track)}
          >
            Xóa
          </button>
        )}
      </div>
      <audio controls src={track.previewUrl} style={{ width: '100%' }}>
        Trình duyệt không hỗ trợ phát nhạc.
      </audio>
    </div>
  )
}
