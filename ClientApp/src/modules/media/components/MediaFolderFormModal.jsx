import { useEffect, useState } from 'react'
import Modal from '@/shared/components/Modal'

/**
 * Tạo mới / đổi tên thư mục media. Khi `editing` có giá trị → chế độ sửa.
 * `parentOptions` là danh sách folder phẳng để chọn thư mục cha.
 */
export default function MediaFolderFormModal({
  open,
  editing = null,
  defaultParentId = null,
  parentOptions = [],
  onClose,
  onSubmit,
  isSubmitting = false,
  errorMessage = '',
}) {
  const [name, setName] = useState('')
  const [parentFolderId, setParentFolderId] = useState('')

  useEffect(() => {
    if (!open) return
    setName(editing?.name ?? '')
    setParentFolderId(editing?.parentFolderId ?? defaultParentId ?? '')
  }, [open, editing, defaultParentId])

  const handleSubmit = (event) => {
    event.preventDefault()
    if (!name.trim()) return
    onSubmit({
      name: name.trim(),
      parentFolderId: parentFolderId || null,
    })
  }

  // Không cho chọn chính nó làm cha (khi đang sửa).
  const options = parentOptions.filter((f) => f.id !== editing?.id)

  return (
    <Modal
      open={open}
      title={editing ? 'Đổi tên / di chuyển thư mục' : 'Tạo thư mục'}
      onClose={onClose}
      footer={(
        <>
          <button type="button" className="btn btn-secondary" onClick={onClose}>Hủy</button>
          <button
            type="submit"
            form="media-folder-form"
            className="btn btn-primary"
            disabled={isSubmitting}
          >
            {isSubmitting ? 'Đang lưu...' : 'Lưu'}
          </button>
        </>
      )}
    >
      <form id="media-folder-form" onSubmit={handleSubmit}>
        {errorMessage && <div className="alert alert-error">{errorMessage}</div>}
        <div className="form-group">
          <label htmlFor="folder-name">Tên thư mục</label>
          <input
            id="folder-name"
            value={name}
            onChange={(event) => setName(event.target.value)}
            placeholder="VD: Logo, Banner, Sản phẩm..."
            autoFocus
          />
        </div>
        <div className="form-group">
          <label htmlFor="folder-parent">Thư mục cha</label>
          <select
            id="folder-parent"
            value={parentFolderId}
            onChange={(event) => setParentFolderId(event.target.value)}
          >
            <option value="">— Thư mục gốc —</option>
            {options.map((f) => (
              <option key={f.id} value={f.id}>{f.name}</option>
            ))}
          </select>
        </div>
      </form>
    </Modal>
  )
}
