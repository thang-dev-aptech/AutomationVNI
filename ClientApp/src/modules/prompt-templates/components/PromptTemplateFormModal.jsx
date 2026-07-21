import { useEffect, useRef, useState } from 'react'
import Modal from '@/shared/components/Modal'
import { TEMPLATE_VARIABLES } from '../constants/promptTemplateType'

const emptyForm = {
  name: '',
  description: '',
  textBody: '',
  imageBody: '',
  isDefault: false,
  isActive: true,
}

function VariableChips({ onInsert }) {
  return (
    <div style={{ display: 'flex', flexWrap: 'wrap', gap: 6, marginTop: 6 }}>
      {TEMPLATE_VARIABLES.map((v) => (
        <button
          key={v.name}
          type="button"
          className="btn btn-ghost"
          style={{ fontSize: 12, padding: '2px 8px' }}
          title={v.desc}
          onClick={() => onInsert(v.name)}
        >
          {`{{${v.name}}}`}
        </button>
      ))}
    </div>
  )
}

export default function PromptTemplateFormModal({
  open,
  onClose,
  initialData,
  mode = 'create',
  onSubmit,
  isSubmitting,
  errorMessage,
}) {
  const [form, setForm] = useState(emptyForm)
  const textRef = useRef(null)
  const imageRef = useRef(null)
  const modalTitle =
    mode === 'edit'
      ? 'Cập nhật danh mục template'
      : mode === 'copy'
        ? 'Sao chép danh mục template'
        : 'Thêm danh mục template'

  useEffect(() => {
    if (!open) return
    setForm(
      initialData
        ? {
            name: initialData.name || '',
            description: initialData.description || '',
            textBody: initialData.textBody || '',
            imageBody: initialData.imageBody || '',
            isDefault: Boolean(initialData.isDefault),
            isActive: initialData.isActive ?? true,
          }
        : emptyForm,
    )
  }, [open, initialData])

  const handleChange = (field) => (event) => {
    const value =
      event.target.type === 'checkbox' ? event.target.checked : event.target.value
    setForm((prev) => ({ ...prev, [field]: value }))
  }

  const insertVariable = (field, ref) => (name) => {
    const token = `{{${name}}}`
    const el = ref.current
    if (!el) {
      setForm((prev) => ({ ...prev, [field]: `${prev[field]}${token}` }))
      return
    }
    const start = el.selectionStart ?? el.value.length
    const end = el.selectionEnd ?? el.value.length
    setForm((prev) => {
      const current = prev[field] || ''
      const next = current.slice(0, start) + token + current.slice(end)
      return { ...prev, [field]: next }
    })
    requestAnimationFrame(() => {
      el.focus()
      const pos = start + token.length
      el.setSelectionRange(pos, pos)
    })
  }

  const handleSubmit = (event) => {
    event.preventDefault()
    onSubmit({
      name: form.name.trim(),
      description: form.description.trim() || null,
      textBody: form.textBody.trim(),
      imageBody: form.imageBody.trim(),
      isDefault: form.isDefault,
      isActive: form.isActive,
    })
  }

  return (
    <Modal
      open={open}
      title={modalTitle}
      onClose={onClose}
      footer={(
        <>
          <button type="button" className="btn btn-secondary" onClick={onClose}>
            Hủy
          </button>
          <button
            type="submit"
            form="prompt-template-form"
            className="btn btn-primary"
            disabled={isSubmitting}
          >
            {isSubmitting ? 'Đang lưu...' : mode === 'copy' ? 'Tạo bản sao' : 'Lưu'}
          </button>
        </>
      )}
    >
      <form id="prompt-template-form" onSubmit={handleSubmit}>
        {errorMessage && <div className="alert alert-error">{errorMessage}</div>}

        <div className="form-group">
          <label htmlFor="tpl-name">Danh mục *</label>
          <input
            id="tpl-name"
            value={form.name}
            onChange={handleChange('name')}
            required
            placeholder="VD: Bán hàng, Tuyển dụng, Branding..."
          />
        </div>

        <div className="form-group">
          <label htmlFor="tpl-desc">Mô tả</label>
          <input id="tpl-desc" value={form.description} onChange={handleChange('description')} />
        </div>

        <div className="form-group">
          <label htmlFor="tpl-text">Prompt text *</label>
          <textarea
            id="tpl-text"
            ref={textRef}
            value={form.textBody}
            onChange={handleChange('textBody')}
            rows={6}
            placeholder="Viết bài về {{title}} thuộc danh mục {{category}}, giọng {{tone}}..."
            required
          />
          <small className="form-hint">Chèn biến:</small>
          <VariableChips onInsert={insertVariable('textBody', textRef)} />
        </div>

        <div className="form-group">
          <label htmlFor="tpl-image">Prompt ảnh *</label>
          <textarea
            id="tpl-image"
            ref={imageRef}
            value={form.imageBody}
            onChange={handleChange('imageBody')}
            rows={5}
            placeholder="Tạo ảnh minh họa cho {{title}}, phong cách {{brand}}..."
            required
          />
          <small className="form-hint">Chèn biến:</small>
          <VariableChips onInsert={insertVariable('imageBody', imageRef)} />
        </div>

        <div className="form-group" style={{ display: 'flex', gap: 20 }}>
          <label style={{ display: 'flex', alignItems: 'center', gap: 6 }}>
            <input type="checkbox" checked={form.isDefault} onChange={handleChange('isDefault')} />
            Mặc định (khi bài không chọn danh mục)
          </label>
          <label style={{ display: 'flex', alignItems: 'center', gap: 6 }}>
            <input type="checkbox" checked={form.isActive} onChange={handleChange('isActive')} />
            Đang dùng
          </label>
        </div>
      </form>
    </Modal>
  )
}
