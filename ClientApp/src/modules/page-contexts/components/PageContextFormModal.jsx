import { useEffect, useState } from 'react'
import Modal from '@/shared/components/Modal'
import { useSocialChannelAll } from '@/modules/social-channels/hooks/useSocialChannels'
import { usePromptTemplateList } from '@/modules/prompt-templates/hooks/usePromptTemplates'

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
  defaultTemplateId: '',
}

export default function PageContextFormModal({
  open,
  onClose,
  initialData,
  mode = 'create',
  unavailableChannelIds = [],
  onSubmit,
  isSubmitting,
  errorMessage,
}) {
  const [form, setForm] = useState(emptyForm)
  const isEdit = mode === 'edit'
  const { data: channels = [] } = useSocialChannelAll()
  const { data: tplData } = usePromptTemplateList({
    isActive: true, index: 1, size: 100,
  })
  const categoryTemplates = tplData?.items ?? []
  const selectableChannels = channels.filter(
    (channel) =>
      isEdit
      || channel.id === initialData?.socialChannelId
      || !unavailableChannelIds.includes(channel.id),
  )
  const modalTitle =
    mode === 'edit'
      ? 'Cập nhật Page Context'
      : mode === 'copy'
        ? 'Sao chép Page Context'
        : 'Thêm Page Context'

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
            // Danh mục là pack text + image; dữ liệu cũ có thể chỉ set 1 trong 2 id.
            defaultTemplateId:
              initialData.defaultTextTemplateId || initialData.defaultImageTemplateId || '',
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
      // 1 danh mục (pack text + image) → set cả 2 id.
      // Bỏ trống → khi sửa gửi EMPTY_GUID để xoá, khi tạo mới gửi null.
      defaultTextTemplateId: form.defaultTemplateId || (isEdit ? EMPTY_GUID : null),
      defaultImageTemplateId: form.defaultTemplateId || (isEdit ? EMPTY_GUID : null),
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
            form="page-context-form"
            className="btn btn-primary"
            disabled={isSubmitting}
          >
            {isSubmitting ? 'Đang lưu...' : mode === 'copy' ? 'Tạo bản sao' : 'Lưu'}
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
            {selectableChannels.map((channel) => (
              <option key={channel.id} value={channel.id}>
                {channel.pageName}
              </option>
            ))}
          </select>
          {!isEdit && selectableChannels.length === 0 && (
            <small className="form-hint">
              Tất cả page hiện đã có Page Context.
            </small>
          )}
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
          <label htmlFor="context-default-tpl">Danh mục mặc định (template text + ảnh)</label>
          <select
            id="context-default-tpl"
            value={form.defaultTemplateId}
            onChange={handleChange('defaultTemplateId')}
          >
            <option value="">Chưa chọn — khi tạo bài phải chọn danh mục thủ công</option>
            {categoryTemplates.map((tpl) => (
              <option key={tpl.id} value={tpl.id}>
                {tpl.name}{tpl.isDefault ? ' ⭐' : ''}
              </option>
            ))}
          </select>
          <p style={{ margin: '6px 0 0', fontSize: 12, color: 'var(--text-muted, #888)' }}>
            Đã chọn danh mục → page này &quot;sẵn sàng&quot;: tạo bài chỉ cần nhập chủ đề,
            không cần chọn danh mục nữa (vẫn có thể override khi tạo bài).
          </p>
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
