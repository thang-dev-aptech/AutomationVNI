import { useEffect, useMemo, useState } from 'react'
import Modal from '@/shared/components/Modal'
import ChannelMultiSelect from '@/shared/components/ChannelMultiSelect'
import { useSocialChannelAll } from '@/modules/social-channels/hooks/useSocialChannels'
import { usePromptTemplateList } from '@/modules/prompt-templates/hooks/usePromptTemplates'

export default function ApproveArticleModal({ open, article, onClose, onSubmit, submitting }) {
  const { data: channelData } = useSocialChannelAll()
  const { data: templateData } = usePromptTemplateList()
  const [channelIds, setChannelIds] = useState([])
  const [templateId, setTemplateId] = useState('')
  const [autoSchedule, setAutoSchedule] = useState(false)

  const channels = useMemo(() => channelData?.items ?? channelData ?? [], [channelData])
  const templates = useMemo(() => templateData?.items ?? templateData ?? [], [templateData])

  useEffect(() => {
    if (open) {
      setChannelIds([])
      setTemplateId('')
      setAutoSchedule(false)
    }
  }, [open])

  if (!article) return null

  const handleSubmit = () => {
    onSubmit({
      channelIds,
      promptTemplateId: templateId || null,
      autoSchedule,
    })
  }

  return (
    <Modal
      open={open}
      title="Duyệt tin & tạo bài"
      onClose={onClose}
      footer={(
        <>
          <button type="button" className="btn btn-ghost" onClick={onClose} disabled={submitting}>Huỷ</button>
          <button
            type="button"
            className="btn btn-primary"
            onClick={handleSubmit}
            disabled={submitting || channelIds.length === 0}
          >
            {submitting ? 'Đang tạo...' : `Tạo ${channelIds.length || ''} bài`}
          </button>
        </>
      )}
    >
      <div className="crawl-approve">
        <p className="crawl-approve-title">{article.title}</p>

        <label className="form-label">Đăng lên page</label>
        <ChannelMultiSelect
          channels={channels}
          value={channelIds}
          onChange={setChannelIds}
          label="Chọn page"
        />
        <p className="form-hint">
          Mỗi page sẽ được AI viết một bản riêng theo thương hiệu, giọng văn và CTA
          cấu hình trong PageContext của page đó.
        </p>

        <label className="form-label" htmlFor="crawl-tpl">Danh mục prompt (tuỳ chọn)</label>
        <select
          id="crawl-tpl"
          className="form-control"
          value={templateId}
          onChange={(e) => setTemplateId(e.target.value)}
        >
          <option value="">— Dùng mặc định của từng page —</option>
          {templates.map((t) => <option key={t.id} value={t.id}>{t.name}</option>)}
        </select>
        <p className="form-hint">
          Bắt buộc chọn nếu có page chưa cấu hình PageContext hoặc template mặc định.
        </p>

        <label className="form-check">
          <input
            type="checkbox"
            checked={autoSchedule}
            onChange={(e) => setAutoSchedule(e.target.checked)}
          />
          <span>
            Rải lịch luôn theo khung giờ vàng (8h/12h/20h, lệch ngẫu nhiên)
          </span>
        </label>
        <p className="form-hint">
          Không tick thì bài dừng ở trạng thái đã duyệt để bạn rà lại nội dung rồi mới lên lịch.
        </p>
      </div>
    </Modal>
  )
}
