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
  // Bắt đầu rải từ mốc nào (datetime-local, giờ máy). Rỗng = từ bây giờ.
  const [startAt, setStartAt] = useState('')
  // Lệch ngẫu nhiên ± phút quanh khung giờ, tránh đăng khít cùng một phút mỗi ngày.
  const [jitterMinutes, setJitterMinutes] = useState(10)

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
    if (slots.length === 0) {
      toast.error('Nhập ít nhất một khung giờ, ví dụ 09:00')
      return
    }
    const payload = {
      batchId,
      timeSlots: slots,
      timezone,
      jitterMinutes: Number(jitterMinutes) || 0,
    }
    // datetime-local là giờ máy → đổi sang UTC cho backend.
    if (startAt) {
      const d = new Date(startAt)
      if (Number.isNaN(d.getTime())) {
        toast.error('Mốc bắt đầu không hợp lệ')
        return
      }
      payload.startAtUtc = d.toISOString()
    }
    try {
      const res = await scheduleMutation.mutateAsync(payload)
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
          <small style={{ color: 'var(--text-muted,#888)' }}>
            Mỗi khung = 1 bài/ngày. Ví dụ “09:00” là 1 bài/ngày, “09:00, 15:00” là 2 bài/ngày.
          </small>
        </div>

        <div className="form-group" style={{ marginBottom: 0, minWidth: 200 }}>
          <label htmlFor="batch-start">Bắt đầu từ</label>
          <input id="batch-start" type="datetime-local" value={startAt}
            onChange={(e) => setStartAt(e.target.value)} />
          <small style={{ color: 'var(--text-muted,#888)' }}>Để trống = từ bây giờ</small>
        </div>

        <div className="form-group" style={{ marginBottom: 0, minWidth: 150 }}>
          <label htmlFor="batch-jitter">Lệch ngẫu nhiên (± phút)</label>
          <input id="batch-jitter" type="number" min="0" max="240" value={jitterMinutes}
            onChange={(e) => setJitterMinutes(e.target.value)} />
          <small style={{ color: 'var(--text-muted,#888)' }}>0 = đăng đúng khung giờ</small>
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
                    {/* Mở cùng tab — trang chi tiết đã có nút "← Về lô rải lịch" để quay lại */}
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
