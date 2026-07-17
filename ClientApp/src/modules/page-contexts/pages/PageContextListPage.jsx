import { useMemo, useState } from 'react'
import PageHeader from '@/shared/components/PageHeader'
import LoadingState from '@/shared/components/LoadingState'
import ErrorState from '@/shared/components/ErrorState'
import EmptyState from '@/shared/components/EmptyState'
import { formatDateTime, getErrorMessage } from '@/shared/utils/apiHelpers'
import { useSocialChannelAll } from '@/modules/social-channels/hooks/useSocialChannels'
import PageContextFormModal from '../components/PageContextFormModal'
import {
  useCreatePageContext,
  useDeletePageContext,
  usePageContextList,
  useUpdatePageContext,
} from '../hooks/usePageContexts'

export default function PageContextListPage() {
  const [keyword, setKeyword] = useState('')
  const [modalOpen, setModalOpen] = useState(false)
  const [editingItem, setEditingItem] = useState(null)
  const [formError, setFormError] = useState('')

  const params = useMemo(() => ({ keyword, index: 1, size: 50 }), [keyword])
  const { data, isLoading, isError, error, refetch } = usePageContextList(params)
  const { data: channels = [] } = useSocialChannelAll()
  const createMutation = useCreatePageContext()
  const updateMutation = useUpdatePageContext()
  const deleteMutation = useDeletePageContext()

  const channelMap = useMemo(
    () => Object.fromEntries(channels.map((c) => [c.id, c.pageName])),
    [channels],
  )

  const items = data?.items ?? []

  const openCreate = () => {
    setEditingItem(null)
    setFormError('')
    setModalOpen(true)
  }

  const openEdit = (item) => {
    setEditingItem(item)
    setFormError('')
    setModalOpen(true)
  }

  const handleSubmit = async (payload) => {
    try {
      setFormError('')
      if (editingItem) {
        const { socialChannelId: _socialChannelId, ...updatePayload } = payload
        await updateMutation.mutateAsync({ id: editingItem.id, payload: updatePayload })
      } else {
        await createMutation.mutateAsync(payload)
      }
      setModalOpen(false)
    } catch (submitError) {
      setFormError(getErrorMessage(submitError))
    }
  }

  const handleDelete = async (item) => {
    if (!window.confirm(`Xóa page context "${item.brandName}"?`)) return
    try {
      await deleteMutation.mutateAsync(item.id)
    } catch (deleteError) {
      window.alert(getErrorMessage(deleteError))
    }
  }

  return (
    <section>
      <PageHeader
        title="Page Context"
        description="Branding, prompt template và CTA cho từng kênh"
        actions={(
          <button type="button" className="btn btn-primary" onClick={openCreate}>
            Thêm context
          </button>
        )}
      />

      <div className="card card-body" style={{ marginBottom: 16 }}>
        <div className="form-group" style={{ marginBottom: 0 }}>
          <label htmlFor="context-keyword">Tìm kiếm</label>
          <input
            id="context-keyword"
            value={keyword}
            onChange={(event) => setKeyword(event.target.value)}
            placeholder="Tên thương hiệu..."
          />
        </div>
      </div>

      <div className="card">
        {isLoading && <LoadingState />}
        {isError && <ErrorState message={getErrorMessage(error)} onRetry={refetch} />}
        {!isLoading && !isError && items.length === 0 && (
          <EmptyState message="Chưa có page context nào" />
        )}
        {!isLoading && !isError && items.length > 0 && (
          <table>
            <thead>
              <tr>
                <th>Thương hiệu</th>
                <th>Kênh</th>
                <th>CTA</th>
                <th>Cập nhật</th>
                <th />
              </tr>
            </thead>
            <tbody>
              {items.map((item) => (
                <tr key={item.id}>
                  <td>{item.brandName}</td>
                  <td>{channelMap[item.socialChannelId] || item.socialChannelId}</td>
                  <td>{item.ctaText || '—'}</td>
                  <td>{formatDateTime(item.updatedAt || item.createdAt)}</td>
                  <td>
                    <div style={{ display: 'flex', gap: 8, justifyContent: 'flex-end' }}>
                      <button
                        type="button"
                        className="btn btn-ghost"
                        onClick={() => openEdit(item)}
                      >
                        Sửa
                      </button>
                      <button
                        type="button"
                        className="btn btn-danger"
                        onClick={() => handleDelete(item)}
                      >
                        Xóa
                      </button>
                    </div>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </div>

      <PageContextFormModal
        open={modalOpen}
        onClose={() => setModalOpen(false)}
        initialData={editingItem}
        onSubmit={handleSubmit}
        isSubmitting={createMutation.isPending || updateMutation.isPending}
        errorMessage={formError}
      />
    </section>
  )
}
