import { getGenerationJobActions } from '../constants/jobConstants'
import { usePermissions } from '@/shared/hooks/usePermissions'
import { confirmAction, CONFIRM_MESSAGES } from '@/shared/utils/confirmAction'
import { toast } from '@/shared/stores/toastStore'
import { getErrorMessage } from '@/shared/utils/apiHelpers'
import {
  useCancelGenerationJob,
  useProcessGenerationJob,
  useRetryGenerationJob,
} from '../hooks/useGenerationJobs'

export default function JobActionButtons({ job, onViewError, onActionError }) {
  const { canManageJobs } = usePermissions()
  const statusActions = getGenerationJobActions(job.status)
  const actions = canManageJobs
    ? statusActions
    : { canProcess: false, canRetry: false, canCancel: false }
  const processMutation = useProcessGenerationJob()
  const retryMutation = useRetryGenerationJob()
  const cancelMutation = useCancelGenerationJob()

  const isBusy =
    processMutation.isPending ||
    retryMutation.isPending ||
    cancelMutation.isPending

  const run = async (fn, successMessage) => {
    try {
      await fn()
      if (successMessage) toast.success(successMessage)
    } catch (error) {
      const message = getErrorMessage(error)
      onActionError?.(error)
      toast.error(message)
    }
  }

  const handleProcess = () => {
    if (!confirmAction(CONFIRM_MESSAGES.processJob(job.id))) return
    run(() => processMutation.mutateAsync(job.id), 'Đã chạy process job')
  }

  const handleRetry = () => {
    if (!confirmAction(CONFIRM_MESSAGES.retryJob(job.id))) return
    run(() => retryMutation.mutateAsync(job.id), 'Đã retry job')
  }

  const handleCancel = () => {
    if (!confirmAction(CONFIRM_MESSAGES.cancelJob(job.id))) return
    run(() => cancelMutation.mutateAsync(job.id), 'Đã hủy job')
  }

  return (
    <div className="job-actions">
      {actions.canProcess && (
        <button
          type="button"
          className="btn btn-primary btn-sm"
          disabled={isBusy}
          onClick={handleProcess}
        >
          {processMutation.isPending ? 'Processing...' : 'Process'}
        </button>
      )}
      {actions.canRetry && (
        <button
          type="button"
          className="btn btn-secondary btn-sm"
          disabled={isBusy}
          onClick={handleRetry}
        >
          {retryMutation.isPending ? 'Retrying...' : 'Retry'}
        </button>
      )}
      {actions.canCancel && (
        <button
          type="button"
          className="btn btn-danger btn-sm"
          disabled={isBusy}
          onClick={handleCancel}
        >
          {cancelMutation.isPending ? 'Cancelling...' : 'Cancel'}
        </button>
      )}
      {(job.errorMessage || job.errorCode) && (
        <button
          type="button"
          className="btn btn-ghost btn-sm"
          onClick={() => onViewError(job)}
        >
          Error
        </button>
      )}
    </div>
  )
}
