import { useMemo, useState } from 'react'
import PageHeader from '@/shared/components/PageHeader'
import LoadingState from '@/shared/components/LoadingState'
import ErrorState from '@/shared/components/ErrorState'
import EmptyState from '@/shared/components/EmptyState'
import { formatDateTime, getErrorMessage } from '@/shared/utils/apiHelpers'
import CategoryFormModal from '../components/CategoryFormModal'
import {
  useCategoryList,
  useCreateCategory,
  useDeleteCategory,
  useUpdateCategory,
} from '../hooks/useCategories'

export default function CategoryListPage() {
  const [keyword, setKeyword] = useState('')
  const [modalOpen, setModalOpen] = useState(false)
  const [editingItem, setEditingItem] = useState(null)
  const [formError, setFormError] = useState('')

  const params = useMemo(
    () => ({ keyword, index: 1, size: 50 }),
    [keyword],
  )

  const { data, isLoading, isError, error, refetch } = useCategoryList(params)
  const createMutation = useCreateCategory()
  const updateMutation = useUpdateCategory()
  const deleteMutation = useDeleteCategory()

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
    if (!window.confirm(`Xóa danh mục "${item.name}"?`)) return
    try {
      await deleteMutation.mutateAsync(item.id)
    } catch (deleteError) {
      window.alert(getErrorMessage(deleteError))
    }
  }

  return (
    <section>
      <PageHeader
        title="Danh mục"
        description="Phân loại bài viết và media"
        actions={(
          <button type="button" className="btn btn-primary" onClick={openCreate}>
            Thêm danh mục
          </button>
        )}
      />

      <div className="card card-body" style={{ marginBottom: 16 }}>
        <div className="form-group" style={{ marginBottom: 0 }}>
          <label htmlFor="category-keyword">Tìm kiếm</label>
          <input
            id="category-keyword"
            value={keyword}
            onChange={(event) => setKeyword(event.target.value)}
            placeholder="Tên hoặc slug..."
          />
        </div>
      </div>

      <div className="card">
        {isLoading && <LoadingState />}
        {isError && (
          <ErrorState message={getErrorMessage(error)} onRetry={refetch} />
        )}
        {!isLoading && !isError && items.length === 0 && (
          <EmptyState message="Chưa có danh mục nào" />
        )}
        {!isLoading && !isError && items.length > 0 && (
          <table>
            <thead>
              <tr>
                <th>Tên</th>
                <th>Slug</th>
                <th>Ngày tạo</th>
                <th />
              </tr>
            </thead>
            <tbody>
              {items.map((item) => (
                <tr key={item.id}>
                  <td>{item.name}</td>
                  <td>{item.slug}</td>
                  <td>{formatDateTime(item.createdAt)}</td>
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

      <CategoryFormModal
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
