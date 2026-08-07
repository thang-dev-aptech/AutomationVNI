import { useEffect, useMemo, useState } from 'react'
import Modal from '@/shared/components/Modal'
import ChannelMultiSelect from '@/shared/components/ChannelMultiSelect'
import { useSocialChannelAll } from '@/modules/social-channels/hooks/useSocialChannels'

/**
 * CỬA 2 — chọn page để đăng một bài ĐÃ LÊN WEB.
 *
 * Khác ô duyệt ở cửa 1: ở đây mới hỏi page, vì lúc này bài đã có URL riêng trên
 * tintuc.vni.edu.vn để dán vào bình luận. Ở cửa 1 chưa có URL nào để dán.
 */
export default function FanpageModal({ open, article, onClose, onSubmit, submitting }) {
  const { data: channelData } = useSocialChannelAll()
  const [channelIds, setChannelIds] = useState([])
  const [autoPublish, setAutoPublish] = useState(true)

  const channels = useMemo(() => channelData?.items ?? channelData ?? [], [channelData])

  useEffect(() => {
    if (open) {
      setChannelIds([])
      setAutoPublish(true)
    }
  }, [open])

  if (!article) return null

  return (
    <Modal
      open={open}
      title="Đăng bài lên fanpage"
      onClose={onClose}
      footer={(
        <>
          <button type="button" className="btn btn-ghost" onClick={onClose} disabled={submitting}>Huỷ</button>
          <button
            type="button"
            className="btn btn-primary"
            onClick={() => onSubmit({ channelIds, autoPublish })}
            disabled={submitting || channelIds.length === 0}
          >
            {submitting ? 'Đang tạo…' : `Đăng ${channelIds.length || ''} page`}
          </button>
        </>
      )}
    >
      <div className="crawl-approve">
        <p className="crawl-approve-title">{article.title}</p>

        {article.url && (
          <p className="form-hint">
            Bình luận đầu tiên sẽ dẫn về <b>{article.url}</b> — link của mình, không phải
            link báo gốc.
          </p>
        )}

        <label className="form-label">Đăng lên page</label>
        <ChannelMultiSelect
          channels={channels}
          value={channelIds}
          onChange={setChannelIds}
          label="Chọn page"
        />
        <p className="form-hint">
          Mỗi page được AI viết một caption riêng theo giọng văn của page đó, rút từ phần
          “Điều cần nhớ” của bài web — không phải đọc lại toàn văn báo gốc.
        </p>

        <label className="form-check">
          <input
            type="checkbox"
            checked={autoPublish}
            onChange={(e) => setAutoPublish(e.target.checked)}
          />
          <span>Đăng ngay sau khi AI viết xong</span>
        </label>
        <p className="form-hint">
          Bỏ tick thì bài dừng ở trạng thái chờ duyệt bên module Bài đăng để bạn rà lại
          caption rồi mới lên lịch.
        </p>
      </div>
    </Modal>
  )
}
