import { useMemo, useState } from 'react'
import PageHeader from '@/shared/components/PageHeader'
import LoadingState from '@/shared/components/LoadingState'
import ErrorState from '@/shared/components/ErrorState'
import EmptyState from '@/shared/components/EmptyState'
import { formatDateTime, getErrorMessage } from '@/shared/utils/apiHelpers'
import PromptTemplateFormModal from '../components/PromptTemplateFormModal'
import {
  TEMPLATE_TYPE_LABELS,
  TEMPLATE_TYPE_OPTIONS,
} from '../constants/promptTemplateType'
import {
  useCreatePromptTemplate,
  useDeletePromptTemplate,
  usePromptTemplateList,
  useUpdatePromptTemplate,
} from '../hooks/usePromptTemplates'

export default function PromptTemplateListPage() {
  const [keyword, setKeyword] = useState('')
  const [typeFilter, setTypeFilter] = useState('')
  const [modalOpen, setModalOpen] = useState(false)
  const [editingItem, setEditingItem] = useState(null)
  const [formError, setFormError] = useState('')

  const params = useMemo(
    () => ({
      keyword,
      templateType: typeFilter ? Number(typeFilter) : undefined,
      index: 1,
      size: 100,
    }),
    [keyword, typeFilter],
  )

  const { data, isLoading, isError, error, refetch } = usePromptTemplateList(params)
  const createMutation = useCreatePromptTemplate()
  const updateMutation = useUpdatePromptTemplate()
  const deleteMutation = useDeletePromptTemplate()

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
    } catch (submitError) {
      setFormError(getErrorMessage(submitError))
    }
  }

  const handleDelete = async (item) => {
    if (!window.confirm(`Xóa template "${item.name}"?`)) return
    try {
      await deleteMutation.mutateAsync(item.id)
    } catch (deleteError) {
      window.alert(getErrorMessage(deleteError))
    }
  }

  return (
    <section>
      <PageHeader
        title="Prompt Templates"
        description="Thư viện prompt sinh nội dung & ảnh, dùng biến {{title}}, {{brand}}..."
        actions={(
          <button type="button" className="btn btn-primary" onClick={openCreate}>
            Thêm template
          </button>
        )}
      />

      <div className="card card-body" style={{ marginBottom: 16 }}>
        <div style={{ display: 'flex', gap: 16, flexWrap: 'wrap' }}>
          <div className="form-group" style={{ marginBottom: 0, flex: 1, minWidth: 200 }}>
            <label htmlFor="tpl-keyword">Tìm kiếm</label>
            <input
              id="tpl-keyword"
              value={keyword}
              onChange={(event) => setKeyword(event.target.value)}
              placeholder="Tên hoặc mô tả..."
            />
          </div>
          <div className="form-group" style={{ marginBottom: 0, minWidth: 200 }}>
            <label htmlFor="tpl-type-filter">Loại</label>
            <select
              id="tpl-type-filter"
              value={typeFilter}
              onChange={(event) => setTypeFilter(event.target.value)}
            >
              <option value="">Tất cả</option>
              {TEMPLATE_TYPE_OPTIONS.map((opt) => (
                <option key={opt.value} value={opt.value}>{opt.label}</option>
              ))}
            </select>
          </div>
        </div>
      </div>

      <div className="card">
        {isLoading && <LoadingState />}
        {isError && <ErrorState message={getErrorMessage(error)} onRetry={refetch} />}
        {!isLoading && !isError && items.length === 0 && (
          <EmptyState message="Chưa có template nào. Bấm + Thêm template." />
        )}
        {!isLoading && !isError && items.length > 0 && (
          <table>
            <thead>
              <tr>
                <th>Tên</th>
                <th>Loại</th>
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
                  <td>{TEMPLATE_TYPE_LABELS[item.templateType] ?? item.templateType}</td>
                  <td>{item.isDefault ? '⭐ Mặc định' : '—'}</td>
                  <td>{item.isActive ? 'Đang dùng' : 'Tắt'}</td>
                  <td>{formatDateTime(item.createdAt)}</td>
                  <td>
                    <div style={{ display: 'flex', gap: 8, justifyContent: 'flex-end' }}>
                      <button type="button" className="btn btn-ghost" onClick={() => openEdit(item)}>
                        Sửa
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
    </section>
  )
}
