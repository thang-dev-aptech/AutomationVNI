import { useState } from 'react'
import Modal from '@/shared/components/Modal'
import MediaFolderPickerTree from './MediaFolderPickerTree'

/**
 * Move folder modal: re-parent a folder using MediaFolderPickerTree.
 * The server-side cycle guard (EnsurNoCycleAsync) prevents reparenting to descendants.
 */
export default function MoveMediaFolderModal({
  open,
  folder = null, // { id, name, socialChannelId, parentFolderId, ... }
  onClose,
  onSubmit,
  isSubmitting = false,
  errorMessage = '',
}) {
  const [parentFolderId, setParentFolderId] = useState('')

  const handleSubmit = (event) => {
    event.preventDefault()
    if (!folder) return

    onSubmit({
      folderId: folder.id,
      parentFolderId: parentFolderId || null,
    })
  }

  return (
    <Modal
      open={open}
      title={`Di chuyển thư mục: ${folder?.name ?? ''}`}
      onClose={onClose}
      footer={
        <>
          <button type="button" className="btn btn-secondary" onClick={onClose}>
            Hủy
          </button>
          <button
            type="submit"
            form="move-folder-form"
            className="btn btn-primary"
            disabled={isSubmitting}
          >
            {isSubmitting ? 'Đang lưu...' : 'Di chuyển'}
          </button>
        </>
      }
    >
      <form id="move-folder-form" onSubmit={handleSubmit}>
        {errorMessage && <div className="alert alert-error">{errorMessage}</div>}

        {folder && (
          <div className="form-group">
            <label htmlFor="move-folder-parent">Chọn thư mục đích</label>
            <MediaFolderPickerTree
              socialChannelId={folder.socialChannelId}
              value={parentFolderId || null}
              onChange={(id) => setParentFolderId(id || '')}
              excludeFolderId={folder.id}
            />
            <p className="form-hint">
              Để trống nếu muốn đưa về thư mục gốc (cấp cao nhất)
            </p>
          </div>
        )}
      </form>
    </Modal>
  )
}
