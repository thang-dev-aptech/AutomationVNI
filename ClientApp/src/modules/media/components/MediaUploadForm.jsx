import { useState } from 'react'
import Modal from '@/shared/components/Modal'
import {
  fileNameFromUrl,
  guessMimeFromUrl,
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
  const [mode, setMode] = useState('file')
  const [form, setForm] = useState(emptyUrlForm)
  const [file, setFile] = useState(null)
  const [fileAltText, setFileAltText] = useState('')
  const [fileError, setFileError] = useState('')

  const handleChange = (field) => (event) => {
    setForm((prev) => ({ ...prev, [field]: event.target.value }))
  }

  const handleFileChange = (event) => {
    const selected = event.target.files?.[0] ?? null
    setFileError('')
    if (selected && selected.size > MAX_UPLOAD_SIZE_BYTES) {
      setFileError(`File vượt quá ${MAX_UPLOAD_SIZE_MB}MB`)
      setFile(null)
      event.target.value = ''
      return
    }
    setFile(selected)
  }

  const handleFileSubmit = (event) => {
    event.preventDefault()
    if (!file) return
    const formData = new FormData()
    formData.append('file', file)
    if (fileAltText.trim()) formData.append('altText', fileAltText.trim())
    onSubmit(formData)
  }

  const handleUrlSubmit = (event) => {
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
    setFile(null)
    setFileAltText('')
    setFileError('')
    onClose()
  }

  const isFileMode = mode === 'file'

  return (
    <Modal
      open={open}
      title="Thêm media"
      onClose={resetAndClose}
      footer={(
        <>
          <button type="button" className="btn btn-secondary" onClick={resetAndClose}>
            Hủy
          </button>
          <button
            type="submit"
            form={isFileMode ? 'media-file-form' : 'media-url-form'}
            className="btn btn-primary"
            disabled={isSubmitting || (isFileMode && !file)}
          >
            {isSubmitting ? 'Đang lưu...' : isFileMode ? 'Upload — AI gắn nhãn' : 'Thêm media'}
          </button>
        </>
      )}
    >
      <div style={{ display: 'flex', gap: 8, marginBottom: 16 }}>
        <button
          type="button"
          className={`btn btn-sm ${isFileMode ? 'btn-primary' : 'btn-secondary'}`}
          onClick={() => setMode('file')}
        >
          Upload file
        </button>
        <button
          type="button"
          className={`btn btn-sm ${!isFileMode ? 'btn-primary' : 'btn-secondary'}`}
          onClick={() => setMode('url')}
        >
          Từ URL
        </button>
      </div>

      {errorMessage && <div className="alert alert-error">{errorMessage}</div>}

      {isFileMode ? (
        <form id="media-file-form" onSubmit={handleFileSubmit}>
          <p style={{ margin: '0 0 16px', fontSize: '0.9rem', color: 'var(--color-text-muted)' }}>
            Ảnh upload sẽ được AI tự phân tích và gắn 5-7 keyword để gợi ý khi tạo bài.
          </p>
          <div className="form-group">
            <label htmlFor="media-file">File ảnh * (jpg/png/webp, tối đa {MAX_UPLOAD_SIZE_MB}MB)</label>
            <input
              id="media-file"
              type="file"
              accept="image/jpeg,image/png,image/webp"
              onChange={handleFileChange}
              required
            />
            {fileError && (
              <p style={{ margin: '6px 0 0', fontSize: '0.85rem', color: 'var(--color-danger, #dc2626)' }}>
                {fileError}
              </p>
            )}
          </div>
          <div className="form-group">
            <label htmlFor="media-file-alt">Alt text (tuỳ chọn)</label>
            <input
              id="media-file-alt"
              value={fileAltText}
              onChange={(event) => setFileAltText(event.target.value)}
            />
          </div>
        </form>
      ) : (
        <form id="media-url-form" onSubmit={handleUrlSubmit}>
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
          <p style={{ margin: 0, fontSize: '0.8rem', color: 'var(--color-text-muted)' }}>
            Media từ URL không có file trên server nên AI không phân tích được — dùng Upload file
            nếu muốn gắn nhãn keyword.
          </p>
        </form>
      )}
    </Modal>
  )
}
