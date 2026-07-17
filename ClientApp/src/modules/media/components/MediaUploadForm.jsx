import { useState } from 'react'
import Modal from '@/shared/components/Modal'
import {
  fileNameFromUrl,
  guessMimeFromUrl,
  IMAGE_MIME_PREFIX,
  MAX_UPLOAD_SIZE_BYTES,
  MAX_UPLOAD_SIZE_MB,
} from '../constants/mediaConstants'

const emptyUrlForm = {
  publicUrl: '',
  originalFileName: '',
  altText: '',
  description: '',
}

export default function MediaUploadForm({ open, onClose, onSubmit, isSubmitting, errorMessage }) {
  const [form, setForm] = useState(emptyUrlForm)

  const handleChange = (field) => (event) => {
    setForm((prev) => ({ ...prev, [field]: event.target.value }))
  }

  const handleSubmit = (event) => {
    event.preventDefault()
    const publicUrl = form.publicUrl.trim()
    const fileName = form.originalFileName.trim() || fileNameFromUrl(publicUrl)
    const mimeType = guessMimeFromUrl(publicUrl)

    onSubmit({
      fileName,
      originalFileName: form.originalFileName.trim() || fileName,
      storagePath: publicUrl,
      publicUrl,
      mimeType,
      fileSize: 0,
      source: 1,
      altText: form.altText.trim() || null,
      description: form.description.trim() || null,
    })
  }

  const resetAndClose = () => {
    setForm(emptyUrlForm)
    onClose()
  }

  return (
    <Modal
      open={open}
      title="Thêm media bằng URL"
      onClose={resetAndClose}
      footer={(
        <>
          <button type="button" className="btn btn-secondary" onClick={resetAndClose}>
            Hủy
          </button>
          <button
            type="submit"
            form="media-url-form"
            className="btn btn-primary"
            disabled={isSubmitting}
          >
            {isSubmitting ? 'Đang lưu...' : 'Thêm media'}
          </button>
        </>
      )}
    >
      <p style={{ margin: '0 0 16px', fontSize: '0.9rem', color: 'var(--color-text-muted)' }}>
        Backend chưa có upload endpoint — nhập URL ảnh công khai để tạo MediaAsset.
      </p>
      <form id="media-url-form" onSubmit={handleSubmit}>
        {errorMessage && <div className="alert alert-error">{errorMessage}</div>}
        <div className="form-group">
          <label htmlFor="media-url">URL ảnh *</label>
          <input
            id="media-url"
            type="url"
            value={form.publicUrl}
            onChange={handleChange('publicUrl')}
            required
            placeholder="https://example.com/image.jpg"
          />
        </div>
        <div className="form-group">
          <label htmlFor="media-filename">Tên hiển thị (tuỳ chọn)</label>
          <input
            id="media-filename"
            value={form.originalFileName}
            onChange={handleChange('originalFileName')}
            placeholder="Tên file gốc"
          />
        </div>
        <div className="form-group">
          <label htmlFor="media-alt">Alt text (tuỳ chọn)</label>
          <input
            id="media-alt"
            value={form.altText}
            onChange={handleChange('altText')}
          />
        </div>
        <div className="form-group">
          <label htmlFor="media-desc">Mô tả (tuỳ chọn)</label>
          <textarea
            id="media-desc"
            value={form.description}
            onChange={handleChange('description')}
            rows={3}
          />
        </div>
      </form>
      <UploadHint />
    </Modal>
  )
}

function UploadHint() {
  return (
    <p style={{ margin: '16px 0 0', fontSize: '0.8rem', color: 'var(--color-text-muted)' }}>
      Khi backend có upload endpoint, form sẽ dùng FormData với validate{' '}
      {IMAGE_MIME_PREFIX}* và tối đa {MAX_UPLOAD_SIZE_MB}MB ({MAX_UPLOAD_SIZE_BYTES} bytes).
    </p>
  )
}
