import { useState } from 'react'
import Modal from '@/shared/components/Modal'
import { usePermissions } from '@/shared/hooks/usePermissions'
import { getErrorMessage } from '@/shared/utils/apiHelpers'
import { confirmAction, CONFIRM_MESSAGES } from '@/shared/utils/confirmAction'
import { toast } from '@/shared/stores/toastStore'
import { getAvailableWorkflowActions } from '../constants/postStatus'
import {
  useApprovePost,
  useCancelSchedulePost,
  useDeletePost,
  usePublishNowPost,
  useRejectPost,
  useSchedulePost,
  useSubmitPostReview,
} from '../hooks/usePosts'

export default function PostWorkflowActions({ post, onDeleted }) {
  const {
    canSubmitReview,
    canApprovePost,
    canRejectPost,
    canSchedulePost,
    canPublishPost,
    canDeletePost,
  } = usePermissions()

  const statusActions = getAvailableWorkflowActions(post.status)
  const actions = {
    submitReview: statusActions.submitReview && canSubmitReview,
    approve: statusActions.approve && canApprovePost,
    reject: statusActions.reject && canRejectPost,
    schedule: statusActions.schedule && canSchedulePost,
    cancelSchedule: statusActions.cancelSchedule && canSchedulePost,
    publishNow: statusActions.publishNow && canPublishPost,
    canDelete: statusActions.canDelete && canDeletePost(post.userId),
  }

  const submitReview = useSubmitPostReview()
  const approve = useApprovePost()
  const reject = useRejectPost()
  const schedule = useSchedulePost()
  const cancelSchedule = useCancelSchedulePost()
  const publishNow = usePublishNowPost()
  const deletePost = useDeletePost()

  const [rejectOpen, setRejectOpen] = useState(false)
  const [scheduleOpen, setScheduleOpen] = useState(false)
  const [rejectReason, setRejectReason] = useState('')
  const [scheduledAt, setScheduledAt] = useState('')
  const [actionError, setActionError] = useState('')

  const isBusy =
    submitReview.isPending ||
    approve.isPending ||
    reject.isPending ||
    schedule.isPending ||
    cancelSchedule.isPending ||
    publishNow.isPending ||
    deletePost.isPending

  const runAction = async (fn) => {
    setActionError('')
    try {
      await fn()
    } catch (error) {
      setActionError(getErrorMessage(error))
    }
  }

  const handleReject = async () => {
    if (!rejectReason.trim()) {
      setActionError('Vui lòng nhập lý do từ chối')
      return
    }
    await runAction(() => reject.mutateAsync({ id: post.id, reason: rejectReason.trim() }))
    setRejectOpen(false)
    setRejectReason('')
  }

  const handleSchedule = async () => {
    if (!scheduledAt) {
      setActionError('Vui lòng chọn thời gian lên lịch')
      return
    }
    await runAction(() =>
      schedule.mutateAsync({
        id: post.id,
        scheduledAt: new Date(scheduledAt).toISOString(),
        timezone: Intl.DateTimeFormat().resolvedOptions().timeZone,
      }),
    )
    setScheduleOpen(false)
    setScheduledAt('')
  }

  const handlePublishNow = async () => {
    if (!confirmAction(CONFIRM_MESSAGES.publishNow())) return
    await runAction(() => publishNow.mutateAsync(post.id))
  }

  const handleDelete = async () => {
    if (!confirmAction(CONFIRM_MESSAGES.deletePost(post.title))) return
    await runAction(async () => {
      await deletePost.mutateAsync(post.id)
      toast.success('Đã xóa bài viết')
      onDeleted?.()
    })
  }

  const handleSubmitReview = async () => {
    if (!confirmAction(CONFIRM_MESSAGES.submitReview())) return
    await runAction(() => submitReview.mutateAsync(post.id))
  }

  const handleApprove = async () => {
    if (!confirmAction(CONFIRM_MESSAGES.approvePost())) return
    await runAction(() => approve.mutateAsync(post.id))
  }

  const handleCancelSchedule = async () => {
    if (!confirmAction(CONFIRM_MESSAGES.cancelSchedule())) return
    await runAction(() => cancelSchedule.mutateAsync(post.id))
  }

  const hasAnyAction =
    actions.submitReview ||
    actions.approve ||
    actions.reject ||
    actions.schedule ||
    actions.cancelSchedule ||
    actions.publishNow ||
    actions.canDelete

  if (!hasAnyAction) return null

  return (
    <div className="card card-body" style={{ marginBottom: 16 }}>
      <h2 style={{ margin: '0 0 12px', fontSize: '1.05rem' }}>Thao tác workflow</h2>
      {actionError && <div className="alert alert-error">{actionError}</div>}
      <div style={{ display: 'flex', flexWrap: 'wrap', gap: 8 }}>
        {actions.submitReview && (
          <button
            type="button"
            className="btn btn-primary"
            disabled={isBusy}
            onClick={handleSubmitReview}
          >
            {submitReview.isPending ? 'Đang gửi...' : 'Gửi duyệt'}
          </button>
        )}
        {actions.approve && (
          <button
            type="button"
            className="btn btn-primary"
            disabled={isBusy}
            onClick={handleApprove}
          >
            {approve.isPending ? 'Đang duyệt...' : 'Duyệt'}
          </button>
        )}
        {actions.reject && (
          <button
            type="button"
            className="btn btn-secondary"
            disabled={isBusy}
            onClick={() => { setActionError(''); setRejectOpen(true) }}
          >
            Từ chối
          </button>
        )}
        {actions.schedule && (
          <button
            type="button"
            className="btn btn-secondary"
            disabled={isBusy}
            onClick={() => { setActionError(''); setScheduleOpen(true) }}
          >
            Lên lịch đăng
          </button>
        )}
        {actions.cancelSchedule && (
          <button
            type="button"
            className="btn btn-secondary"
            disabled={isBusy}
            onClick={handleCancelSchedule}
          >
            {cancelSchedule.isPending ? 'Đang hủy...' : 'Hủy lịch'}
          </button>
        )}
        {actions.publishNow && (
          <button
            type="button"
            className="btn btn-primary"
            disabled={isBusy}
            onClick={handlePublishNow}
          >
            {publishNow.isPending ? 'Đang đăng...' : 'Đăng ngay'}
          </button>
        )}
        {actions.canDelete && (
          <button
            type="button"
            className="btn btn-danger"
            disabled={isBusy}
            onClick={handleDelete}
          >
            {deletePost.isPending ? 'Đang xóa...' : 'Xóa'}
          </button>
        )}
      </div>

      <Modal
        open={rejectOpen}
        title="Từ chối bài viết"
        onClose={() => setRejectOpen(false)}
        footer={(
          <>
            <button type="button" className="btn btn-secondary" onClick={() => setRejectOpen(false)}>
              Hủy
            </button>
            <button
              type="button"
              className="btn btn-danger"
              disabled={reject.isPending}
              onClick={handleReject}
            >
              {reject.isPending ? 'Đang xử lý...' : 'Từ chối'}
            </button>
          </>
        )}
      >
        <div className="form-group">
          <label htmlFor="reject-reason">Lý do từ chối *</label>
          <textarea
            id="reject-reason"
            value={rejectReason}
            onChange={(event) => setRejectReason(event.target.value)}
            rows={4}
            required
          />
        </div>
      </Modal>

      <Modal
        open={scheduleOpen}
        title="Lên lịch đăng bài"
        onClose={() => setScheduleOpen(false)}
        footer={(
          <>
            <button type="button" className="btn btn-secondary" onClick={() => setScheduleOpen(false)}>
              Hủy
            </button>
            <button
              type="button"
              className="btn btn-primary"
              disabled={schedule.isPending}
              onClick={handleSchedule}
            >
              {schedule.isPending ? 'Đang lưu...' : 'Lên lịch'}
            </button>
          </>
        )}
      >
        <div className="form-group">
          <label htmlFor="schedule-at">Thời gian đăng *</label>
          <input
            id="schedule-at"
            type="datetime-local"
            value={scheduledAt}
            onChange={(event) => setScheduledAt(event.target.value)}
            required
          />
        </div>
      </Modal>
    </div>
  )
}
