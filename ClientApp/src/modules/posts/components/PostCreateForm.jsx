import { useMemo, useState } from 'react'
import ChannelMultiSelect from '@/shared/components/ChannelMultiSelect'

const emptyForm = {
  idea: '',
  promptTemplateId: '',
}

/** PageContext đủ để bỏ chọn danh mục: có default template hoặc prompt text. */
export function isPageContextTemplateReady(ctx) {
  if (!ctx) return false
  return Boolean(
    ctx.defaultTextTemplateId
    || ctx.defaultImageTemplateId
    || (ctx.promptTemplateText && String(ctx.promptTemplateText).trim()),
  )
}

export default function PostCreateForm({
  channels = [],
  categoryTemplates = [],
  pageContexts = [],
  categories = [],
  isSubmitting,
  errorMessage,
  onSubmit,
}) {
  const [form, setForm] = useState(emptyForm)
  const [channelIds, setChannelIds] = useState([])
  const [showCategoryOverride, setShowCategoryOverride] = useState(false)
  // Nhánh 2: bật để AI tự tìm 2–3 ảnh kho phù hợp (GenerationFlow.RAG). Kèm "loại bài" để lọc kho.
  const [useMedia, setUseMedia] = useState(false)
  const [postTypeId, setPostTypeId] = useState('')

  const contextByChannel = useMemo(() => {
    const map = new Map()
    for (const ctx of pageContexts) {
      if (ctx?.socialChannelId) map.set(ctx.socialChannelId, ctx)
    }
    return map
  }, [pageContexts])

  const selectedMeta = useMemo(() => {
    let ready = 0
    let needCategory = 0
    for (const id of channelIds) {
      if (isPageContextTemplateReady(contextByChannel.get(id))) ready += 1
      else needCategory += 1
    }
    return { ready, needCategory }
  }, [channelIds, contextByChannel])

  const categoryRequired = selectedMeta.needCategory > 0

  const handleChange = (field) => (event) => {
    setForm((prev) => ({ ...prev, [field]: event.target.value }))
  }

  const handleSubmit = (event) => {
    event.preventDefault()
    if (channelIds.length === 0) return
    if (categoryRequired && !form.promptTemplateId) return

    onSubmit({
      title: form.idea.trim(),
      socialChannelIds: channelIds,
      socialChannelId: channelIds.length === 1 ? channelIds[0] : undefined,
      promptTemplateId: form.promptTemplateId || null,
      categoryId: useMedia ? (postTypeId || null) : null,
      generationFlow: useMedia ? 2 : 1,
    })
  }

  const getBadge = (channel) => {
    const ready = isPageContextTemplateReady(contextByChannel.get(channel.id))
    return ready
      ? { label: 'PC ✓', title: 'Đã có PageContext / danh mục mặc định', tone: 'ok' }
      : { label: 'Thiếu PC', title: 'Chưa setup PageContext — cần chọn danh mục', tone: 'warn' }
  }

  return (
    <form onSubmit={handleSubmit}>
      {errorMessage && <div className="alert alert-error">{errorMessage}</div>}

      <div className="form-group">
        <label htmlFor="post-idea">Ý tưởng bài viết *</label>
        <textarea
          id="post-idea"
          value={form.idea}
          onChange={handleChange('idea')}
          required
          rows={3}
          placeholder="Ví dụ: Khuyến mãi mùa hè, giảm 30% toàn bộ áo thun trong tuần này"
        />
      </div>

      <div className="form-group">
        <ChannelMultiSelect
          label="Kênh đăng *"
          placeholder="Chọn page"
          channels={channels}
          value={channelIds}
          onChange={setChannelIds}
          getBadge={getBadge}
          maxHeight={280}
        />
        {channels.length === 0 && (
          <p style={{ margin: '6px 0 0', fontSize: '0.85rem', color: 'var(--color-warning)' }}>
            Chưa có kênh nào — hãy kết nối kênh tại Platforms trước.
          </p>
        )}
        {channelIds.length > 0 && (
          <p style={{ margin: '8px 0 0', fontSize: '0.85rem', color: 'var(--text-muted, #888)' }}>
            {selectedMeta.ready} page đã setup · {selectedMeta.needCategory} page cần chọn danh mục
            {channelIds.length > 1 && ' — nhiều page sẽ sinh nền (batch)'}
          </p>
        )}
      </div>

      {(categoryRequired || showCategoryOverride || form.promptTemplateId) && (
        <div className="form-group">
          <label htmlFor="post-category">
            Danh mục {categoryRequired ? '*' : '(tuỳ chọn — ghi đè PageContext)'}
          </label>
          <select
            id="post-category"
            value={form.promptTemplateId}
            onChange={handleChange('promptTemplateId')}
            required={categoryRequired}
          >
            <option value="">{categoryRequired ? 'Chọn danh mục' : 'Dùng mặc định PageContext'}</option>
            {categoryTemplates.map((tpl) => (
              <option key={tpl.id} value={tpl.id}>
                {tpl.name}{tpl.isDefault ? ' ⭐' : ''}
              </option>
            ))}
          </select>
          {categoryRequired && (
            <p style={{ margin: '6px 0 0', fontSize: '0.85rem', color: 'var(--color-warning)' }}>
              Có page chưa setup PageContext — bắt buộc chọn danh mục.
            </p>
          )}
        </div>
      )}

      {!categoryRequired && channelIds.length > 0 && !showCategoryOverride && !form.promptTemplateId && (
        <p style={{ marginBottom: 12, fontSize: '0.9rem', color: 'var(--text-muted, #888)' }}>
          Tất cả page đã có PageContext — chỉ cần ý tưởng + kênh.{' '}
          <button
            type="button"
            className="btn btn-ghost"
            style={{ padding: '0 4px', fontSize: 'inherit' }}
            onClick={() => setShowCategoryOverride(true)}
          >
            Chọn danh mục để ghi đè
          </button>
        </p>
      )}

      <div
        className="form-group"
        style={{ border: '1px solid var(--color-border, #e5e7eb)', borderRadius: 8, padding: 12 }}
      >
        <label style={{ display: 'flex', alignItems: 'center', gap: 8, marginBottom: 0, cursor: 'pointer' }}>
          <input
            type="checkbox"
            checked={useMedia}
            onChange={(event) => setUseMedia(event.target.checked)}
          />
          <span>Dùng ảnh từ kho media (AI tự chọn 2–3 ảnh phù hợp nội dung)</span>
        </label>
        {useMedia && (
          <div style={{ marginTop: 10 }}>
            <label htmlFor="post-type">Loại bài viết — giúp AI lọc ảnh đúng chủ đề (tuỳ chọn)</label>
            <select
              id="post-type"
              value={postTypeId}
              onChange={(event) => setPostTypeId(event.target.value)}
            >
              <option value="">Tất cả loại (không lọc)</option>
              {categories.map((c) => (
                <option key={c.id} value={c.id}>{c.name}</option>
              ))}
            </select>
            <p style={{ margin: '6px 0 0', fontSize: '0.82rem', color: 'var(--text-muted, #888)' }}>
              Ngoài ảnh AI (cover), hệ thống tìm thêm 2–3 ảnh từ kho khớp nội dung
              {postTypeId ? ' và loại bài đã chọn' : ''}. Bạn vẫn có thể thêm/gỡ ảnh ở màn preview.
            </p>
          </div>
        )}
      </div>

      <button
        type="submit"
        className="btn btn-primary"
        disabled={
          isSubmitting
          || channelIds.length === 0
          || (categoryRequired && !form.promptTemplateId)
          || (categoryRequired && categoryTemplates.length === 0)
        }
      >
        {isSubmitting
          ? '⏳ Đang xử lý...'
          : channelIds.length > 1
            ? `Tạo ${channelIds.length} bài → AI sinh nền`
            : 'Hoàn tất — để AI sinh bài'}
      </button>
    </form>
  )
}
