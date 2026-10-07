import {
  CAMPAIGN_IMAGE_STRATEGY,
  CAMPAIGN_MEDIA_TYPE,
  CAMPAIGN_SCHEDULE_MODE,
} from '../constants/campaignEnums'

const TIME_RE = /^([01]\d|2[0-3]):([0-5]\d)$/

/** Chạy lặp đến khi người dùng tạm dừng/kết thúc — không có ngày kết thúc. */
export const RUN_MODE_UNTIL_STOPPED = 'untilStopped'
/** Có ngày kết thúc (cấu hình hiện tại). */
export const RUN_MODE_WITH_END = 'withEndDate'

export function emptyCampaignForm() {
  const today = new Date()
  const ymd = [
    today.getFullYear(),
    String(today.getMonth() + 1).padStart(2, '0'),
    String(today.getDate()).padStart(2, '0'),
  ].join('-')

  return {
    name: '',
    mediaType: CAMPAIGN_MEDIA_TYPE.Image,
    imageStrategy: CAMPAIGN_IMAGE_STRATEGY.KeepOld,
    channelIds: [],
    channelGroupIds: [],
    scheduleMode: CAMPAIGN_SCHEDULE_MODE.AllWeek,
    weekdays: [],
    publishTimes: ['09:00'],
    timeDraft: '',
    jitterMinutes: 0,
    startDate: ymd,
    endDate: '',
    runMode: RUN_MODE_UNTIL_STOPPED,
  }
}

export function formFromCampaign(campaign) {
  if (!campaign) return emptyCampaignForm()
  const endDate = campaign.endDate ? toDateInput(campaign.endDate) : ''
  return {
    name: campaign.name ?? '',
    mediaType: campaign.mediaType ?? CAMPAIGN_MEDIA_TYPE.Image,
    // UI không còn chọn — luôn dùng lại bài/ảnh gốc.
    imageStrategy: CAMPAIGN_IMAGE_STRATEGY.KeepOld,
    channelIds: [...(campaign.channelIds ?? [])],
    channelGroupIds: [...(campaign.channelGroupIds ?? [])],
    scheduleMode: campaign.scheduleMode ?? CAMPAIGN_SCHEDULE_MODE.AllWeek,
    weekdays: [...(campaign.weekdays ?? [])],
    publishTimes: [...(campaign.publishTimes?.length ? campaign.publishTimes : ['09:00'])],
    timeDraft: '',
    jitterMinutes: campaign.jitterMinutes ?? 0,
    startDate: toDateInput(campaign.startDate),
    endDate,
    runMode: endDate ? RUN_MODE_WITH_END : RUN_MODE_UNTIL_STOPPED,
  }
}

function toDateInput(value) {
  if (!value) return ''
  const d = new Date(value)
  if (Number.isNaN(d.getTime())) return String(value).slice(0, 10)
  return [
    d.getUTCFullYear(),
    String(d.getUTCMonth() + 1).padStart(2, '0'),
    String(d.getUTCDate()).padStart(2, '0'),
  ].join('-')
}

export function isValidPublishTime(text) {
  return TIME_RE.test(String(text || '').trim())
}

/**
 * @returns {{ ok: true } | { ok: false, errors: Record<string, string> }}
 */
export function validateCampaignForm(form) {
  const errors = {}
  const name = String(form.name || '').trim()
  if (!name) errors.name = 'Tên chiến dịch là bắt buộc'

  const channelIds = form.channelIds ?? []
  const groupIds = form.channelGroupIds ?? []
  if (channelIds.length === 0 && groupIds.length === 0) {
    errors.targets = 'Chọn ít nhất một kênh hoặc nhóm kênh'
  }

  if (Number(form.scheduleMode) === CAMPAIGN_SCHEDULE_MODE.ByWeekday) {
    if (!form.weekdays?.length) {
      errors.weekdays = 'Chọn ít nhất một thứ trong tuần'
    }
  }

  const times = (form.publishTimes ?? []).map((t) => String(t).trim()).filter(Boolean)
  if (times.length === 0) {
    errors.publishTimes = 'Thêm ít nhất một giờ đăng (HH:mm)'
  } else {
    const bad = times.find((t) => !isValidPublishTime(t))
    if (bad) errors.publishTimes = `Giờ không hợp lệ: ${bad}`
    const unique = new Set(times)
    if (unique.size !== times.length) {
      errors.publishTimes = 'Không được trùng giờ đăng'
    }
  }

  const jitter = Number(form.jitterMinutes)
  if (!Number.isFinite(jitter) || jitter < 0 || jitter > 240) {
    errors.jitterMinutes = 'Lệch phút phải từ 0 đến 240'
  }

  if (!form.startDate) {
    errors.startDate = 'Ngày bắt đầu là bắt buộc'
  }

  const withEnd = form.runMode === RUN_MODE_WITH_END
  if (withEnd) {
    if (!form.endDate) {
      errors.endDate = 'Chọn ngày kết thúc hoặc chuyển sang chạy đến khi dừng'
    } else if (form.startDate && form.endDate < form.startDate) {
      errors.endDate = 'Ngày kết thúc phải ≥ ngày bắt đầu'
    }
  }

  return Object.keys(errors).length ? { ok: false, errors } : { ok: true }
}

export function buildCampaignPayload(form) {
  const mediaType = Number(form.mediaType)
  const scheduleMode = Number(form.scheduleMode)
  const withEnd = form.runMode === RUN_MODE_WITH_END
  return {
    name: String(form.name || '').trim(),
    mediaType,
    // Mặc định dùng lại bài/ảnh gốc — không còn chọn trên UI.
    imageStrategy: mediaType === CAMPAIGN_MEDIA_TYPE.Image
      ? CAMPAIGN_IMAGE_STRATEGY.KeepOld
      : null,
    channelIds: [...(form.channelIds ?? [])],
    channelGroupIds: [...(form.channelGroupIds ?? [])],
    scheduleMode,
    weekdays: scheduleMode === CAMPAIGN_SCHEDULE_MODE.ByWeekday
      ? [...(form.weekdays ?? [])].sort((a, b) => a - b)
      : [],
    publishTimes: (form.publishTimes ?? [])
      .map((t) => String(t).trim())
      .filter(Boolean),
    jitterMinutes: Number(form.jitterMinutes) || 0,
    startDate: `${form.startDate}T00:00:00.000Z`,
    endDate: withEnd && form.endDate ? `${form.endDate}T00:00:00.000Z` : null,
  }
}

export function tryAddPublishTime(times, draft) {
  const value = String(draft || '').trim()
  if (!isValidPublishTime(value)) {
    return { ok: false, error: 'Giờ phải dạng HH:mm (ví dụ 09:00)' }
  }
  if ((times ?? []).includes(value)) {
    return { ok: false, error: 'Giờ này đã có trong danh sách' }
  }
  return { ok: true, times: [...(times ?? []), value] }
}
