import { useMemo, useState } from 'react'
import PageHeader from '@/shared/components/PageHeader'
import LoadingState from '@/shared/components/LoadingState'
import ErrorState from '@/shared/components/ErrorState'
import EmptyState from '@/shared/components/EmptyState'
import { formatDateTime, getErrorMessage } from '@/shared/utils/apiHelpers'
import { toast } from '@/shared/stores/toastStore'
import PromptTemplateFormModal from '../components/PromptTemplateFormModal'
import PromptTemplateImportModal from '../components/PromptTemplateImportModal'
import {
  useBulkImportPromptTemplates,
  useCreatePromptTemplate,
  useDeletePromptTemplate,
  usePromptTemplateList,
  useUpdatePromptTemplate,
} from '../hooks/usePromptTemplates'

export default function PromptTemplateListPage() {
  const [keyword, setKeyword] = useState('')
  const [modalOpen, setModalOpen] = useState(false)
  const [importOpen, setImportOpen] = useState(false)
  const [editingItem, setEditingItem] = useState(null)
  const [formError, setFormError] = useState('')

  const params = useMemo(
    () => ({
      keyword,
      isActive: undefined,
      index: 1,
      size: 100,
    }),
    [keyword],
  )

  const { data, isLoading, isError, error, refetch } = usePromptTemplateList(params)
  const createMutation = useCreatePromptTemplate()
  const updateMutation = useUpdatePromptTemplate()
  const deleteMutation = useDeletePromptTemplate()
  const importMutation = useBulkImportPromptTemplates()

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
        await updateMutation.mutateAsync({ id: editingItem.id, payload })
      } else {
        await createMutation.mutateAsync(payload)
      }
      setModalOpen(false)
      toast.success(editingItem ? 'Đã cập nhật danh mục' : 'Đã thêm danh mục')
    } catch (submitError) {
      setFormError(getErrorMessage(submitError))
    }
  }

  const handleDelete = async (item) => {
    if (!window.confirm(`Xóa danh mục template "${item.name}"?`)) return
    try {
      await deleteMutation.mutateAsync(item.id)
      toast.success('Đã xóa danh mục')
    } catch (deleteError) {
      toast.error(getErrorMessage(deleteError))
    }
  }

  const handleImport = async (payload) => {
    try {
      const result = await importMutation.mutateAsync(payload)
      toast.success(result?.message || 'Import xong')
      if (result?.errors?.length) {
        toast.warning(`${result.errors.length} mục có lỗi — xem log chi tiết nếu cần`)
      }
      setImportOpen(false)
    } catch (importError) {
      toast.error(getErrorMessage(importError))
    }
  }

  return (
    <section>
      <PageHeader
        title="Template theo danh mục"
        description="Mỗi danh mục = prompt text + ảnh. Thêm tay hoặc import JSON/CSV (có thể nhờ Claude sinh file)."
        actions={(
          <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap' }}>
            <button type="button" className="btn btn-secondary" onClick={() => setImportOpen(true)}>
              Import hàng loạt
            </button>
            <button type="button" className="btn btn-primary" onClick={openCreate}>
              Thêm danh mục
            </button>
          </div>
        )}
      />

      <div className="card card-body" style={{ marginBottom: 16 }}>
        <div className="form-group" style={{ marginBottom: 0, maxWidth: 360 }}>
          <label htmlFor="tpl-keyword">Tìm danh mục</label>
          <input
            id="tpl-keyword"
            value={keyword}
            onChange={(event) => setKeyword(event.target.value)}
            placeholder="Tên danh mục..."
          />
        </div>
      </div>

      <div className="card">
        {isLoading && <LoadingState />}
        {isError && <ErrorState message={getErrorMessage(error)} onRetry={refetch} />}
        {!isLoading && !isError && items.length === 0 && (
          <EmptyState
            message="Chưa có danh mục template."
            action={(
              <div style={{ display: 'flex', gap: 8, justifyContent: 'center', flexWrap: 'wrap' }}>
                <button type="button" className="btn btn-secondary" onClick={() => setImportOpen(true)}>
                  Import hàng loạt
                </button>
                <button type="button" className="btn btn-primary" onClick={openCreate}>
                  Thêm danh mục
                </button>
              </div>
            )}
          />
        )}
        {!isLoading && !isError && items.length > 0 && (
          <table>
            <thead>
              <tr>
                <th>Danh mục</th>
                <th>Prompt text</th>
                <th>Prompt ảnh</th>
                <th>Mặc định</th>
                <th>Trạng thái</th>
                <th>Ngày tạo</th>
                <th />
              </tr>
            </thead>
            <tbody>
              {items.map((item) => (
                <tr key={item.id}>
                  <td>
                    <div style={{ fontWeight: 600 }}>{item.name}</div>
                    {item.description && (
                      <div style={{ fontSize: 12, color: 'var(--text-muted, #888)' }}>
                        {item.description}
                      </div>
                    )}
                  </td>
                  <td style={{ maxWidth: 220 }}>
                    <div style={{ fontSize: 12, color: 'var(--text-muted, #666)', overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>
                      {item.textBody || '—'}
                    </div>
                  </td>
                  <td style={{ maxWidth: 220 }}>
                    <div style={{ fontSize: 12, color: 'var(--text-muted, #666)', overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>
                      {item.imageBody || '—'}
                    </div>
                  </td>
                  <td>{item.isDefault ? '⭐' : '—'}</td>
                  <td>{item.isActive ? 'Đang dùng' : 'Tắt'}</td>
                  <td>{formatDateTime(item.createdAt)}</td>
                  <td>
                    <div style={{ display: 'flex', gap: 8, justifyContent: 'flex-end' }}>
                      <button type="button" className="btn btn-ghost" onClick={() => openEdit(item)}>
                        Chi tiết
                      </button>
                      <button type="button" className="btn btn-danger" onClick={() => handleDelete(item)}>
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

      <PromptTemplateFormModal
        open={modalOpen}
        onClose={() => setModalOpen(false)}
        initialData={editingItem}
        onSubmit={handleSubmit}
        isSubmitting={createMutation.isPending || updateMutation.isPending}
        errorMessage={formError}
      />

      <PromptTemplateImportModal
        open={importOpen}
        onClose={() => setImportOpen(false)}
        onImport={handleImport}
        isSubmitting={importMutation.isPending}
      />
    </section>
  )
}
