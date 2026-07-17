import { useEffect, useState } from 'react'
import Modal from '@/shared/components/Modal'
import { useSocialChannelAll } from '@/modules/social-channels/hooks/useSocialChannels'

const emptyForm = {
  socialChannelId: '',
  brandName: '',
  toneOfVoice: '',
  ctaText: '',
  ctaUrl: '',
  defaultHashtags: '',
  promptTemplateText: '',
  promptTemplateImage: '',
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
          <label htmlFor="context-prompt-text">Prompt template — Text</label>
          <textarea
            id="context-prompt-text"
            value={form.promptTemplateText}
            onChange={handleChange('promptTemplateText')}
          />
        </div>
        <div className="form-group">
          <label htmlFor="context-prompt-image">Prompt template — Image</label>
          <textarea
            id="context-prompt-image"
            value={form.promptTemplateImage}
            onChange={handleChange('promptTemplateImage')}
          />
        </div>
      </form>
    </Modal>
  )
}
