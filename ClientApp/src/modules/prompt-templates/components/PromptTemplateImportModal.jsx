import { useRef, useState } from 'react'
import Modal from '@/shared/components/Modal'
import { toast } from '@/shared/stores/toastStore'
import {
  CLAUDE_TEMPLATE_PROMPT,
  downloadSampleCsv,
  downloadSampleJson,
  parseTemplateUploadFile,
} from '../constants/templateImport'

export default function PromptTemplateImportModal({
  open,
  onClose,
  onImport,
  isSubmitting,
}) {
  const fileRef = useRef(null)
  const [updateExisting, setUpdateExisting] = useState(true)
  const [preview, setPreview] = useState([])
  const [parseError, setParseError] = useState('')
  const [fileLabel, setFileLabel] = useState('')

  const reset = () => {
    setPreview([])
    setParseError('')
    setFileLabel('')
    if (fileRef.current) fileRef.current.value = ''
  }

  const handleClose = () => {
    reset()
    onClose()
  }

  const handleFile = async (event) => {
    const file = event.target.files?.[0]
    if (!file) return
    setParseError('')
    setFileLabel(file.name)
    try {
      const text = await file.text()
      const items = parseTemplateUploadFile(file.name, text)
      setPreview(items)
      toast.success(`Đã đọc ${items.length} template từ file`)
    } catch (err) {
      setPreview([])
      setParseError(err.message || 'Không đọc được file')
    }
  }

  const copyClaudePrompt = async () => {
    try {
      await navigator.clipboard.writeText(CLAUDE_TEMPLATE_PROMPT)
      toast.success('Đã copy prompt Claude — dán vào chat Claude, lấy JSON rồi upload')
    } catch {
      toast.error('Không copy được clipboard')
    }
  }

  const handleSubmit = async () => {
    if (preview.length === 0) {
      toast.error('Chọn file JSON/CSV hợp lệ trước')
      return
    }
    await onImport({ items: preview, updateExisting })
    reset()
  }

  return (
    <Modal
      open={open}
      title="Import nhiều danh mục template"
      onClose={handleClose}
      footer={(
        <>
          <button type="button" className="btn btn-secondary" onClick={handleClose}>
            Đóng
          </button>
          <button
            type="button"
            className="btn btn-primary"
            disabled={isSubmitting || preview.length === 0}
            onClick={handleSubmit}
          >
            {isSubmitting ? 'Đang import...' : `Import ${preview.length || ''} template`}
          </button>
        </>
      )}
    >
      <div style={{ display: 'flex', flexDirection: 'column', gap: 16 }}>
        <div className="card card-body" style={{ margin: 0, background: 'var(--color-surface-muted)' }}>
          <strong style={{ display: 'block', marginBottom: 8 }}>Hướng dẫn nhanh</strong>
          <ol style={{ margin: 0, paddingLeft: 18, fontSize: 14, lineHeight: 1.5 }}>
            <li>
              <button type="button" className="btn btn-ghost" style={{ padding: '0 4px' }} onClick={copyClaudePrompt}>
                Copy prompt Claude
              </button>
              → dán vào Claude → yêu cầu xuất JSON array.
            </li>
            <li>
              Tải mẫu:{' '}
              <button type="button" className="btn btn-ghost" style={{ padding: '0 4px' }} onClick={downloadSampleJson}>
                sample.json
              </button>
              {' / '}
              <button type="button" className="btn btn-ghost" style={{ padding: '0 4px' }} onClick={downloadSampleCsv}>
                sample.csv
              </button>
            </li>
            <li>Upload file .json (khuyên dùng) hoặc .csv → kiểm tra preview → Import.</li>
          </ol>
        </div>

        <details>
          <summary style={{ cursor: 'pointer', fontWeight: 600 }}>Định dạng file bắt buộc</summary>
          <div style={{ fontSize: 13, marginTop: 8, lineHeight: 1.55 }}>
            <p style={{ marginTop: 0 }}>
              <strong>JSON</strong> — mảng object, mỗi object:
            </p>
            <pre style={{ overflow: 'auto', fontSize: 12, padding: 10, background: 'var(--color-surface-muted)', borderRadius: 6 }}>
{`[
  {
    "name": "Bán hàng",
    "description": "Đăng bán sản phẩm",
    "textBody": "Viết bài... {{title}} {{brand}}",
    "imageBody": "Create image... {{title}}",
    "isDefault": true,
    "isActive": true
  }
]`}
            </pre>
            <p>
              <strong>CSV</strong> — header đúng cột:
              {' '}
              <code>name,description,textBody,imageBody,isDefault,isActive</code>
              . Prompt có dấu phẩy/xuống dòng phải bọc trong dấu <code>&quot;...&quot;</code>.
            </p>
            <p style={{ marginBottom: 0 }}>
              Biến hợp lệ: <code>{'{{title}} {{category}} {{brand}} {{tone}} {{audience}} {{cta}} {{hashtags}} {{caption}} {{imagePrompt}}'}</code>
            </p>
          </div>
        </details>

        <div className="form-group" style={{ marginBottom: 0 }}>
          <label htmlFor="tpl-upload">Chọn file (.json / .csv)</label>
          <input
            id="tpl-upload"
            ref={fileRef}
            type="file"
            accept=".json,.csv,application/json,text/csv"
            onChange={handleFile}
          />
          {fileLabel && (
            <small className="form-hint">File: {fileLabel}</small>
          )}
        </div>

        <label style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
          <input
            type="checkbox"
            checked={updateExisting}
            onChange={(e) => setUpdateExisting(e.target.checked)}
          />
          Nếu trùng tên danh mục thì cập nhật (bỏ tick = bỏ qua trùng)
        </label>

        {parseError && <div className="alert alert-error">{parseError}</div>}

        {preview.length > 0 && (
          <div>
            <strong>Preview ({preview.length})</strong>
            <ul style={{ margin: '8px 0 0', paddingLeft: 18, maxHeight: 180, overflow: 'auto', fontSize: 13 }}>
              {preview.map((item) => (
                <li key={item.name}>
                  <strong>{item.name}</strong>
                  {item.isDefault ? ' ⭐' : ''}
                  {' — '}
                  text {item.textBody.length} ký tự, ảnh {item.imageBody.length} ký tự
                </li>
              ))}
            </ul>
          </div>
        )}
      </div>
    </Modal>
  )
}
