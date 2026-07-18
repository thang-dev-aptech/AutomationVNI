import { useEffect, useState } from 'react'
import Modal from '@/shared/components/Modal'
import { useSocialChannelAll } from '@/modules/social-channels/hooks/useSocialChannels'
import { usePromptTemplateList } from '@/modules/prompt-templates/hooks/usePromptTemplates'
import { TEMPLATE_TYPE } from '@/modules/prompt-templates/constants/promptTemplateType'

const EMPTY_GUID = '00000000-0000-0000-0000-000000000000'

const emptyForm = {
  socialChannelId: '',
  brandName: '',
  toneOfVoice: '',
  ctaText: '',
  ctaUrl: '',
  defaultHashtags: '',
  promptTemplateText: '',
  promptTemplateImage: '',
  defaultTextTemplateId: '',
  defaultImageTemplateId: '',
}

export default function PageContextFormModal({
  open,
  onClose,
  initialData,
  onSubmit,
  isSubmitting,
  errorMessage,
}) {
  const [form, setForm] = useState(emptyForm)
  const isEdit = Boolean(initialData?.id)
  const { data: channels = [] } = useSocialChannelAll()
  const { data: textTplData } = usePromptTemplateList({
    templateType: TEMPLATE_TYPE.TEXT, isActive: true, index: 1, size: 100,
  })
  const { data: imageTplData } = usePromptTemplateList({
    templateType: TEMPLATE_TYPE.IMAGE, isActive: true, index: 1, size: 100,
  })
  const textTemplates = textTplData?.items ?? []
  const imageTemplates = imageTplData?.items ?? []

  useEffect(() => {
    if (!open) return
    setForm(
      initialData
        ? {
            socialChannelId: initialData.socialChannelId || '',
            brandName: initialData.brandName || '',
            toneOfVoice: initialData.toneOfVoice || '',
            ctaText: initialData.ctaText || '',
            ctaUrl: initialData.ctaUrl || '',
            defaultHashtags: initialData.defaultHashtags || '',
            promptTemplateText: initialData.promptTemplateText || '',
            promptTemplateImage: initialData.promptTemplateImage || '',
            defaultTextTemplateId: initialData.defaultTextTemplateId || '',
            defaultImageTemplateId: initialData.defaultImageTemplateId || '',
          }
        : emptyForm,
    )
  }, [open, initialData])

  const handleChange = (field) => (event) => {
    setForm((prev) => ({ ...prev, [field]: event.target.value }))
  }

  const handleSubmit = (event) => {
    event.preventDefault()
    onSubmit({
      socialChannelId: form.socialChannelId,
      brandName: form.brandName.trim(),
      toneOfVoice: form.toneOfVoice.trim() || null,
      ctaText: form.ctaText.trim() || null,
      ctaUrl: form.ctaUrl.trim() || null,
      defaultHashtags: form.defaultHashtags.trim() || null,
      promptTemplateText: form.promptTemplateText.trim() || null,
      promptTemplateImage: form.promptTemplateImage.trim() || null,
      // Chọn template → gửi id; bỏ trống → khi sửa gửi EMPTY_GUID để xoá, khi tạo mới gửi null.
      defaultTextTemplateId: form.defaultTextTemplateId || (isEdit ? EMPTY_GUID : null),
      defaultImageTemplateId: form.defaultImageTemplateId || (isEdit ? EMPTY_GUID : null),
    })
  }

  return (
    <Modal
      open={open}
      title={isEdit ? 'Cập nhật Page Context' : 'Thêm Page Context'}
      onClose={onClose}
      footer={(
        <>
          <button type="button" className="btn btn-secondary" onClick={onClose}>
            Hủy
          </button>
          <button
            type="submit"
            form="page-context-form"
            className="btn btn-primary"
            disabled={isSubmitting}
          >
            {isSubmitting ? 'Đang lưu...' : 'Lưu'}
          </button>
        </>
      )}
    >
      <form id="page-context-form" onSubmit={handleSubmit}>
        {errorMessage && <div className="alert alert-error">{errorMessage}</div>}
        <div className="form-group">
          <label htmlFor="context-channel">Kênh MXH</label>
          <select
            id="context-channel"
            value={form.socialChannelId}
            onChange={handleChange('socialChannelId')}
            required
            disabled={isEdit}
          >
            <option value="">Chọn kênh</option>
            {channels.map((channel) => (
              <option key={channel.id} value={channel.id}>
                {channel.pageName}
              </option>
            ))}
          </select>
        </div>
        <div className="form-group">
          <label htmlFor="context-brand">Tên thương hiệu</label>
          <input
            id="context-brand"
            value={form.brandName}
            onChange={handleChange('brandName')}
            required
          />
        </div>
        <div className="form-group">
          <label htmlFor="context-tone">Giọng văn (Tone of Voice)</label>
          <textarea
            id="context-tone"
            value={form.toneOfVoice}
            onChange={handleChange('toneOfVoice')}
          />
        </div>
        <div className="form-group">
          <label htmlFor="context-cta-text">CTA Text</label>
          <input
            id="context-cta-text"
            value={form.ctaText}
            onChange={handleChange('ctaText')}
          />
        </div>
        <div className="form-group">
          <label htmlFor="context-cta-url">CTA URL</label>
          <input
            id="context-cta-url"
            value={form.ctaUrl}
            onChange={handleChange('ctaUrl')}
          />
        </div>
        <div className="form-group">
          <label htmlFor="context-hashtags">Hashtags mặc định (JSON array)</label>
          <input
            id="context-hashtags"
            value={form.defaultHashtags}
            onChange={handleChange('defaultHashtags')}
          />
        </div>
        <div className="form-group">
          <label htmlFor="context-default-text-tpl">Template mặc định — Nội dung</label>
          <select
            id="context-default-text-tpl"
            value={form.defaultTextTemplateId}
            onChange={handleChange('defaultTextTemplateId')}
          >
            <option value="">Không dùng (theo default hệ thống)</option>
            {textTemplates.map((tpl) => (
              <option key={tpl.id} value={tpl.id}>
                {tpl.name}{tpl.isDefault ? ' ⭐' : ''}
              </option>
            ))}
          </select>
        </div>
        <div className="form-group">
          <label htmlFor="context-default-image-tpl">Template mặc định — Ảnh</label>
          <select
            id="context-default-image-tpl"
            value={form.defaultImageTemplateId}
            onChange={handleChange('defaultImageTemplateId')}
          >
            <option value="">Không dùng (theo default hệ thống)</option>
            {imageTemplates.map((tpl) => (
              <option key={tpl.id} value={tpl.id}>
                {tpl.name}{tpl.isDefault ? ' ⭐' : ''}
              </option>
            ))}
          </select>
        </div>
        <details>
          <summary style={{ cursor: 'pointer', fontSize: 13, color: 'var(--text-muted, #888)', marginBottom: 8 }}>
            Prompt inline (nâng cao — ưu tiên thấp hơn template ở trên)
          </summary>
          <div className="form-group">
            <label htmlFor="context-prompt-text">Prompt inline — Text</label>
            <textarea
              id="context-prompt-text"
              value={form.promptTemplateText}
              onChange={handleChange('promptTemplateText')}
            />
          </div>
          <div className="form-group">
            <label htmlFor="context-prompt-image">Prompt inline — Image</label>
            <textarea
              id="context-prompt-image"
              value={form.promptTemplateImage}
              onChange={handleChange('promptTemplateImage')}
            />
          </div>
        </details>
      </form>
    </Modal>
  )
}
