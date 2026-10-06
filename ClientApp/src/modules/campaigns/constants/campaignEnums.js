/** Khớp backend CampaignEnums. */

export const CAMPAIGN_MEDIA_TYPE = {
  Image: 1,
  Video: 2,
}

export const CAMPAIGN_IMAGE_STRATEGY = {
  KeepOld: 1,
  VectorSearch: 2,
}

export const CAMPAIGN_SCHEDULE_MODE = {
  ByWeekday: 1,
  AllWeek: 2,
}

export const CAMPAIGN_STATUS = {
  Running: 1,
  Paused: 2,
  Ended: 3,
}

export const WEEKDAY_OPTIONS = [
  { value: 1, label: 'Thứ 2' },
  { value: 2, label: 'Thứ 3' },
  { value: 3, label: 'Thứ 4' },
  { value: 4, label: 'Thứ 5' },
  { value: 5, label: 'Thứ 6' },
  { value: 6, label: 'Thứ 7' },
  { value: 7, label: 'Chủ nhật' },
]

export function mediaTypeLabel(value) {
  if (value === CAMPAIGN_MEDIA_TYPE.Video) return 'Video'
  return 'Ảnh'
}

export function scheduleModeLabel(value) {
  if (value === CAMPAIGN_SCHEDULE_MODE.ByWeekday) return 'Theo thứ'
  return 'Cả tuần'
}

export function statusLabel(value) {
  if (value === CAMPAIGN_STATUS.Paused) return 'Tạm dừng'
  if (value === CAMPAIGN_STATUS.Ended) return 'Đã kết thúc'
  return 'Đang chạy'
}
