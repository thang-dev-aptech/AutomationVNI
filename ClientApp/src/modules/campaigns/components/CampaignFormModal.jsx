import { useEffect, useState } from 'react'
import Modal from '@/shared/components/Modal'
import ChannelMultiSelect from '@/shared/components/ChannelMultiSelect'
import { useSocialChannelAll } from '@/modules/social-channels/hooks/useSocialChannels'
import { useChannelGroupAll } from '@/modules/social-channels/hooks/useChannelGroups'
import {
  CAMPAIGN_IMAGE_STRATEGY,
  CAMPAIGN_MEDIA_TYPE,
  CAMPAIGN_SCHEDULE_MODE,
  WEEKDAY_OPTIONS,
} from '../constants/campaignEnums'
import {
  buildCampaignPayload,
  emptyCampaignForm,
  formFromCampaign,
  tryAddPublishTime,
  validateCampaignForm,
} from '../utils/campaignForm'

export default function CampaignFormModal({
  open,
  onClose,
  initialData,
  onSubmit,
  isSubmitting,
  errorMessage,
}) {
  const [form, setForm] = useState(emptyCampaignForm)
  const [fieldErrors, setFieldErrors] = useState({})
  const { data: channels = [] } = useSocialChannelAll()
  const { data: groups = [] } = useChannelGroupAll()

  useEffect(() => {
    if (!open) return
    setForm(formFromCampaign(initialData))
    setFieldErrors({})
  }, [open, initialData])

  const setField = (key, value) => {
    setForm((prev) => ({ ...prev, [key]: value }))
    setFieldErrors((prev) => {
      if (!prev[key]) return prev
      const next = { ...prev }
      delete next[key]
      return next
    })
  }

  const toggleWeekday = (day) => {
    setForm((prev) => {
      const set = new Set(prev.weekdays)
      if (set.has(day)) set.delete(day)
      else set.add(day)
      return { ...prev, weekdays: [...set].sort((a, b) => a - b) }
    })
    setFieldErrors((prev) => {
      if (!prev.weekdays) return prev
      const next = { ...prev }
      delete next.weekdays
      return next
    })
  }

  const toggleGroup = (groupId) => {
    setForm((prev) => {
      const set = new Set(prev.channelGroupIds)
      if (set.has(groupId)) set.delete(groupId)
      else set.add(groupId)
      return { ...prev, channelGroupIds: [...set] }
    })
    setFieldErrors((prev) => {
      if (!prev.targets) return prev
      const next = { ...prev }
      delete next.targets
      return next
    })
  }

  const handleAddTime = () => {
    const result = tryAddPublishTime(form.publishTimes, form.timeDraft)
    if (!result.ok) {
      setFieldErrors((prev) => ({ ...prev, publishTimes: result.error }))
      return
    }
    setForm((prev) => ({ ...prev, publishTimes: result.times, timeDraft: '' }))
    setFieldErrors((prev) => {
      if (!prev.publishTimes) return prev
      const next = { ...prev }
      delete next.publishTimes
      return next
    })
  }

  const removeTime = (time) => {
    setForm((prev) => ({
      ...prev,
      publishTimes: prev.publishTimes.filter((t) => t !== time),
    }))
  }

  const handleSubmit = async () => {
    const check = validateCampaignForm(form)
    if (!check.ok) {
      setFieldErrors(check.errors)
      return
    }
    setFieldErrors({})
    await onSubmit(buildCampaignPayload(form))
  }

  const footer = (
    <div style={{ display: 'flex', gap: 8, justifyContent: 'flex-end' }}>
      <button type="button" className="btn btn-ghost" onClick={onClose} disabled={isSubmitting}>
        Hủy
      </button>
      <button
        type="button"
        className="btn btn-primary"
        onClick={handleSubmit}
        disabled={isSubmitting}
        data-testid="campaign-form-submit"
      >
        {isSubmitting ? 'Đang lưu...' : initialData ? 'Lưu' : 'Tạo chiến dịch'}
      </button>
    </div>
  )

  return (
    <Modal
      open={open}
      title={initialData ? 'Sửa chiến dịch' : 'Tạo chiến dịch'}
      onClose={onClose}
      footer={footer}
    >
      <div style={{ display: 'flex', flexDirection: 'column', gap: 14 }} data-testid="campaign-form">
        {errorMessage ? (
          <div className="text-danger" role="alert">{errorMessage}</div>
        ) : null}

        <div className="form-group" style={{ marginBottom: 0 }}>
          <label htmlFor="campaign-name">
            Tên <span className="text-danger">*</span>
          </label>
          <input
            id="campaign-name"
            value={form.name}
            onChange={(e) => setField('name', e.target.value)}
            disabled={isSubmitting}
          />
          {fieldErrors.name ? (
            <p className="text-danger" data-testid="error-name">{fieldErrors.name}</p>
          ) : null}
        </div>

        <div className="form-group" style={{ marginBottom: 0 }}>
          <label>Loại bài nguồn</label>
          <div style={{ display: 'flex', gap: 16, marginTop: 8 }}>
            <label style={{ display: 'flex', alignItems: 'center', gap: 6, fontWeight: 'normal' }}>
              <input
                type="radio"
                name="media-type"
                checked={form.mediaType === CAMPAIGN_MEDIA_TYPE.Image}
                onChange={() => setField('mediaType', CAMPAIGN_MEDIA_TYPE.Image)}
                disabled={isSubmitting}
              />
              Ảnh
            </label>
            <label style={{ display: 'flex', alignItems: 'center', gap: 6, fontWeight: 'normal' }}>
              <input
                type="radio"
                name="media-type"
                checked={form.mediaType === CAMPAIGN_MEDIA_TYPE.Video}
                onChange={() => setField('mediaType', CAMPAIGN_MEDIA_TYPE.Video)}
                disabled={isSubmitting}
              />
              Video
            </label>
          </div>
        </div>

        {form.mediaType === CAMPAIGN_MEDIA_TYPE.Image ? (
          <div className="form-group" style={{ marginBottom: 0 }} data-testid="image-strategy-block">
            <label>Chiến lược hình ảnh</label>
            <div style={{ display: 'flex', gap: 16, marginTop: 8, flexWrap: 'wrap' }}>
              <label style={{ display: 'flex', alignItems: 'center', gap: 6, fontWeight: 'normal' }}>
                <input
                  type="radio"
                  name="image-strategy"
                  checked={form.imageStrategy === CAMPAIGN_IMAGE_STRATEGY.KeepOld}
                  onChange={() => setField('imageStrategy', CAMPAIGN_IMAGE_STRATEGY.KeepOld)}
                  disabled={isSubmitting}
                />
                Dùng lại ảnh gốc
              </label>
              <label style={{ display: 'flex', alignItems: 'center', gap: 6, fontWeight: 'normal' }}>
                <input
                  type="radio"
                  name="image-strategy"
                  checked={form.imageStrategy === CAMPAIGN_IMAGE_STRATEGY.VectorSearch}
                  onChange={() => setField('imageStrategy', CAMPAIGN_IMAGE_STRATEGY.VectorSearch)}
                  disabled={isSubmitting}
                />
                Hệ thống tự chọn (Vector Search)
              </label>
            </div>
            {fieldErrors.imageStrategy ? (
              <p className="text-danger">{fieldErrors.imageStrategy}</p>
            ) : null}
          </div>
        ) : null}

        <div className="form-group" style={{ marginBottom: 0 }}>
          <label>Kênh</label>
          <ChannelMultiSelect
            channels={channels}
            value={form.channelIds}
            onChange={(ids) => setField('channelIds', ids)}
            disabled={isSubmitting}
            placeholder="-- Chọn kênh --"
          />
        </div>

        <div className="form-group" style={{ marginBottom: 0 }} data-testid="channel-groups">
          <label>Nhóm kênh</label>
          <div style={{ display: 'flex', flexDirection: 'column', gap: 6, marginTop: 8 }}>
            {groups.length === 0 ? (
              <span className="text-muted">Chưa có nhóm kênh</span>
            ) : (
              groups.map((g) => (
                <label
                  key={g.id}
                  style={{ display: 'flex', alignItems: 'center', gap: 8, fontWeight: 'normal' }}
                >
                  <input
                    type="checkbox"
                    checked={form.channelGroupIds.includes(g.id)}
                    onChange={() => toggleGroup(g.id)}
                    disabled={isSubmitting}
                  />
                  {g.name}
                </label>
              ))
            )}
          </div>
          {fieldErrors.targets ? (
            <p className="text-danger" data-testid="error-targets">{fieldErrors.targets}</p>
          ) : null}
        </div>

        <div className="form-group" style={{ marginBottom: 0 }}>
          <label>Lịch</label>
          <div style={{ display: 'flex', gap: 16, marginTop: 8 }}>
            <label style={{ display: 'flex', alignItems: 'center', gap: 6, fontWeight: 'normal' }}>
              <input
                type="radio"
                name="schedule-mode"
                checked={form.scheduleMode === CAMPAIGN_SCHEDULE_MODE.AllWeek}
                onChange={() => setField('scheduleMode', CAMPAIGN_SCHEDULE_MODE.AllWeek)}
                disabled={isSubmitting}
              />
              Cả tuần
            </label>
            <label style={{ display: 'flex', alignItems: 'center', gap: 6, fontWeight: 'normal' }}>
              <input
                type="radio"
                name="schedule-mode"
                checked={form.scheduleMode === CAMPAIGN_SCHEDULE_MODE.ByWeekday}
                onChange={() => setField('scheduleMode', CAMPAIGN_SCHEDULE_MODE.ByWeekday)}
                disabled={isSubmitting}
              />
              Theo thứ
            </label>
          </div>
        </div>

        {form.scheduleMode === CAMPAIGN_SCHEDULE_MODE.ByWeekday ? (
          <div className="form-group" style={{ marginBottom: 0 }} data-testid="weekday-picker">
            <label>Chọn thứ</label>
            <div style={{ display: 'flex', flexWrap: 'wrap', gap: 10, marginTop: 8 }}>
              {WEEKDAY_OPTIONS.map((d) => (
                <label
                  key={d.value}
                  style={{ display: 'flex', alignItems: 'center', gap: 4, fontWeight: 'normal' }}
                >
                  <input
                    type="checkbox"
                    checked={form.weekdays.includes(d.value)}
                    onChange={() => toggleWeekday(d.value)}
                    disabled={isSubmitting}
                  />
                  {d.label}
                </label>
              ))}
            </div>
            {fieldErrors.weekdays ? (
              <p className="text-danger" data-testid="error-weekdays">{fieldErrors.weekdays}</p>
            ) : null}
          </div>
        ) : null}

        <div className="form-group" style={{ marginBottom: 0 }} data-testid="publish-times">
          <label>Giờ đăng (HH:mm)</label>
          <div style={{ display: 'flex', gap: 8, marginTop: 8 }}>
            <input
              value={form.timeDraft}
              onChange={(e) => setField('timeDraft', e.target.value)}
              placeholder="09:00"
              disabled={isSubmitting}
              data-testid="time-draft"
            />
            <button
              type="button"
              className="btn btn-secondary"
              onClick={handleAddTime}
              disabled={isSubmitting}
              data-testid="add-time"
            >
              Thêm
            </button>
          </div>
          <ul style={{ listStyle: 'none', padding: 0, margin: '8px 0 0' }}>
            {form.publishTimes.map((t) => (
              <li
                key={t}
                style={{ display: 'flex', alignItems: 'center', gap: 8, marginBottom: 4 }}
              >
                <span>{t}</span>
                <button
                  type="button"
                  className="btn btn-ghost btn-sm"
                  onClick={() => removeTime(t)}
                  disabled={isSubmitting}
                  data-testid={`remove-time-${t}`}
                >
                  Xoá
                </button>
              </li>
            ))}
          </ul>
          {fieldErrors.publishTimes ? (
            <p className="text-danger" data-testid="error-publishTimes">{fieldErrors.publishTimes}</p>
          ) : null}
        </div>

        <div className="form-group" style={{ marginBottom: 0 }}>
          <label htmlFor="campaign-jitter">Lệch ± phút (0–240)</label>
          <input
            id="campaign-jitter"
            type="number"
            min={0}
            max={240}
            value={form.jitterMinutes}
            onChange={(e) => setField('jitterMinutes', e.target.value)}
            disabled={isSubmitting}
          />
          {fieldErrors.jitterMinutes ? (
            <p className="text-danger" data-testid="error-jitter">{fieldErrors.jitterMinutes}</p>
          ) : null}
        </div>

        <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 12 }}>
          <div className="form-group" style={{ marginBottom: 0 }}>
            <label htmlFor="campaign-start">Ngày bắt đầu</label>
            <input
              id="campaign-start"
              type="date"
              value={form.startDate}
              onChange={(e) => setField('startDate', e.target.value)}
              disabled={isSubmitting}
            />
            {fieldErrors.startDate ? (
              <p className="text-danger">{fieldErrors.startDate}</p>
            ) : null}
          </div>
          <div className="form-group" style={{ marginBottom: 0 }}>
            <label htmlFor="campaign-end">Ngày kết thúc (tuỳ chọn)</label>
            <input
              id="campaign-end"
              type="date"
              value={form.endDate}
              onChange={(e) => setField('endDate', e.target.value)}
              disabled={isSubmitting}
            />
            {fieldErrors.endDate ? (
              <p className="text-danger" data-testid="error-endDate">{fieldErrors.endDate}</p>
            ) : null}
          </div>
        </div>
      </div>
    </Modal>
  )
}
