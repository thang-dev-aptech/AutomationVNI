import { useState } from 'react'
import { Link, useNavigate, useParams } from 'react-router-dom'
import PageHeader from '@/shared/components/PageHeader'
import LoadingState from '@/shared/components/LoadingState'
import ErrorState from '@/shared/components/ErrorState'
import { formatDateTime, getErrorMessage } from '@/shared/utils/apiHelpers'
import { confirmAction } from '@/shared/utils/confirmAction'
import { toast } from '@/shared/stores/toastStore'
import { usePermissions } from '@/shared/hooks/usePermissions'
import { ROLES } from '@/shared/auth/permissions'
import CampaignFormModal from '../components/CampaignFormModal'
import {
  CAMPAIGN_STATUS,
  mediaTypeLabel,
  scheduleModeLabel,
  statusLabel,
  WEEKDAY_OPTIONS,
} from '../constants/campaignEnums'
import {
  useCampaignDetail,
  useDeleteCampaign,
  useEndCampaign,
  usePauseCampaign,
  useResumeCampaign,
  useUpdateCampaign,
} from '../hooks/useCampaigns'

export default function CampaignDetailPage() {
  const { id } = useParams()
  const navigate = useNavigate()
  const { hasRole } = usePermissions()
  const canManage = hasRole([ROLES.ADMIN, ROLES.CONTENT_MANAGER])

  const { data, isLoading, isError, error, refetch } = useCampaignDetail(id)
  const campaign = data?.campaign
  const pages = data?.pages ?? []

  const pauseMutation = usePauseCampaign()
  const resumeMutation = useResumeCampaign()
  const endMutation = useEndCampaign()
  const deleteMutation = useDeleteCampaign()
  const updateMutation = useUpdateCampaign()

  const [editOpen, setEditOpen] = useState(false)
  const [formError, setFormError] = useState('')

  const busy = pauseMutation.isPending
    || resumeMutation.isPending
    || endMutation.isPending
    || deleteMutation.isPending
    || updateMutation.isPending

  const runConfirmed = async (message, action, successMsg) => {
    if (!confirmAction(message)) return
    try {
      await action()
      toast.success(successMsg)
      refetch()
    } catch (err) {
      toast.error(getErrorMessage(err))
    }
  }

  const handlePause = () => runConfirmed(
    `Tạm dừng chiến dịch "${campaign?.name}"? Các bài chưa đăng trong tương lai sẽ bị huỷ lịch.`,
    () => pauseMutation.mutateAsync(id),
    'Đã tạm dừng chiến dịch',
  )

  const handleResume = () => runConfirmed(
    `Tiếp tục chiến dịch "${campaign?.name}"? Hệ thống sẽ sinh lại bài từ thời điểm hiện tại.`,
    () => resumeMutation.mutateAsync(id),
    'Đã tiếp tục chiến dịch',
  )

  const handleEnd = () => runConfirmed(
    `Kết thúc chiến dịch "${campaign?.name}"? Không thể tiếp tục sau khi kết thúc.`,
    () => endMutation.mutateAsync(id),
    'Đã kết thúc chiến dịch',
  )

  const handleDelete = () => runConfirmed(
    `Xoá chiến dịch "${campaign?.name}"? Chiến dịch sẽ biến khỏi danh sách; bài chưa đăng bị huỷ lịch.`,
    async () => {
      await deleteMutation.mutateAsync(id)
      navigate('/campaigns')
    },
    'Đã xoá chiến dịch',
  )

  const handleEdit = () => {
    if (!confirmAction(`Sửa cấu hình chiến dịch "${campaign?.name}"?`)) return
    setFormError('')
    setEditOpen(true)
  }

  const handleSubmitEdit = async (payload) => {
    try {
      setFormError('')
      await updateMutation.mutateAsync({ id, payload })
      toast.success('Đã cập nhật chiến dịch')
      setEditOpen(false)
      refetch()
    } catch (err) {
      setFormError(getErrorMessage(err))
    }
  }

  const weekdayText = (campaign?.weekdays ?? [])
    .map((d) => WEEKDAY_OPTIONS.find((o) => o.value === d)?.label || d)
    .join(', ')

  const actionButtons = canManage && campaign ? (
    <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap' }} data-testid="campaign-lifecycle-actions">
      {campaign.status === CAMPAIGN_STATUS.Running ? (
        <button
          type="button"
          className="btn btn-secondary"
          data-testid="campaign-pause"
          disabled={busy}
          onClick={handlePause}
        >
          Tạm dừng
        </button>
      ) : null}
      {campaign.status === CAMPAIGN_STATUS.Paused ? (
        <button
          type="button"
          className="btn btn-success"
          data-testid="campaign-resume"
          disabled={busy}
          onClick={handleResume}
        >
          Tiếp tục
        </button>
      ) : null}
      {campaign.status !== CAMPAIGN_STATUS.Ended ? (
        <>
          <button
            type="button"
            className="btn btn-ghost"
            data-testid="campaign-edit"
            disabled={busy}
            onClick={handleEdit}
          >
            Sửa
          </button>
          <button
            type="button"
            className="btn btn-secondary"
            data-testid="campaign-end"
            disabled={busy}
            onClick={handleEnd}
          >
            Kết thúc
          </button>
        </>
      ) : null}
      <button
        type="button"
        className="btn btn-danger"
        data-testid="campaign-delete"
        disabled={busy}
        onClick={handleDelete}
      >
        Xoá
      </button>
    </div>
  ) : null

  return (
    <section data-testid="campaign-detail">
      <PageHeader
        title={campaign?.name || 'Chi tiết chiến dịch'}
        description={campaign ? statusLabel(campaign.status) : 'Đang tải...'}
        actions={(
          <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap', alignItems: 'center' }}>
            {actionButtons}
            <Link to="/campaigns" className="btn btn-ghost">
              ← Danh sách
            </Link>
          </div>
        )}
      />

      {isLoading && <LoadingState />}
      {isError && <ErrorState message={getErrorMessage(error)} onRetry={refetch} />}

      {!isLoading && !isError && campaign && (
        <>
          <div className="card card-body" style={{ marginBottom: 16 }} data-testid="campaign-info">
            <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(180px, 1fr))', gap: 12 }}>
              <div>
                <div className="text-muted" style={{ fontSize: '0.85rem' }}>Loại</div>
                <div>{mediaTypeLabel(campaign.mediaType)}</div>
              </div>
              <div>
                <div className="text-muted" style={{ fontSize: '0.85rem' }}>Lịch</div>
                <div>
                  {scheduleModeLabel(campaign.scheduleMode)}
                  {campaign.scheduleMode === 1 && weekdayText ? ` · ${weekdayText}` : ''}
                </div>
              </div>
              <div>
                <div className="text-muted" style={{ fontSize: '0.85rem' }}>Giờ đăng</div>
                <div>{(campaign.publishTimes ?? []).join(', ') || '—'}</div>
              </div>
              <div>
                <div className="text-muted" style={{ fontSize: '0.85rem' }}>Lệch ± phút</div>
                <div>{campaign.jitterMinutes ?? 0}</div>
              </div>
              <div>
                <div className="text-muted" style={{ fontSize: '0.85rem' }}>Bắt đầu</div>
                <div>{formatDateTime(campaign.startDate)}</div>
              </div>
              <div>
                <div className="text-muted" style={{ fontSize: '0.85rem' }}>Kết thúc</div>
                <div>{campaign.endDate ? formatDateTime(campaign.endDate) : '—'}</div>
              </div>
            </div>
          </div>

          <div className="card">
            <div className="table-container">
              <table data-testid="campaign-pages-table">
                <thead>
                  <tr>
                    <th>Page</th>
                    <th>Lên lịch</th>
                    <th>Đã đăng</th>
                    <th>Thất bại</th>
                    <th>Bài kế tiếp</th>
                    <th>Cảnh báo</th>
                  </tr>
                </thead>
                <tbody>
                  {pages.length === 0 ? (
                    <tr>
                      <td colSpan={6} className="text-muted">
                        Chưa có page đang chạy
                      </td>
                    </tr>
                  ) : (
                    pages.map((p) => (
                      <tr key={p.socialChannelId} data-testid={`page-row-${p.socialChannelId}`}>
                        <td>
                          <Link
                            to={`/campaigns/${id}/pages/${p.socialChannelId}`}
                            data-testid={`page-link-${p.socialChannelId}`}
                          >
                            {p.channelName || p.socialChannelId}
                          </Link>
                        </td>
                        <td>{p.scheduledCount}</td>
                        <td>{p.publishedCount}</td>
                        <td>{p.failedCount}</td>
                        <td>{formatDateTime(p.nextScheduledAt)}</td>
                        <td>
                          {p.warning ? (
                            <span className="text-danger" data-testid={`page-warning-${p.socialChannelId}`}>
                              {p.warning}
                            </span>
                          ) : '—'}
                        </td>
                      </tr>
                    ))
                  )}
                </tbody>
              </table>
            </div>
          </div>
        </>
      )}

      <CampaignFormModal
        open={editOpen}
        onClose={() => setEditOpen(false)}
        initialData={campaign}
        onSubmit={handleSubmitEdit}
        isSubmitting={updateMutation.isPending}
        errorMessage={formError}
      />
    </section>
  )
}
