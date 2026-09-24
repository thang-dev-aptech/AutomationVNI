import { useEffect, useState } from 'react'
import Modal from '@/shared/components/Modal'
import { useWritableMediaFolderPages } from '../hooks/useMediaFolders'
import MediaFolderPickerTree from './MediaFolderPickerTree'

/**
 * Folder creation and rename modal with three explicit modes:
 * - 'page-roots': Create root folders for Pages that don't have one yet (checklist, no name field)
 * - 'create-child': Create a child in a specific folder (name only, parent fixed)
 * - 'rename': Rename an existing folder (name only)
 */
export default function MediaFolderFormModal({
  open,
  mode = 'rename', // 'page-roots' | 'create-child' | 'rename'
  editing = null, // For rename mode: the folder being edited
  defaultParentId = null, // For create-child mode: the fixed parent folder id
  defaultSocialChannelId = null, // For create-child mode: the Page of the parent
  onClose,
  onSubmit,
  isSubmitting = false,
  errorMessage = '',
}) {
  const [name, setName] = useState('')
  const [selectedPageIds, setSelectedPageIds] = useState(() => new Set())

  // For page-roots mode: Pages that don't have a root yet
  const { data: pagesWithoutRoot = [] } = useWritableMediaFolderPages({
    withoutRoot: mode === 'page-roots',
  })

  const displayChannels = mode === 'page-roots' ? pagesWithoutRoot : []

  useEffect(() => {
    if (!open) return
    setName(editing?.name ?? '')
    setSelectedPageIds(new Set())
  }, [open, editing, mode])

  const togglePage = (id) => {
    setSelectedPageIds((prev) => {
      const next = new Set(prev)
      if (next.has(id)) {
        next.delete(id)
      } else {
        next.add(id)
      }
      return next
    })
  }

  const toggleAllPages = () => {
    setSelectedPageIds((prev) =>
      prev.size === displayChannels.length ? new Set() : new Set(displayChannels.map((c) => c.id))
    )
  }

  const handleSubmit = (event) => {
    event.preventDefault()

    if (mode === 'page-roots') {
      // Create roots for selected Pages (names come from Page names)
      const pageIds = [...selectedPageIds]
      if (pageIds.length === 0) return

      const items = pageIds.map((id) => ({
        socialChannelId: id,
        name: displayChannels.find((c) => c.id === id)?.pageName ?? id,
      }))
      onSubmit({ items })
      return
    }

    if (mode === 'create-child') {
      // Create child in fixed parent with given name
      if (!name.trim()) return
      onSubmit({
        name: name.trim(),
        parentFolderId: defaultParentId || null,
        socialChannelId: defaultSocialChannelId || null,
      })
      return
    }

    if (mode === 'rename') {
      // Rename existing folder
      if (!name.trim()) return
      onSubmit({
        name: name.trim(),
        parentFolderId: editing?.parentFolderId ?? null,
        socialChannelId: editing?.socialChannelId ?? null,
      })
    }
  }

  const getTitle = () => {
    if (mode === 'page-roots') return 'Tạo thư mục gốc cho các Page'
    if (mode === 'create-child') return 'Tạo thư mục con'
    if (mode === 'rename') return 'Đổi tên thư mục'
    return 'Thư mục'
  }

  const isValid = () => {
    if (mode === 'page-roots') return selectedPageIds.size > 0
    if (mode === 'create-child' || mode === 'rename') return name.trim().length > 0
    return false
  }

  return (
    <Modal
      open={open}
      title={getTitle()}
      onClose={onClose}
      footer={
        <>
          <button type="button" className="btn btn-secondary" onClick={onClose}>
            Hủy
          </button>
          <button
            type="submit"
            form="media-folder-form"
            className="btn btn-primary"
            disabled={isSubmitting || !isValid()}
          >
            {isSubmitting ? 'Đang lưu...' : 'Lưu'}
          </button>
        </>
      }
    >
      <form id="media-folder-form" onSubmit={handleSubmit}>
        {errorMessage && <div className="alert alert-error">{errorMessage}</div>}

        {mode === 'page-roots' && (
          <div className="form-group">
            <div className="media-folder-page-multiselect-header">
              <label>
                Chọn Page để tạo thư mục gốc (mỗi Page được chọn sẽ có 1 thư mục, tên trùng với tên Page)
              </label>
              {displayChannels.length > 0 && (
                <button type="button" className="btn btn-ghost btn-sm" onClick={toggleAllPages}>
                  {selectedPageIds.size === displayChannels.length ? 'Bỏ chọn tất cả' : 'Chọn tất cả'}
                </button>
              )}
            </div>
            {displayChannels.length === 0 ? (
              <p className="form-hint">Tất cả các Page đã có thư mục gốc</p>
            ) : (
              <div className="media-folder-page-multiselect-list">
                {displayChannels.map((c) => (
                  <label key={c.id} className="media-folder-page-multiselect-item">
                    <input
                      type="checkbox"
                      checked={selectedPageIds.has(c.id)}
                      onChange={() => togglePage(c.id)}
                    />
                    {c.pageName}
                  </label>
                ))}
              </div>
            )}
          </div>
        )}

        {(mode === 'create-child' || mode === 'rename') && (
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
        )}
      </form>
    </Modal>
  )
}
