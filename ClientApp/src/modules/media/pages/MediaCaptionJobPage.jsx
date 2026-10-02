import { Link, useParams } from 'react-router-dom'
import PageHeader from '@/shared/components/PageHeader'
import LoadingState from '@/shared/components/LoadingState'
import ErrorState from '@/shared/components/ErrorState'
import StatusBadge from '@/shared/components/StatusBadge'
import { getErrorMessage } from '@/shared/utils/apiHelpers'
import { toast } from '@/shared/stores/toastStore'
import {
  useMediaCaptionJob,
  useRetryCaptionItem,
  useRetryFailedCaptionItems,
} from '../hooks/useMediaCaptionJobs'
import { ITEM_STATUS_META, JOB_STATUS_LABEL } from './mediaCaptionJobMeta'
import './MediaPage.css'

export default function MediaCaptionJobPage() {
  const { jobId } = useParams()
  const { data: job, isLoading, isError, error, refetch } = useMediaCaptionJob(jobId)
  const retryItem = useRetryCaptionItem()
  const retryFailed = useRetryFailedCaptionItems()

  const run = async (mutation, arg, successMessage) => {
    try {
      await mutation.mutateAsync(arg)
      toast.success(successMessage)
    } catch (err) {
      toast.error(getErrorMessage(err))
    }
  }

  if (isLoading) return <LoadingState message="Đang tải job..." />
  if (isError) return <ErrorState message={getErrorMessage(error)} onRetry={refetch} />

  const items = job.items ?? []
  const finishedItems = items.filter((x) => ['Succeeded', 'Failed', 'Skipped'].includes(x.status)).length
  const percent = items.length > 0 ? Math.round((finishedItems / items.length) * 100) : 100

  return (
    <section className="media-caption-job">
      <PageHeader
        title={`Sinh caption: ${job.folderName}`}
        description={`Trạng thái: ${JOB_STATUS_LABEL[job.status] ?? job.status}`}
        actions={(
          <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap' }}>
            <Link to={`/media?folder=${job.folderId}`} className="btn btn-secondary">← Về thư mục</Link>
            <Link to="/media/caption-jobs" className="btn btn-ghost">Danh sách job</Link>
          </div>
        )}
      />

      <div className="card card-body">
        <div
          className="media-caption-job-progress"
          role="progressbar"
          aria-valuenow={percent}
          aria-valuemin={0}
          aria-valuemax={100}
        >
          <div className="media-caption-job-progress-bar" style={{ width: `${percent}%` }} />
        </div>
        <p className="media-caption-job-counts">
          Xong <strong>{job.succeeded}</strong> · Lỗi <strong>{job.failed}</strong> · Bỏ qua{' '}
          <strong>{job.skipped}</strong> · Tổng <strong>{job.total}</strong>
        </p>
        <button
          type="button"
          className="btn btn-secondary"
          disabled={job.failed === 0 || retryFailed.isPending}
          onClick={() => run(retryFailed, job.id, 'Đã đưa các ảnh lỗi vào hàng chờ')}
        >
          Retry tất cả ảnh lỗi
        </button>
      </div>

      <div className="card card-body">
        <table className="table media-caption-job-table">
          <thead>
            <tr><th>Ảnh</th><th>Tên file</th><th>Trạng thái</th><th>Lỗi</th><th /></tr>
          </thead>
          <tbody>
            {items.map((item) => {
              const meta = ITEM_STATUS_META[item.status] ?? { label: item.status, tone: 'neutral' }
              return (
                <tr
                  key={item.id}
                  className={item.status === 'Running' ? 'media-caption-job-row-running' : undefined}
                  data-status={item.status}
                >
                  <td><img className="media-caption-job-thumb" src={item.previewUrl} alt={item.fileName} /></td>
                  <td>{item.fileName}</td>
                  <td><StatusBadge label={meta.label} tone={meta.tone} /></td>
                  <td>{item.error ?? ''}</td>
                  <td>
                    {item.status === 'Failed' && (
                      <button
                        type="button"
                        className="btn btn-ghost btn-sm"
                        disabled={retryItem.isPending}
                        onClick={() => run(retryItem, item.id, 'Đã đưa ảnh vào hàng chờ')}
                      >
                        Retry
                      </button>
                    )}
                  </td>
                </tr>
              )
            })}
          </tbody>
        </table>
      </div>
    </section>
  )
}
