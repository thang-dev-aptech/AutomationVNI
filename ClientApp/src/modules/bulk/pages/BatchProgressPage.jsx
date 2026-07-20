import { useState } from 'react'
import { Link, useParams } from 'react-router-dom'
import PageHeader from '@/shared/components/PageHeader'
import LoadingState from '@/shared/components/LoadingState'
import ErrorState from '@/shared/components/ErrorState'
import { formatDateTime, getErrorMessage } from '@/shared/utils/apiHelpers'
import { toast } from '@/shared/stores/toastStore'
import { getPostStatusMeta } from '@/modules/posts/constants/postStatus'
import { useBatch, useBulkApprove, useBulkSchedule } from '../hooks/useBulk'

export default function BatchProgressPage() {
  const { batchId } = useParams()
  const [timeSlots, setTimeSlots] = useState('08:00, 12:00, 20:00')
  const [timezone] = useState('Asia/Ho_Chi_Minh')

  const { data, isLoading, isError, error, refetch, isFetching } = useBatch(batchId)
  const approveMutation = useBulkApprove()
  const scheduleMutation = useBulkSchedule()

  const byStatus = data?.byStatus ?? {}
  const posts = data?.posts ?? []
  const pending =
    (byStatus.Queued ?? 0) + (byStatus.Generating ?? 0) +
    (byStatus.GeneratingMedia ?? 0) + (byStatus.RenderingTemplate ?? 0)
  const waitingReview = byStatus.WaitingReview ?? 0
  const approved = byStatus.Approved ?? 0

  const handleApprove = async () => {
    try {
      const res = await approveMutation.mutateAsync({ batchId })
      toast.success(res?.message || 'Đã duyệt')
      refetch()
    } catch (e) { toast.error(getErrorMessage(e)) }
  }

  const handleSchedule = async () => {
    const slots = timeSlots.split(',').map((s) => s.trim()).filter(Boolean)
    try {
      const res = await scheduleMutation.mutateAsync({ batchId, timeSlots: slots, timezone })
      toast.success(res?.message || 'Đã lên lịch')
      refetch()
    } catch (e) { toast.error(getErrorMessage(e)) }
  }

  if (isLoading) return <LoadingState message="Đang tải batch..." />
  if (isError) return <ErrorState message={getErrorMessage(error)} onRetry={refetch} />

  return (
    <section>
      <PageHeader
        title="Tiến độ batch"
        description={`${data?.total ?? 0} bài${pending > 0 ? ' — đang sinh nội dung nền (xong → Đã duyệt)...' : ''}`}
        actions={<Link to="/bulk" className="btn btn-secondary">+ Tạo lô mới</Link>}
      />

      {/* Tổng quan trạng thái */}
      <div className="card card-body" style={{ marginBottom: 16 }}>
        <div style={{ display: 'flex', gap: 10, flexWrap: 'wrap' }}>
          {Object.entries(byStatus).map(([name, count]) => (
            <span key={name} className="badge" style={{ padding: '4px 10px', border: '1px solid var(--border,#ddd)', borderRadius: 6 }}>
              {name}: <strong>{count}</strong>
            </span>
          ))}
          {isFetching && <span style={{ color: 'var(--text-muted,#888)' }}>đang cập nhật…</span>}
        </div>
      </div>

      {/* Hành động loạt — Duyệt chỉ còn cho bài Chờ duyệt cũ (lô trước khi auto-approve) */}
      <div className="card card-body" style={{ marginBottom: 16, display: 'flex', gap: 16, flexWrap: 'wrap', alignItems: 'flex-end' }}>
        {waitingReview > 0 && (
          <button type="button" className="btn btn-secondary" onClick={handleApprove}
            disabled={approveMutation.isPending}>
            Duyệt {waitingReview} bài còn chờ
          </button>
        )}

        <div className="form-group" style={{ marginBottom: 0, minWidth: 220 }}>
          <label htmlFor="batch-slots">Khung giờ rải (local, cách nhau dấu phẩy)</label>
          <input id="batch-slots" value={timeSlots} onChange={(e) => setTimeSlots(e.target.value)} placeholder="08:00, 12:00, 20:00" />
        </div>
        <button type="button" className="btn btn-primary" onClick={handleSchedule}
          disabled={scheduleMutation.isPending || approved === 0}>
          Rải lịch {approved > 0 ? `${approved} bài` : ''}
        </button>
      </div>

      {/* Danh sách bài */}
      <div className="card">
        <table>
          <thead>
            <tr><th>Tiêu đề</th><th>Trạng thái</th><th>Lịch đăng</th><th /></tr>
          </thead>
          <tbody>
            {posts.map((p) => {
              const meta = getPostStatusMeta(p.status)
              return (
                <tr key={p.id}>
                  <td>{p.title}</td>
                  <td><span className={`badge badge-${meta.tone}`}>{meta.label}</span></td>
                  <td>{p.scheduledPublishAt ? formatDateTime(p.scheduledPublishAt) : '—'}</td>
                  <td style={{ textAlign: 'right' }}>
                    <Link to={`/posts/${p.id}`} className="btn btn-ghost">Xem</Link>
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
