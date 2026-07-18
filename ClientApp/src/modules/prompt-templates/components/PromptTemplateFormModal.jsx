import { useEffect, useRef, useState } from 'react'
import Modal from '@/shared/components/Modal'
import {
  TEMPLATE_TYPE,
  TEMPLATE_TYPE_OPTIONS,
  TEMPLATE_VARIABLES,
} from '../constants/promptTemplateType'

const emptyForm = {
  name: '',
  description: '',
  templateType: TEMPLATE_TYPE.TEXT,
  body: '',
  isDefault: false,
  isActive: true,
}

export default function PromptTemplateFormModal({
  open,
  onClose,
  initialData,
  onSubmit,
  isSubmitting,
  errorMessage,
}) {
  const [form, setForm] = useState(emptyForm)
  const bodyRef = useRef(null)
  const isEdit = Boolean(initialData?.id)

  useEffect(() => {
    if (!open) return
    setForm(
      initialData
        ? {
            name: initialData.name || '',
            description: initialData.description || '',
            templateType: initialData.templateType || TEMPLATE_TYPE.TEXT,
            body: initialData.body || '',
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

  const insertVariable = (name) => {
    const token = `{{${name}}}`
    const el = bodyRef.current
    if (!el) {
      setForm((prev) => ({ ...prev, body: `${prev.body}${token}` }))
      return
    }
    const start = el.selectionStart ?? el.value.length
    const end = el.selectionEnd ?? el.value.length
    setForm((prev) => {
      const next = prev.body.slice(0, start) + token + prev.body.slice(end)
      return { ...prev, body: next }
    })
    // đặt lại con trỏ sau token vừa chèn
    requestAnimationFrame(() => {
      el.focus()
      const pos = start + token.length
      el.setSelectionRange(pos, pos)
    })
  }

  const handleSubmit = (event) => {
    event.preventDefault()
    const payload = {
      name: form.name.trim(),
      description: form.description.trim() || null,
      body: form.body.trim(),
      isDefault: form.isDefault,
      isActive: form.isActive,
    }
    // templateType chỉ gửi khi tạo mới (backend Update không cho đổi loại)
    if (!isEdit) payload.templateType = Number(form.templateType)
    onSubmit(payload)
  }

  return (
    <Modal
      open={open}
      title={isEdit ? 'Cập nhật template' : 'Thêm template'}
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
            {isSubmitting ? 'Đang lưu...' : 'Lưu'}
          </button>
        </>
      )}
    >
      <form id="prompt-template-form" onSubmit={handleSubmit}>
        {errorMessage && <div className="alert alert-error">{errorMessage}</div>}

        <div className="form-group">
          <label htmlFor="tpl-name">Tên template</label>
          <input id="tpl-name" value={form.name} onChange={handleChange('name')} required />
        </div>

        <div className="form-group">
          <label htmlFor="tpl-type">Loại</label>
          <select
            id="tpl-type"
            value={form.templateType}
            onChange={handleChange('templateType')}
            disabled={isEdit}
          >
            {TEMPLATE_TYPE_OPTIONS.map((opt) => (
              <option key={opt.value} value={opt.value}>{opt.label}</option>
            ))}
          </select>
          {isEdit && <small className="form-hint">Không đổi loại sau khi tạo.</small>}
        </div>

        <div className="form-group">
          <label htmlFor="tpl-desc">Mô tả</label>
          <input id="tpl-desc" value={form.description} onChange={handleChange('description')} />
        </div>

        <div className="form-group">
          <label htmlFor="tpl-body">Nội dung prompt</label>
          <textarea
            id="tpl-body"
            ref={bodyRef}
            value={form.body}
            onChange={handleChange('body')}
            rows={7}
            placeholder="Viết bài về {{title}}, giọng {{tone}}, thương hiệu {{brand}}..."
            required
          />
          <small className="form-hint">Bấm để chèn biến động:</small>
          <div style={{ display: 'flex', flexWrap: 'wrap', gap: 6, marginTop: 6 }}>
            {TEMPLATE_VARIABLES.map((v) => (
              <button
                key={v.name}
                type="button"
                className="btn btn-ghost"
                style={{ fontSize: 12, padding: '2px 8px' }}
                title={v.desc}
                onClick={() => insertVariable(v.name)}
              >
                {`{{${v.name}}}`}
              </button>
            ))}
          </div>
        </div>

        <div className="form-group" style={{ display: 'flex', gap: 20 }}>
          <label style={{ display: 'flex', alignItems: 'center', gap: 6 }}>
            <input type="checkbox" checked={form.isDefault} onChange={handleChange('isDefault')} />
            Mặc định cho loại này
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
