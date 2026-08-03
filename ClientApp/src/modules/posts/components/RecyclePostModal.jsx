import { useState } from 'react'
import Modal from '@/shared/components/Modal'
import { toast } from '@/shared/stores/toastStore'
import { getErrorMessage } from '@/shared/utils/apiHelpers'
import { useSocialChannelAll } from '@/modules/social-channels/hooks/useSocialChannels'
import { useRecyclePosts } from '../hooks/usePosts'

export default function RecyclePostModal({ open, onClose }) {
  const [channelId, setChannelId] = useState('')
  const [count, setCount] = useState(10)
  const [flow, setFlow] = useState(3) // 3 = Recycle (Verbatim), 4 = RecycleRewrite
  const [scheduleEnabled, setScheduleEnabled] = useState(false)
  const [startTime, setStartTime] = useState('')
  const [intervalMinutes, setIntervalMinutes] = useState(60)
  const [imageStrategy, setImageStrategy] = useState(1) // 1 = KeepOld, 2 = VectorSearch

  const { data: channels = [] } = useSocialChannelAll()
  const recycleMutation = useRecyclePosts()

  const handleClose = () => {
    if (recycleMutation.isPending) return
    setChannelId('')
    setCount(10)
    setFlow(3)
    setScheduleEnabled(false)
    setStartTime('')
    setIntervalMinutes(60)
    setImageStrategy(1)
    onClose()
  }

  const handleSubmit = async () => {
    if (!channelId) {
      toast.error('Vui lòng chọn kênh mạng xã hội')
      return
    }
    if (count < 1 || count > 100) {
      toast.error('Số lượng bài phải từ 1 đến 100')
      return
    }
    if (scheduleEnabled && !startTime) {
      toast.error('Vui lòng chọn thời gian bắt đầu lên lịch')
      return
    }

    try {
      const result = await recycleMutation.mutateAsync({
        channelId,
        count: Number(count),
        flow: Number(flow),
        scheduleEnabled,
        startTime: scheduleEnabled && startTime ? new Date(startTime).toISOString() : null,
        intervalMinutes: scheduleEnabled ? Number(intervalMinutes) : null,
        imageStrategy: flow === 3 ? 1 : Number(imageStrategy)
      })
      const createdCount = result?.created ?? 0
      if (createdCount > 0) {
        toast.success(`Đã tái tạo ${createdCount} bài viết`)
      } else {
        toast.success('Không có bài viết nào được tái tạo (có thể kênh chưa có bài cũ)')
      }
      handleClose()
    } catch (error) {
      toast.error(getErrorMessage(error))
    }
  }

  const footer = (
    <div style={{ display: 'flex', gap: 8, justifyContent: 'flex-end' }}>
      <button
        type="button"
        className="btn btn-ghost"
        onClick={handleClose}
        disabled={recycleMutation.isPending}
      >
        Hủy
      </button>
      <button
        type="button"
        className="btn btn-success"
        onClick={handleSubmit}
        disabled={recycleMutation.isPending}
      >
        {recycleMutation.isPending ? 'Đang tạo...' : 'Tạo lô bài mới'}
      </button>
    </div>
  )

  return (
    <Modal open={open} title="Tái sử dụng bài viết" onClose={handleClose} footer={footer}>
      <div style={{ display: 'flex', flexDirection: 'column', gap: 16 }}>
        <div className="form-group" style={{ marginBottom: 0 }}>
          <label htmlFor="recycle-channel">Kênh mạng xã hội <span className="text-danger">*</span></label>
          <select
            id="recycle-channel"
            value={channelId}
            onChange={(e) => setChannelId(e.target.value)}
            disabled={recycleMutation.isPending}
          >
            <option value="">-- Chọn kênh --</option>
            {channels.map((c) => (
              <option key={c.id} value={c.id}>
                {c.pageName} ({c.platform})
              </option>
            ))}
          </select>
        </div>

        <div className="form-group" style={{ marginBottom: 0 }}>
          <label htmlFor="recycle-count">Số lượng bài muốn tạo mới</label>
          <input
            id="recycle-count"
            type="number"
            min="1"
            max="100"
            value={count}
            onChange={(e) => setCount(e.target.value)}
            disabled={recycleMutation.isPending}
          />
        </div>

        <div className="form-group" style={{ marginBottom: 0 }}>
          <label>Phương thức tái sử dụng</label>
          <div style={{ display: 'flex', gap: 16, marginTop: 8 }}>
            <label style={{ display: 'flex', alignItems: 'center', gap: 8, fontWeight: 'normal' }}>
              <input
                type="radio"
                name="recycle-flow"
                value={3}
                checked={flow === 3}
                onChange={() => setFlow(3)}
                disabled={recycleMutation.isPending}
              />
              Copy nguyên văn (Giữ nguyên)
            </label>
            <label style={{ display: 'flex', alignItems: 'center', gap: 8, fontWeight: 'normal' }}>
              <input
                type="radio"
                name="recycle-flow"
                value={4}
                checked={flow === 4}
                onChange={() => setFlow(4)}
                disabled={recycleMutation.isPending}
              />
              AI Viết lại (Paraphrase)
            </label>
          </div>
        </div>

        <div className="form-group" style={{ marginBottom: 0 }}>
          <label>Chiến lược hình ảnh</label>
          <div style={{ display: 'flex', gap: 16, marginTop: 8 }}>
            <label style={{ display: 'flex', alignItems: 'center', gap: 8, fontWeight: 'normal' }}>
              <input
                type="radio"
                name="image-strategy"
                value={1}
                checked={imageStrategy === 1 || flow === 3}
                onChange={() => setImageStrategy(1)}
                disabled={recycleMutation.isPending || flow === 3}
              />
              Dùng lại ảnh gốc
            </label>
            {flow === 4 && (
              <label style={{ display: 'flex', alignItems: 'center', gap: 8, fontWeight: 'normal' }}>
                <input
                  type="radio"
                  name="image-strategy"
                  value={2}
                  checked={imageStrategy === 2}
                  onChange={() => setImageStrategy(2)}
                  disabled={recycleMutation.isPending}
                />
                Vector Search (Tìm ảnh kho)
              </label>
            )}
          </div>
        </div>

        <div className="form-group" style={{ marginBottom: 0 }}>
          <label style={{ display: 'flex', alignItems: 'center', gap: 8, cursor: 'pointer' }}>
            <input
              type="checkbox"
              checked={scheduleEnabled}
              onChange={(e) => setScheduleEnabled(e.target.checked)}
              disabled={recycleMutation.isPending}
            />
            Tự động lên lịch đăng
          </label>
          {scheduleEnabled && (
            <div style={{ display: 'flex', gap: 16, marginTop: 12 }}>
              <div style={{ flex: 1 }}>
                <label style={{ fontSize: '0.85rem' }}>Bắt đầu từ</label>
                <input
                  type="datetime-local"
                  value={startTime}
                  onChange={(e) => setStartTime(e.target.value)}
                  disabled={recycleMutation.isPending}
                />
              </div>
              <div style={{ flex: 1 }}>
                <label style={{ fontSize: '0.85rem' }}>Cách nhau (phút)</label>
                <input
                  type="number"
                  min="1"
                  value={intervalMinutes}
                  onChange={(e) => setIntervalMinutes(e.target.value)}
                  disabled={recycleMutation.isPending}
                />
              </div>
            </div>
          )}
        </div>
      </div>
    </Modal>
  )
}
