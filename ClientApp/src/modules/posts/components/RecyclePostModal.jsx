import { useState } from 'react'
import Modal from '@/shared/components/Modal'
import { toast } from '@/shared/stores/toastStore'
import { getErrorMessage } from '@/shared/utils/apiHelpers'
import { useSocialChannelAll } from '@/modules/social-channels/hooks/useSocialChannels'
import { useRecyclePosts } from '../hooks/usePosts'
import ChannelMultiSelect from '@/shared/components/ChannelMultiSelect'
import {
  eachDateInclusive,
  buildJitteredTimesForDay,
  parseTimeSlots
} from '@/modules/bulk/utils/bulkScheduleSkeleton'

function defaultDateFrom() {
  const d = new Date()
  return d.toISOString().slice(0, 10)
}

function defaultDateTo() {
  const d = new Date()
  d.setDate(d.getDate() + 6)
  return d.toISOString().slice(0, 10)
}

export default function RecyclePostModal({ open, onClose }) {
  const [channelIds, setChannelIds] = useState([])
  const [skelFrom, setSkelFrom] = useState(defaultDateFrom)
  const [skelTo, setSkelTo] = useState(defaultDateTo)
  const [skelSlots, setSkelSlots] = useState('09:00,15:00')
  const [skelJitter, setSkelJitter] = useState(60)
  const [imageStrategy, setImageStrategy] = useState(1) // 1 = KeepOld, 2 = VectorSearch

  const { data: channels = [] } = useSocialChannelAll()
  const recycleMutation = useRecyclePosts()

  const handleClose = () => {
    if (recycleMutation.isPending) return
    setChannelIds([])
    setSkelFrom(defaultDateFrom())
    setSkelTo(defaultDateTo())
    setSkelSlots('09:00,15:00')
    setSkelJitter(35)
    setImageStrategy(1)
    onClose()
  }

  const handleSubmit = async () => {
    if (!channelIds || channelIds.length === 0) {
      toast.error('Vui lòng chọn ít nhất một kênh mạng xã hội')
      return
    }

    let startTimes = []
    try {
      const days = eachDateInclusive(skelFrom, skelTo)
      const jitterValue = Math.max(0, Math.min(240, Number(skelJitter) || 0))
      const slots = parseTimeSlots(skelSlots)
      if (slots.length === 0) {
        toast.error('Vui lòng nhập ít nhất 1 khung giờ hợp lệ, ví dụ 09:00,15:00')
        return
      }

      for (const day of days) {
        // Gọi hàm nhưng truyền jitter = 0 để Frontend không tự xê dịch
        const times = buildJitteredTimesForDay(day, slots, 0, 0)
        for (const t of times) {
          startTimes.push(t.toISOString())
        }
      }

      if (startTimes.length === 0) {
        toast.error('Không tính toán được mốc thời gian nào từ lịch trình đã chọn')
        return
      }
      if (startTimes.length > 100) {
        toast.error(`Lịch trình sinh ra tới ${startTimes.length} bài. Tối đa chỉ hỗ trợ tạo 100 bài mỗi lần.`)
        return
      }
    } catch (err) {
      toast.error(err.message || 'Lỗi tính toán khung giờ')
      return
    }

    try {
      const result = await recycleMutation.mutateAsync({
        channelIds,
        count: startTimes.length,
        flow: 3, // GenerationFlow.Recycle
        scheduleEnabled: true,
        startTimes: startTimes,
        jitterMinutes: Number(skelJitter) || 0,
        imageStrategy: Number(imageStrategy)
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
          <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
            <label>Kênh mạng xã hội <span className="text-danger">*</span></label>
            <div style={{ fontSize: '0.875rem' }}>
              <span className="text-muted" style={{ marginRight: 8 }}>
                {channelIds.length}/{channels.length} đã chọn
              </span>
              <button
                type="button"
                className="btn btn-link btn-sm p-0 text-decoration-none me-2"
                onClick={() => setChannelIds(channels.map((c) => c.id))}
                disabled={recycleMutation.isPending}
              >
                Chọn tất cả
              </button>
              <button
                type="button"
                className="btn btn-link btn-sm p-0 text-danger text-decoration-none"
                onClick={() => setChannelIds([])}
                disabled={recycleMutation.isPending}
              >
                Bỏ chọn
              </button>
            </div>
          </div>
          <ChannelMultiSelect
            channels={channels}
            value={channelIds}
            onChange={setChannelIds}
            disabled={recycleMutation.isPending}
            placeholder="-- Chọn các kênh --"
          />
        </div>

        <div className="form-group" style={{ marginBottom: 0 }}>
          <label>Chiến lược hình ảnh</label>
          <div style={{ display: 'flex', gap: 16, marginTop: 8 }}>
            <label style={{ display: 'flex', alignItems: 'center', gap: 8, fontWeight: 'normal' }}>
              <input
                type="radio"
                name="image-strategy"
                value={1}
                checked={imageStrategy === 1}
                onChange={() => setImageStrategy(1)}
                disabled={recycleMutation.isPending}
              />
              Dùng lại ảnh gốc
            </label>
            <label style={{ display: 'flex', alignItems: 'center', gap: 8, fontWeight: 'normal' }}>
              <input
                type="radio"
                name="image-strategy"
                value={2}
                checked={imageStrategy === 2}
                onChange={() => setImageStrategy(2)}
                disabled={recycleMutation.isPending}
              />
              Hệ thống tự chọn (Vector Search)
            </label>
          </div>
        </div>

        <div className="form-group" style={{ marginBottom: 0 }}>
          <label style={{ fontSize: '0.85rem', fontWeight: 600 }}>Cấu hình lịch đăng</label>
          <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 16, marginTop: 12 }}>
            <div>
              <label style={{ fontSize: '0.85rem' }}>Từ ngày</label>
              <input
                type="date"
                value={skelFrom}
                onChange={(e) => setSkelFrom(e.target.value)}
                disabled={recycleMutation.isPending}
              />
            </div>
            <div>
              <label style={{ fontSize: '0.85rem' }}>Đến ngày</label>
              <input
                type="date"
                value={skelTo}
                onChange={(e) => setSkelTo(e.target.value)}
                disabled={recycleMutation.isPending}
              />
            </div>
            <div>
              <label style={{ fontSize: '0.85rem' }}>Khung giờ / ngày</label>
              <input
                value={skelSlots}
                onChange={(e) => setSkelSlots(e.target.value)}
                placeholder="09:00,15:00"
                disabled={recycleMutation.isPending}
              />
              <p className="bulk-field-hint" style={{ marginTop: 4, fontSize: '0.75rem', color: '#666' }}>Ví dụ: 09:00,15:00</p>
            </div>
            <div>
              <label style={{ fontSize: '0.85rem' }}>Độ lệch giờ (phút)</label>
              <input
                type="number"
                min="0"
                value={skelJitter}
                onChange={(e) => setSkelJitter(e.target.value)}
                disabled={recycleMutation.isPending}
              />
              <p className="bulk-field-hint" style={{ marginTop: 4, fontSize: '0.75rem', color: '#666' }}>Lệch ngẫu nhiên ± phút</p>
            </div>
          </div>
        </div>
      </div>
    </Modal>
  )
}
