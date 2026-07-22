import { useEffect, useState } from 'react'
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

export default function MediaUploadForm({
  open,
  onClose,
  onSubmit,
  isSubmitting,
  errorMessage,
  folders = [],
  categories = [],
  defaultFolderId = null,
}) {
  const [mode, setMode] = useState('file')
  const [form, setForm] = useState(emptyUrlForm)
  const [files, setFiles] = useState([])
  const [fileError, setFileError] = useState('')
  const [folderId, setFolderId] = useState('')
  const [categoryIds, setCategoryIds] = useState([])

  useEffect(() => {
    if (open) {
      setFolderId(defaultFolderId ?? '')
      setFiles([])
      setCategoryIds([])
      setFileError('')
    }
  }, [open, defaultFolderId])

  const handleChange = (field) => (event) => {
    setForm((prev) => ({ ...prev, [field]: event.target.value }))
  }

  const handleFileChange = (event) => {
    const selected = Array.from(event.target.files ?? [])
    setFileError('')
    const tooBig = selected.filter((f) => f.size > MAX_UPLOAD_SIZE_BYTES)
    const ok = selected.filter((f) => f.size <= MAX_UPLOAD_SIZE_BYTES)
    if (tooBig.length > 0) {
      setFileError(`${tooBig.length} file vượt quá ${MAX_UPLOAD_SIZE_MB}MB, đã bỏ qua`)
    }
    setFiles(ok)
  }

  const toggleCategory = (id) => {
    setCategoryIds((prev) =>
      prev.includes(id) ? prev.filter((x) => x !== id) : [...prev, id],
    )
  }

  const handleFileSubmit = (event) => {
    event.preventDefault()
    if (files.length === 0) return
    const formData = new FormData()
    files.forEach((f) => formData.append('files', f))
    if (folderId) formData.append('folderId', folderId)
    categoryIds.forEach((id) => formData.append('categoryIds', id))
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
      folderId: folderId || null,
      altText: form.altText.trim() || null,
      description: form.description.trim() || null,
    })
  }

  const resetAndClose = () => {
    setForm(emptyUrlForm)
    setFiles([])
    setCategoryIds([])
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
            disabled={isSubmitting || (isFileMode && files.length === 0)}
          >
            {isSubmitting
              ? 'Đang lưu...'
              : isFileMode
                ? `Upload ${files.length || ''} ảnh — AI gắn nhãn`
                : 'Thêm media'}
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

      <div className="form-group">
        <label htmlFor="media-folder">Thư mục</label>
        <select
          id="media-folder"
          value={folderId}
          onChange={(event) => setFolderId(event.target.value)}
        >
          <option value="">— Chưa phân loại —</option>
          {folders.map((f) => (
            <option key={f.id} value={f.id}>{f.name}</option>
          ))}
        </select>
      </div>

      {isFileMode ? (
        <form id="media-file-form" onSubmit={handleFileSubmit}>
          <p style={{ margin: '0 0 16px', fontSize: '0.9rem', color: 'var(--color-text-muted)' }}>
            Chọn 1 hoặc nhiều ảnh (cả folder). Mỗi ảnh sẽ được AI phân tích và gắn 5-7 keyword để
            gợi ý khi tạo bài.
          </p>
          <div className="form-group">
            <label htmlFor="media-file">Ảnh * (jpg/png/webp, tối đa {MAX_UPLOAD_SIZE_MB}MB/ảnh)</label>
            <input
              id="media-file"
              type="file"
              accept="image/jpeg,image/png,image/webp"
              onChange={handleFileChange}
              multiple
              required
            />
            {files.length > 0 && (
              <p style={{ margin: '6px 0 0', fontSize: '0.85rem', color: 'var(--color-text-muted)' }}>
                Đã chọn {files.length} ảnh
              </p>
            )}
            {fileError && (
              <p style={{ margin: '6px 0 0', fontSize: '0.85rem', color: 'var(--color-danger, #dc2626)' }}>
                {fileError}
              </p>
            )}
          </div>
          {categories.length > 0 && (
            <div className="form-group">
              <label>Áp dụng cho loại bài (tuỳ chọn, chọn nhiều)</label>
              <div style={{ display: 'flex', flexWrap: 'wrap', gap: 8, marginTop: 4 }}>
                {categories.map((c) => {
                  const active = categoryIds.includes(c.id)
                  return (
                    <button
                      type="button"
                      key={c.id}
                      className={`btn btn-sm ${active ? 'btn-primary' : 'btn-secondary'}`}
                      onClick={() => toggleCategory(c.id)}
                    >
                      {active ? '✓ ' : ''}{c.name}
                    </button>
                  )
                })}
              </div>
              <p style={{ margin: '6px 0 0', fontSize: '0.8rem', color: 'var(--color-text-muted)' }}>
                Không chọn = ảnh dùng chung cho mọi loại bài.
              </p>
            </div>
          )}
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
