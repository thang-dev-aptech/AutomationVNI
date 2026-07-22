import { useEffect, useState } from 'react'
import Modal from '@/shared/components/Modal'
import { useSocialChannelAll } from '@/modules/social-channels/hooks/useSocialChannels'
import { usePromptTemplateList } from '@/modules/prompt-templates/hooks/usePromptTemplates'
import { useMediaAssetAll } from '@/modules/media/hooks/useMediaAssets'
import { useMediaFolderTree } from '@/modules/media/hooks/useMediaFolders'
import { IMAGE_MIME_PREFIX } from '@/modules/media/constants/mediaConstants'

const EMPTY_GUID = '00000000-0000-0000-0000-000000000000'

const emptyForm = {
  socialChannelId: '',
  brandName: '',
  logoMediaId: '',
  toneOfVoice: '',
  ctaText: '',
  ctaUrl: '',
  hotline: '',
  website: '',
  brandColors: '',
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
  const { data: mediaAssets = [] } = useMediaAssetAll()
  const { data: mediaFolders = [] } = useMediaFolderTree()
  const [logoFolderId, setLogoFolderId] = useState('')
  const logoOptions = mediaAssets.filter((asset) =>
    asset.mimeType?.startsWith(IMAGE_MIME_PREFIX)
    && (!logoFolderId || asset.folderId === logoFolderId))
  const selectedLogo = mediaAssets.find((asset) => asset.id === form.logoMediaId)
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
            logoMediaId: initialData.logoMediaId || '',
            toneOfVoice: initialData.toneOfVoice || '',
            ctaText: initialData.ctaText || '',
            ctaUrl: initialData.ctaUrl || '',
            hotline: initialData.hotline || '',
            website: initialData.website || '',
            brandColors: initialData.brandColors || '',
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
      // Bỏ trống → khi sửa gửi EMPTY_GUID để gỡ logo, khi tạo mới gửi null.
      logoMediaId: form.logoMediaId || (isEdit ? EMPTY_GUID : null),
      toneOfVoice: form.toneOfVoice.trim() || null,
      ctaText: form.ctaText.trim() || null,
      ctaUrl: form.ctaUrl.trim() || null,
      hotline: form.hotline.trim() || null,
      website: form.website.trim() || null,
      brandColors: form.brandColors.trim() || null,
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
          <label htmlFor="context-logo">Logo thương hiệu</label>
          {mediaFolders.length > 0 && (
            <select
              id="context-logo-folder"
              value={logoFolderId}
              onChange={(event) => setLogoFolderId(event.target.value)}
              style={{ marginBottom: 8 }}
            >
              <option value="">📁 Tất cả thư mục</option>
              {mediaFolders.map((folder) => (
                <option key={folder.id} value={folder.id}>{folder.name}</option>
              ))}
            </select>
          )}
          <select
            id="context-logo"
            value={form.logoMediaId}
            onChange={handleChange('logoMediaId')}
          >
            <option value="">Chưa chọn — banner sẽ không có logo</option>
            {logoOptions.map((asset) => (
              <option key={asset.id} value={asset.id}>
                {asset.originalFileName || asset.fileName}
              </option>
            ))}
          </select>
          {selectedLogo?.publicUrl && (
            <img
              src={selectedLogo.publicUrl}
              alt={selectedLogo.altText || 'Logo đã chọn'}
              style={{ marginTop: 8, maxHeight: 56, maxWidth: '100%', objectFit: 'contain' }}
            />
          )}
          <small className="form-hint">
            Ảnh này được gửi kèm làm tham chiếu khi sinh banner — model vẽ đúng logo thật
            thay vì tự bịa. Upload logo tại mục Media trước nếu chưa có.
          </small>
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
          <label htmlFor="context-hotline">Hotline (in trên banner)</label>
          <input
            id="context-hotline"
            value={form.hotline}
            onChange={handleChange('hotline')}
            placeholder="0823 86 5858"
          />
        </div>
        <div className="form-group">
          <label htmlFor="context-website">Website (in trên banner)</label>
          <input
            id="context-website"
            value={form.website}
            onChange={handleChange('website')}
            placeholder="https://vni.edu.vn/"
          />
          <small className="form-hint">Bỏ trống sẽ dùng CTA URL ở trên.</small>
        </div>
        <div className="form-group">
          <label htmlFor="context-brand-colors">Màu thương hiệu</label>
          <input
            id="context-brand-colors"
            value={form.brandColors}
            onChange={handleChange('brandColors')}
            placeholder="#1565C0, #F59E0B, #22C55E"
          />
          <small className="form-hint">
            Mã màu đưa thẳng vào prompt sinh ảnh để banner đúng nhận diện.
          </small>
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
