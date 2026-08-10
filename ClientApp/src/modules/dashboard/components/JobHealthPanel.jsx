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

/**
 * Đổi mã lỗi kỹ thuật sang câu người dùng hiểu được.
 *
 * Trước đây bảng hiện thẳng "FB_API_ERROR" — người đọc biết có gì đó hỏng nhưng không biết
 * hỏng ở đâu và phải làm gì. Mã gốc vẫn giữ trong tooltip cho người kỹ thuật.
 *
 * Mã lạ thì hiện thông điệp gốc, KHÔNG hiện mã: một câu tiếng Anh vẫn đọc được hơn một mã
 * viết hoa gạch dưới.
 */
const ERROR_LABELS = {
  FB_API_ERROR: 'Facebook từ chối — kiểm tra token của page',
  FB_TOKEN_EXPIRED: 'Token Facebook hết hạn — kết nối lại page',
  FB_PERMISSION: 'Thiếu quyền đăng trên page này',
  RATE_LIMITED: 'Bị giới hạn tốc độ — thử lại sau',
  NETWORK_ERROR: 'Không kết nối được — kiểm tra mạng',
  AI_ERROR: 'AI không trả lời được',
  TIMEOUT: 'Quá thời gian chờ',
}

function explainError(code, message) {
  if (code && ERROR_LABELS[code]) return ERROR_LABELS[code]
  if (message) return truncateText(message, 60)
  return truncateText(code ?? 'Lỗi không rõ', 60)
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
                      <span className="dashboard-health-error" title={log.errorMessage || log.errorCode}>
                        {explainError(log.errorCode, log.errorMessage)}
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
