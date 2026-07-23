import { useMemo, useState } from 'react'
import PageHeader from '@/shared/components/PageHeader'
import LoadingState from '@/shared/components/LoadingState'
import ErrorState from '@/shared/components/ErrorState'
import EmptyState from '@/shared/components/EmptyState'
import Modal from '@/shared/components/Modal'
import { toast } from '@/shared/stores/toastStore'
import { formatDateTime, getErrorMessage } from '@/shared/utils/apiHelpers'
import CategoryFormModal from '../components/CategoryFormModal'
import {
  useCategoryList,
  useCreateCategory,
  useDeleteCategory,
  useImportCategories,
  useUpdateCategory,
} from '../hooks/useCategories'

export default function CategoryListPage() {
  const [keyword, setKeyword] = useState('')
  const [modalOpen, setModalOpen] = useState(false)
  const [editingItem, setEditingItem] = useState(null)
  const [formError, setFormError] = useState('')
  const [importOpen, setImportOpen] = useState(false)
  const [importText, setImportText] = useState('')

  const params = useMemo(
    () => ({ keyword, index: 1, size: 50 }),
    [keyword],
  )

  const { data, isLoading, isError, error, refetch } = useCategoryList(params)
  const createMutation = useCreateCategory()
  const updateMutation = useUpdateCategory()
  const deleteMutation = useDeleteCategory()
  const importMutation = useImportCategories()

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

  const handleImport = async () => {
    const names = importText
      .split('\n')
      .map((s) => s.trim())
      .filter(Boolean)
    if (names.length === 0) {
      toast.error('Nhập ít nhất 1 tên loại bài (mỗi dòng một tên)')
      return
    }
    try {
      const result = await importMutation.mutateAsync({ names })
      toast.success(`Đã thêm ${result?.created ?? 0} loại bài; bỏ qua ${result?.skipped ?? 0}`)
      setImportOpen(false)
      setImportText('')
    } catch (importError) {
      toast.error(getErrorMessage(importError))
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
          <div style={{ display: 'flex', gap: 8 }}>
            <button type="button" className="btn btn-secondary" onClick={() => setImportOpen(true)}>
              Import nhanh
            </button>
            <button type="button" className="btn btn-primary" onClick={openCreate}>
              Thêm danh mục
            </button>
          </div>
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

      <Modal
        open={importOpen}
        title="Import nhanh loại bài"
        onClose={() => setImportOpen(false)}
        footer={(
          <>
            <button type="button" className="btn btn-secondary" onClick={() => setImportOpen(false)}>
              Hủy
            </button>
            <button
              type="button"
              className="btn btn-primary"
              onClick={handleImport}
              disabled={importMutation.isPending}
            >
              {importMutation.isPending ? 'Đang import...' : 'Import'}
            </button>
          </>
        )}
      >
        <div className="form-group">
          <label htmlFor="category-import">Mỗi dòng một tên loại bài (slug tự sinh, trùng bỏ qua)</label>
          <textarea
            id="category-import"
            rows={8}
            value={importText}
            onChange={(event) => setImportText(event.target.value)}
            placeholder={'Tuyển sinh\nBán khóa học\nKhai giảng\nƯu đãi\nChứng chỉ'}
          />
        </div>
      </Modal>
    </section>
  )
}
