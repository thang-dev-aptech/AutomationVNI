import LoadingState from '@/shared/components/LoadingState'
import ErrorState from '@/shared/components/ErrorState'
import StatusBadge from '@/shared/components/StatusBadge'
import { formatDateTime, getErrorMessage } from '@/shared/utils/apiHelpers'
import { getJobStatusMeta, getJobTypeLabel } from '../constants/postStatus'
import { usePostGenerationStatus } from '../hooks/usePosts'

export default function PostGenerationStatus({ postId, postStatus }) {
  const { data, isLoading, isError, error, refetch } = usePostGenerationStatus(postId, postStatus)

  if (isLoading) {
    return (
      <div className="card card-body" style={{ marginBottom: 16 }}>
        <LoadingState message="Đang tải trạng thái sinh nội dung..." />
      </div>
    )
  }

  if (isError) {
    return (
      <div className="card card-body" style={{ marginBottom: 16 }}>
        <h2 style={{ margin: '0 0 8px', fontSize: '1.05rem' }}>Trạng thái sinh nội dung</h2>
        <ErrorState
          message={getErrorMessage(error, 'Chưa có thông tin generation cho bài viết này.')}
          onRetry={refetch}
        />
      </div>
    )
  }

  if (!data) return null

  const steps = data.steps ?? []

  return (
    <div className="card card-body" style={{ marginBottom: 16 }}>
      <h2 style={{ margin: '0 0 12px', fontSize: '1.05rem' }}>Trạng thái sinh nội dung</h2>

      {(data.generationError || data.lastErrorMessage) && (
        <div className="alert alert-error" style={{ marginBottom: 12 }}>
          {data.lastErrorMessage || data.generationError}
        </div>
      )}

      {steps.length === 0 ? (
        <p style={{ margin: 0, color: 'var(--color-text-muted)' }}>
          Chưa có job generation nào.
        </p>
      ) : (
        <table>
          <thead>
            <tr>
              <th>Loại job</th>
              <th>Trạng thái</th>
              <th>Retry</th>
              <th>Bắt đầu</th>
              <th>Hoàn thành</th>
            </tr>
          </thead>
          <tbody>
            {steps.map((step) => {
              const statusMeta = getJobStatusMeta(step.jobStatus)
              return (
                <tr key={step.jobId}>
                  <td>{getJobTypeLabel(step.jobType)}</td>
                  <td>
                    <StatusBadge label={statusMeta.label} tone={statusMeta.tone} />
                  </td>
                  <td>{step.retryCount}/{step.maxRetries}</td>
                  <td>{formatDateTime(step.startedAt)}</td>
                  <td>{formatDateTime(step.completedAt)}</td>
                </tr>
              )
            })}
          </tbody>
        </table>
      )}
    </div>
  )
}
