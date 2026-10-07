import { useState } from 'react'
import { useNavigate } from 'react-router-dom'
import PageHeader from '@/shared/components/PageHeader'
import LoadingState from '@/shared/components/LoadingState'
import ErrorState from '@/shared/components/ErrorState'
import EmptyState from '@/shared/components/EmptyState'
import { formatDateTime, getErrorMessage, unwrapApiData } from '@/shared/utils/apiHelpers'
import { toast } from '@/shared/stores/toastStore'
import { usePermissions } from '@/shared/hooks/usePermissions'
import { ROLES } from '@/shared/auth/permissions'
import CampaignFormModal from '../components/CampaignFormModal'
import {
  mediaTypeLabel,
  scheduleModeLabel,
  statusLabel,
} from '../constants/campaignEnums'
import {
  useCampaignSummaries,
  useCreateCampaign,
  useUpdateCampaign,
} from '../hooks/useCampaigns'
import { campaignApi } from '../services/campaignApi'

export default function CampaignListPage() {
  const navigate = useNavigate()
  const { hasRole } = usePermissions()
  const canManage = hasRole([ROLES.ADMIN, ROLES.CONTENT_MANAGER])

  const { data: items = [], isLoading, isError, error, refetch } = useCampaignSummaries()
  const createMutation = useCreateCampaign()
  const updateMutation = useUpdateCampaign()

  const [modalOpen, setModalOpen] = useState(false)
  const [editing, setEditing] = useState(null)
  const [formError, setFormError] = useState('')
  const [loadingEditId, setLoadingEditId] = useState(null)

  const openCreate = () => {
    setEditing(null)
    setFormError('')
    setModalOpen(true)
  }

  const openEdit = async (summary) => {
    setLoadingEditId(summary.id)
    setFormError('')
    try {
      const full = unwrapApiData(await campaignApi.getById(summary.id))
      setEditing(full)
      setModalOpen(true)
    } catch (err) {
      toast.error(getErrorMessage(err))
    } finally {
      setLoadingEditId(null)
    }
  }

  const handleSubmit = async (payload) => {
    try {
      setFormError('')
      if (editing?.id) {
        await updateMutation.mutateAsync({ id: editing.id, payload })
        toast.success('Đã cập nhật chiến dịch')
      } else {
        await createMutation.mutateAsync(payload)
        toast.success('Đã tạo chiến dịch')
      }
      setModalOpen(false)
    } catch (err) {
      setFormError(getErrorMessage(err))
    }
  }

  return (
    <section>
      <PageHeader
        title="Chiến dịch"
        description="Lặp hàng tuần: sinh bài cuốn chiếu theo kênh / nhóm kênh"
        actions={
          canManage ? (
            <button type="button" className="btn btn-primary" onClick={openCreate}>
              Tạo chiến dịch
            </button>
          ) : null
        }
      />

      <div className="card">
        {isLoading && <LoadingState />}
        {isError && <ErrorState message={getErrorMessage(error)} onRetry={refetch} />}
        {!isLoading && !isError && items.length === 0 && (
          <EmptyState
            message="Chưa có chiến dịch nào"
            action={
              canManage ? (
                <button type="button" className="btn btn-primary" onClick={openCreate}>
                  Tạo chiến dịch đầu tiên
                </button>
              ) : null
            }
          />
        )}
        {!isLoading && !isError && items.length > 0 && (
          <div className="table-container">
            <table data-testid="campaign-list-table">
              <thead>
                <tr>
                  <th>Tên</th>
                  <th>Trạng thái</th>
                  <th>Loại</th>
                  <th>Lịch</th>
                  <th>Page</th>
                  <th>Sắp tới</th>
                  <th>Đã đăng</th>
                  <th>Thất bại</th>
                  <th>Bắt đầu</th>
                  <th />
                </tr>
              </thead>
              <tbody>
                {items.map((item) => (
                  <tr
                    key={item.id}
                    data-testid={`campaign-row-${item.id}`}
                    style={{ cursor: 'pointer' }}
                    onClick={() => navigate(`/campaigns/${item.id}`)}
                  >
                    <td>{item.name}</td>
                    <td>{statusLabel(item.status)}</td>
                    <td>{mediaTypeLabel(item.mediaType)}</td>
                    <td>{scheduleModeLabel(item.scheduleMode)}</td>
                    <td>{item.pageCount ?? 0}</td>
                    <td>{item.upcomingCount ?? 0}</td>
                    <td>{item.publishedCount ?? 0}</td>
                    <td>{item.failedCount ?? 0}</td>
                    <td>{formatDateTime(item.startDate)}</td>
                    <td onClick={(e) => e.stopPropagation()}>
                      {canManage ? (
                        <button
                          type="button"
                          className="btn btn-ghost"
                          disabled={loadingEditId === item.id}
                          onClick={() => openEdit(item)}
                        >
                          Sửa
                        </button>
                      ) : null}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>

      <CampaignFormModal
        open={modalOpen}
        onClose={() => setModalOpen(false)}
        initialData={editing}
        onSubmit={handleSubmit}
        isSubmitting={createMutation.isPending || updateMutation.isPending}
        errorMessage={formError}
      />
    </section>
  )
}
