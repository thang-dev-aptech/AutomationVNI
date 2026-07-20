import StatusBadge from '@/shared/components/StatusBadge'
import { formatDateTime } from '@/shared/utils/apiHelpers'
import {
  getChannelTypeLabel,
  getProviderLabel,
} from '../constants/socialPlatform'

function groupChannels(channels = []) {
  const pages = []
  const instagram = []
  const groups = []
  const threads = []
  for (const ch of channels) {
    if (ch.channelType === 2) instagram.push(ch)
    else if (ch.channelType === 3) groups.push(ch)
    else if (ch.channelType === 4) threads.push(ch)
    // Nhánh mặc định là Page — kênh loại mới phải khai tường minh ở trên,
    // nếu không sẽ bị gán nhầm nhãn "Facebook Pages".
    else pages.push(ch)
  }
  return { pages, instagram, groups, threads }
}

function ChannelSection({ title, items, canManage, onEdit, onDelete }) {
  if (items.length === 0) return null
  return (
    <div className="connection-section">
      <h4 className="connection-section-title">
        {title} <span className="connection-section-count">({items.length})</span>
      </h4>
      <ul className="connection-channel-list">
        {items.map((item) => (
          <li key={item.id} className="connection-channel-row">
            <div className="connection-channel-main">
              <span className="connection-channel-name">{item.pageName}</span>
              <span className="connection-channel-meta">
                {getChannelTypeLabel(item.channelType)} · {item.externalPageId}
              </span>
            </div>
            <StatusBadge
              label={item.isActive ? 'Hoạt động' : 'Tắt'}
              tone={item.isActive ? 'success' : 'neutral'}
            />
            {canManage && (
              <div className="table-actions">
                <button type="button" className="btn btn-ghost btn-sm" onClick={() => onEdit(item)}>
                  Sửa
                </button>
                <button type="button" className="btn btn-danger btn-sm" onClick={() => onDelete(item)}>
                  Xóa
                </button>
              </div>
            )}
          </li>
        ))}
      </ul>
    </div>
  )
}

export default function ConnectionCard({
  connection,
  expanded,
  onToggle,
  canManage,
  onResync,
  onDisconnect,
  onEditChannel,
  onDeleteChannel,
  resyncPending = false,
}) {
  const { pages, instagram, groups, threads } = groupChannels(connection.channels)
  const providerLabel = getProviderLabel(connection.provider)

  return (
    <article className={`connection-card ${expanded ? 'is-expanded' : ''}`}>
      <header className="connection-card-header">
        <button type="button" className="connection-card-toggle" onClick={onToggle}>
          {connection.avatarUrl ? (
            <img
              src={connection.avatarUrl}
              alt=""
              className="connection-avatar"
              referrerPolicy="no-referrer"
            />
          ) : (
            <span className="connection-avatar connection-avatar-fallback">
              {(connection.displayName || '?').slice(0, 1).toUpperCase()}
            </span>
          )}
          <div className="connection-card-titles">
            <strong>{connection.displayName || 'Tài khoản đã kết nối'}</strong>
            <span>
              {providerLabel}
              {' · '}
              {/* Threads grants one profile per authorization — Page/IG/Group counts don't apply. */}
              {connection.provider === 3
                ? `${connection.threadsCount ?? 0} Profile`
                : `${connection.pageCount} Page · ${connection.instagramCount} IG · ${connection.groupCount} Group`}
              {connection.lastSyncedAt
                ? ` · sync ${formatDateTime(connection.lastSyncedAt)}`
                : ''}
            </span>
          </div>
          <span className="connection-chevron" aria-hidden>
            {expanded ? '▾' : '▸'}
          </span>
        </button>

        {canManage && (
          <div className="connection-card-actions">
            <button
              type="button"
              className="btn btn-secondary btn-sm"
              onClick={onResync}
              disabled={resyncPending}
            >
              {resyncPending ? 'Đang mở…' : 'Re-sync'}
            </button>
            <button type="button" className="btn btn-ghost btn-sm" onClick={onDisconnect}>
              Ngắt kết nối
            </button>
          </div>
        )}
      </header>

      {expanded && (
        <div className="connection-card-body">
          <ChannelSection
            title="Facebook Pages"
            items={pages}
            canManage={canManage}
            onEdit={onEditChannel}
            onDelete={onDeleteChannel}
          />
          <ChannelSection
            title="Instagram"
            items={instagram}
            canManage={canManage}
            onEdit={onEditChannel}
            onDelete={onDeleteChannel}
          />
          <ChannelSection
            title="Groups"
            items={groups}
            canManage={canManage}
            onEdit={onEditChannel}
            onDelete={onDeleteChannel}
          />
          <ChannelSection
            title="Threads"
            items={threads}
            canManage={canManage}
            onEdit={onEditChannel}
            onDelete={onDeleteChannel}
          />
          {pages.length === 0 && instagram.length === 0 && groups.length === 0
            && threads.length === 0 && (
            <p className="connection-empty">Chưa có kênh nào trong tài khoản này. Bấm Re-sync.</p>
          )}
        </div>
      )}
    </article>
  )
}
