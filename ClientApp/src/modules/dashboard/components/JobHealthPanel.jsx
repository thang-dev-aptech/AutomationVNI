import { Link } from 'react-router-dom'
import EmptyState from '@/shared/components/EmptyState'
import StatusBadge from '@/shared/components/StatusBadge'
import { formatDateTime } from '@/shared/utils/apiHelpers'
import { shortId, truncateText } from '@/modules/jobs/constants/jobConstants'
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

export default function JobHealthPanel({
  jobs,
  publishLogs,
  canViewJobs = true,
}) {
  const hasJobData = jobs?.available !== false
  const hasPublishData = publishLogs?.available !== false
  const recentFailed = publishLogs?.recentFailed ?? []

  if (!canViewJobs) return null

  return (
    <DashboardSection
      title="Job health"
      description="Trạng thái generation jobs và publish logs thất bại"
      action={(
        <Link to="/jobs" className="btn btn-ghost btn-sm">
          Mở Jobs
        </Link>
      )}
    >
      {!hasJobData && !hasPublishData ? (
        <EmptyState message="Chưa có dữ liệu jobs" />
      ) : (
        <div className="dashboard-health-grid">
          <div className="dashboard-health-block">
            <h3 className="dashboard-health-subtitle">Generation jobs</h3>
            <MetricRow label="Pending" value={jobs?.pending} tone="neutral" />
            <MetricRow label="Running" value={jobs?.running} tone="info" />
            <MetricRow
              label="Failed / Dead letter"
              value={jobs?.failedTotal}
              tone="danger"
            />
          </div>

          <div className="dashboard-health-block">
            <h3 className="dashboard-health-subtitle">Publish logs thất bại</h3>
            <MetricRow
              label="Tổng failed"
              value={publishLogs?.failed}
              tone="danger"
            />
            {recentFailed.length === 0 ? (
              <p className="dashboard-health-empty">Không có publish log thất bại gần đây</p>
            ) : (
              <ul className="dashboard-health-list">
                {recentFailed.map((log) => (
                  <li key={log.id}>
                    <Link to={`/posts/${log.postId}`} className="dashboard-health-link">
                      Post {shortId(log.postId)}
                    </Link>
                    <span className="dashboard-health-meta">
                      {formatDateTime(log.createdAt)}
                    </span>
                    {(log.errorCode || log.errorMessage) && (
                      <span className="dashboard-health-error" title={log.errorMessage || ''}>
                        {truncateText(log.errorCode || log.errorMessage, 48)}
                      </span>
                    )}
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
