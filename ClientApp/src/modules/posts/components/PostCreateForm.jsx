import { useState } from 'react'
import { GENERATION_FLOW_OPTIONS } from '../constants/postStatus'

const emptyForm = {
  idea: '',
  objective: '',
  socialChannelId: '',
  categoryId: '',
  generationFlow: '1',
}

export default function PostCreateForm({
  channels = [],
  categories = [],
  isSubmitting,
  errorMessage,
  onSubmit,
}) {
  const [form, setForm] = useState(emptyForm)

  const handleChange = (field) => (event) => {
    setForm((prev) => ({ ...prev, [field]: event.target.value }))
  }

  const handleSubmit = (event) => {
    event.preventDefault()
    onSubmit({
      title: form.idea.trim(), // ý tưởng → tiêu đề + prompt AI
      objective: form.objective.trim() || null,
      socialChannelId: form.socialChannelId,
      generationFlow: Number(form.generationFlow),
      categoryId: form.categoryId || null,
    })
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
        <label htmlFor="post-objective">Mục tiêu (tuỳ chọn)</label>
        <input
          id="post-objective"
          value={form.objective}
          onChange={handleChange('objective')}
          placeholder="Ví dụ: Tăng đơn hàng, thu hút khách mới đăng ký"
        />
      </div>

      <div className="form-group">
        <label htmlFor="post-channel">Kênh đăng *</label>
        <select
          id="post-channel"
          value={form.socialChannelId}
          onChange={handleChange('socialChannelId')}
          required
        >
          <option value="">Chọn kênh</option>
          {channels.map((channel) => (
            <option key={channel.id} value={channel.id}>
              {channel.pageName}
            </option>
          ))}
        </select>
        {channels.length === 0 && (
          <p style={{ margin: '6px 0 0', fontSize: '0.85rem', color: 'var(--color-warning)' }}>
            Chưa có kênh nào — hãy kết nối kênh tại Platforms trước.
          </p>
        )}
      </div>

      {categories.length > 0 && (
        <div className="form-group">
          <label htmlFor="post-category">Danh mục (tuỳ chọn)</label>
          <select
            id="post-category"
            value={form.categoryId}
            onChange={handleChange('categoryId')}
          >
            <option value="">Không chọn</option>
            {categories.map((category) => (
              <option key={category.id} value={category.id}>
                {category.name}
              </option>
            ))}
          </select>
        </div>
      )}

      <div className="form-group">
        <label htmlFor="post-flow">Luồng sinh nội dung</label>
        <select
          id="post-flow"
          value={form.generationFlow}
          onChange={handleChange('generationFlow')}
        >
          {GENERATION_FLOW_OPTIONS.map((option) => (
            <option key={option.value} value={option.value}>
              {option.label}
            </option>
          ))}
        </select>
      </div>

      <button type="submit" className="btn btn-primary" disabled={isSubmitting}>
        {isSubmitting ? '⏳ AI đang sinh text + ảnh...' : 'Hoàn tất — để AI sinh bài'}
      </button>
    </form>
  )
}
