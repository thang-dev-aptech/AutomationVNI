import { useEffect, useMemo, useState } from 'react'
import Modal from '@/shared/components/Modal'
import ChannelMultiSelect from '@/shared/components/ChannelMultiSelect'
import { useSocialChannelAll } from '@/modules/social-channels/hooks/useSocialChannels'
import { usePromptTemplateList } from '@/modules/prompt-templates/hooks/usePromptTemplates'

/**
 * Ô duyệt tin. Có HAI hình dạng, chọn theo cờ ContentCrawl:TwoGateFlow của backend:
 *
 *   twoGateFlow = true  → CỬA 1: bấm Duyệt là đưa lên website. Fanpage là bước riêng.
 *   twoGateFlow = false → luồng cũ: chọn page, fan-out post, đăng fanpage ngay.
 *
 * Vì sao phải đọc cờ chứ không viết cứng: khi cờ bật mà ô này vẫn hỏi chọn page, người duyệt
 * chọn 3 page rồi bấm, tưởng đã hẹn đăng — thực ra ApproveAsync thoát sớm và KHÔNG page nào
 * biết. Không lỗi nào hiện ra, chỉ có bài không bao giờ lên.
 */
export default function ApproveArticleModal({ open, article, onClose, onSubmit, submitting, twoGateFlow }) {
  // Chưa đọc được cờ thì coi như CỬA 1 — đó là mặc định của backend. Đoán nhầm về phía luồng
  // cũ nguy hiểm hơn: nó hỏi page rồi vứt đi.
  const twoGate = twoGateFlow !== false

  const { data: channelData } = useSocialChannelAll()
  const { data: templateData } = usePromptTemplateList()
  const [channelIds, setChannelIds] = useState([])
  const [templateId, setTemplateId] = useState('')
  const [autoSchedule, setAutoSchedule] = useState(false)
  // Mặc định chỉ sinh chữ + link nguồn — tin tức không cần banner, đỡ hẳn một lượt gọi AI ảnh
  // cho mỗi bài × mỗi page.
  const [withImage, setWithImage] = useState(false)

  const channels = useMemo(() => channelData?.items ?? channelData ?? [], [channelData])
  const templates = useMemo(() => templateData?.items ?? templateData ?? [], [templateData])

  useEffect(() => {
    if (open) {
      setChannelIds([])
      setTemplateId('')
      setAutoSchedule(false)
      setWithImage(false)
    }
  }, [open])

  if (!article) return null

  const handleSubmit = () => {
    if (twoGate) { onSubmit({}); return }
    onSubmit({
      channelIds,
      promptTemplateId: templateId || null,
      autoSchedule,
      generationFlow: withImage ? 1 : 5, // 1 = Full AI (có ảnh), 5 = TextOnly
    })
  }

  const submitLabel = twoGate
    ? (submitting ? 'Đang viết bài…' : 'Đưa lên web')
    : (submitting ? 'Đang tạo...' : `Tạo ${channelIds.length || ''} bài`)

  return (
    <Modal
      open={open}
      title={twoGate ? 'Duyệt tin → đưa lên website' : 'Duyệt tin & tạo bài'}
      onClose={onClose}
      footer={(
        <>
          <button type="button" className="btn btn-ghost" onClick={onClose} disabled={submitting}>Huỷ</button>
          <button
            type="button"
            className="btn btn-primary"
            onClick={handleSubmit}
            disabled={submitting || (!twoGate && channelIds.length === 0)}
          >
            {submitLabel}
          </button>
        </>
      )}
    >
      <div className="crawl-approve">
        <p className="crawl-approve-title">{article.title}</p>

        {twoGate ? (
          <>
            <p className="form-hint">
              AI sẽ viết lại thành bài hoàn chỉnh 500–800 chữ rồi đăng lên
              <b> tintuc.vni.edu.vn</b>. Mọi con số trong bài được đối chiếu với bản gốc,
              bịa dữ kiện là chặn không cho lên.
            </p>
            <p className="form-hint">
              <b>Chưa đăng fanpage ở bước này.</b> Bài lên web xong mới sang mục
              “Bài đã lên web” chọn page — lúc đó link dán ở bình luận mới là link của mình.
            </p>
            <p className="form-hint">
              Mất khoảng 20–40 giây vì phải chờ AI viết xong.
            </p>
          </>
        ) : (
          <>
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
                checked={withImage}
                onChange={(e) => setWithImage(e.target.checked)}
              />
              <span>Sinh thêm ảnh banner bằng AI</span>
            </label>
            <p className="form-hint">
              Mặc định KHÔNG sinh ảnh — bài tin đăng dạng tóm tắt kèm link bài gốc bên dưới,
              tiết kiệm một lượt gọi AI ảnh cho mỗi page. Tick nếu muốn có banner.
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
          </>
        )}
      </div>
    </Modal>
  )
}
