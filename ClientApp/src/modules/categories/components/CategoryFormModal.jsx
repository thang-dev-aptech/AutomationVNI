import { useEffect, useState } from 'react'
import Modal from '@/shared/components/Modal'
import { slugify } from '@/shared/utils/apiHelpers'

const emptyForm = {
  name: '',
  slug: '',
  description: '',
  parentCategoryId: '',
}

export default function CategoryFormModal({
  open,
  onClose,
  initialData,
  onSubmit,
  isSubmitting,
  errorMessage,
}) {
  const [form, setForm] = useState(emptyForm)
  const isEdit = Boolean(initialData?.id)

  useEffect(() => {
    if (!open) return
    setForm(
      initialData
        ? {
            name: initialData.name || '',
            slug: initialData.slug || '',
            description: initialData.description || '',
            parentCategoryId: initialData.parentCategoryId || '',
          }
        : emptyForm,
    )
  }, [open, initialData])

  const handleChange = (field) => (event) => {
    const value = event.target.value
    setForm((prev) => {
      const next = { ...prev, [field]: value }
      if (field === 'name' && !isEdit) {
        next.slug = slugify(value)
      }
      return next
    })
  }

  const handleSubmit = (event) => {
    event.preventDefault()
    onSubmit({
      name: form.name.trim(),
      slug: form.slug.trim(),
      description: form.description.trim() || null,
      parentCategoryId: form.parentCategoryId || null,
    })
  }

  return (
    <Modal
      open={open}
      title={isEdit ? 'Cập nhật danh mục' : 'Thêm danh mục'}
      onClose={onClose}
      footer={(
        <>
          <button type="button" className="btn btn-secondary" onClick={onClose}>
            Hủy
          </button>
          <button
            type="submit"
            form="category-form"
            className="btn btn-primary"
            disabled={isSubmitting}
          >
            {isSubmitting ? 'Đang lưu...' : 'Lưu'}
          </button>
        </>
      )}
    >
      <form id="category-form" onSubmit={handleSubmit}>
        {errorMessage && <div className="alert alert-error">{errorMessage}</div>}
        <div className="form-group">
          <label htmlFor="category-name">Tên danh mục</label>
          <input
            id="category-name"
            value={form.name}
            onChange={handleChange('name')}
            required
          />
        </div>
        <div className="form-group">
          <label htmlFor="category-slug">Slug</label>
          <input
            id="category-slug"
            value={form.slug}
            onChange={handleChange('slug')}
            required
          />
        </div>
        <div className="form-group">
          <label htmlFor="category-description">Mô tả</label>
          <textarea
            id="category-description"
            value={form.description}
            onChange={handleChange('description')}
          />
        </div>
      </form>
    </Modal>
  )
}
