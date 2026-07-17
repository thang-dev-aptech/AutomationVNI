import { Link } from 'react-router-dom'
import EmptyState from '@/shared/components/EmptyState'
import StatusBadge from '@/shared/components/StatusBadge'
import { formatDateTime } from '@/shared/utils/apiHelpers'
import { getSocialPlatformLabel } from '@/modules/social-channels/constants/socialPlatform'
import DashboardSection from './DashboardSection'
import './DashboardComponents.css'

function MetricRow({ label, value, tone = 'neutral' }) {
  return (
    <div className="dashboard-metric-row">
      <span className="dashboard-metric-label">{label}</span>
      <StatusBadge
        label={value === null || value === undefined ? '—' : String(value)}
        tone={tone}
      />
    </div>
  )
}

export default function ChannelHealthPanel({ channels, canViewPlatforms = true }) {
  if (!canViewPlatforms) return null

  const expired = channels?.expired ?? []

  return (
    <DashboardSection
      title="Channel health"
      description="Kênh hoạt động, tắt và token hết hạn"
      action={(
        <Link to="/platforms" className="btn btn-ghost btn-sm">
          Mở Platforms
        </Link>
      )}
    >
      {channels?.available === false ? (
        <EmptyState message="Chưa có dữ liệu kênh" />
      ) : (
        <div className="dashboard-health-grid">
          <div className="dashboard-health-block">
            <MetricRow label="Đang hoạt động" value={channels?.active} tone="success" />
            <MetricRow label="Không hoạt động" value={channels?.inactive} tone="neutral" />
            <MetricRow label="Token hết hạn" value={channels?.expiredCount} tone="warning" />
          </div>

          <div className="dashboard-health-block">
            <h3 className="dashboard-health-subtitle">Kênh token hết hạn</h3>
            {expired.length === 0 ? (
              <p className="dashboard-health-empty">Không có kênh token hết hạn</p>
            ) : (
              <ul className="dashboard-health-list">
                {expired.slice(0, 5).map((channel) => (
                  <li key={channel.id}>
                    <span className="dashboard-health-link">{channel.pageName}</span>
                    <span className="dashboard-health-meta">
                      {getSocialPlatformLabel(channel.platform)}
                      {' · '}
                      Hết hạn {formatDateTime(channel.tokenExpiresAt)}
                    </span>
                  </li>
                ))}
              </ul>
            )}
          </div>
        </div>
      )}
    </DashboardSection>
  )
}
