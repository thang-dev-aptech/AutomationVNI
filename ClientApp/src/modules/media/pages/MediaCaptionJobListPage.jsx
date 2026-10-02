import { Link } from 'react-router-dom'
import PageHeader from '@/shared/components/PageHeader'
import LoadingState from '@/shared/components/LoadingState'
import ErrorState from '@/shared/components/ErrorState'
import EmptyState from '@/shared/components/EmptyState'
import StatusBadge from '@/shared/components/StatusBadge'
import { formatDateTime, getErrorMessage } from '@/shared/utils/apiHelpers'
import { useMediaCaptionJobList } from '../hooks/useMediaCaptionJobs'
import { JOB_STATUS_LABEL } from './mediaCaptionJobMeta'

const JOB_TONE = { Queued: 'neutral', Running: 'info', Completed: 'success' }

export default function MediaCaptionJobListPage() {
  const { data, isLoading, isError, error, refetch } = useMediaCaptionJobList()
  const jobs = Array.isArray(data) ? data : []

  return (
    <section className="media-caption-job">
      <PageHeader
        title="Job sinh caption"
        description="Các job sinh caption hàng loạt gần đây"
        actions={<Link to="/media" className="btn btn-secondary">← Về Media</Link>}
      />
      {isLoading && <LoadingState message="Đang tải..." />}
      {isError && <ErrorState message={getErrorMessage(error)} onRetry={refetch} />}
      {!isLoading && !isError && jobs.length === 0 && <EmptyState message="Chưa có job nào." />}
      {jobs.length > 0 && (
        <div className="card card-body">
          <table className="table">
            <thead>
              <tr><th>Thư mục</th><th>Trạng thái</th><th>Xong / Lỗi / Bỏ qua / Tổng</th><th>Tạo lúc</th></tr>
            </thead>
            <tbody>
              {jobs.map((job) => (
                <tr key={job.id}>
                  <td><Link to={`/media/caption-jobs/${job.id}`}>{job.folderName}</Link></td>
                  <td>
                    <StatusBadge label={JOB_STATUS_LABEL[job.status] ?? job.status} tone={JOB_TONE[job.status] ?? 'neutral'} />
                  </td>
                  <td>{job.succeeded} / {job.failed} / {job.skipped} / {job.total}</td>
                  <td>{formatDateTime(job.createdAt)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </section>
  )
}
