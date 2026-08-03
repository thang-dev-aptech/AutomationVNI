import { useState } from 'react'
import Modal from '@/shared/components/Modal'
import { toast } from '@/shared/stores/toastStore'
import { getErrorMessage } from '@/shared/utils/apiHelpers'
import { useSocialChannelAll } from '@/modules/social-channels/hooks/useSocialChannels'
import { useRecyclePosts } from '../hooks/usePosts'
import ChannelMultiSelect from '@/shared/components/ChannelMultiSelect'
import {
  buildJitteredTimesForDay,
  parseTimeSlots
} from '@/modules/bulk/utils/bulkScheduleSkeleton'

function defaultDateFrom() {
  const d = new Date()
  return d.toISOString().slice(0, 10)
}

export default function RecyclePostModal({ open, onClose }) {
  const [channelIds, setChannelIds] = useState([])
  const [count, setCount] = useState(10)
  const [scheduleEnabled, setScheduleEnabled] = useState(false)
  const [skelFrom, setSkelFrom] = useState(defaultDateFrom)
  const [skelSlots, setSkelSlots] = useState('09:00,15:00')
  const [skelJitter, setSkelJitter] = useState(35)
  const [imageStrategy, setImageStrategy] = useState(1) // 1 = KeepOld, 2 = VectorSearch

  const { data: channels = [] } = useSocialChannelAll()
  const recycleMutation = useRecyclePosts()

  const handleClose = () => {
    if (recycleMutation.isPending) return
    setChannelIds([])
    setCount(10)
    setScheduleEnabled(false)
    setSkelFrom(defaultDateFrom())
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
    const reqCount = Number(count)
    if (reqCount < 1 || reqCount > 100) {
      toast.error('Số lượng bài phải từ 1 đến 100')
      return
    }

    let startTimes = []
    if (scheduleEnabled) {
      try {
        const jitter = Math.max(0, Math.min(240, Number(skelJitter) || 0))
        const slots = parseTimeSlots(skelSlots)
        if (slots.length === 0) {
          toast.error('Vui lòng nhập ít nhất 1 khung giờ hợp lệ, ví dụ 09:00,15:00')
          return
        }
        let scheduledTimes = []
        let currentDay = new Date(skelFrom)
        
        while (scheduledTimes.length < reqCount) {
          const dayStr = currentDay.toISOString().slice(0, 10)
          const times = buildJitteredTimesForDay(dayStr, slots, jitter, 0)
          for (const t of times) {
            if (scheduledTimes.length < reqCount) {
              scheduledTimes.push(t.toISOString())
            }
          }
          currentDay.setDate(currentDay.getDate() + 1)
        }
        
        startTimes = scheduledTimes
      } catch (err) {
        toast.error(err.message || 'Lỗi tính toán khung giờ')
        return
      }
    }

    try {
      const result = await recycleMutation.mutateAsync({
        channelIds,
        count: reqCount,
        flow: 3, // GenerationFlow.Recycle
        scheduleEnabled,
        startTimes: scheduleEnabled ? startTimes : null,
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
                <label style={{ fontSize: '0.85rem' }}>Lệch giờ ± phút</label>
                <input
                  type="number"
                  min="0"
                  max="240"
                  value={skelJitter}
                  onChange={(e) => setSkelJitter(e.target.value)}
                  disabled={recycleMutation.isPending}
                />
                <p className="bulk-field-hint" style={{ marginTop: 4, fontSize: '0.75rem', color: '#666' }}>0 = đúng khung</p>
              </div>
            </div>
          )}
        </div>
      </div>
    </Modal>
  )
}
