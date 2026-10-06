/**
 * Ánh xạ nhãn sidebar → PostStatus (R-033).
 * Mặc định bật: Chờ đăng + Thành công + Thất bại.
 */

export const CALENDAR_VIEWS = {
  calendar: 'calendar',
  list: 'list',
  byChannel: 'by-channel',
}

export const CALENDAR_VIEW_OPTIONS = [
  { key: CALENDAR_VIEWS.calendar, label: 'Lịch' },
  { key: CALENDAR_VIEWS.list, label: 'Danh sách' },
  { key: CALENDAR_VIEWS.byChannel, label: 'Theo kênh' },
]

export const CHANNEL_FILTER_MODES = {
  channel: 'channel',
  group: 'group',
}

/** Loại bài từ backend PostTypeClassifier — frontend không suy ra. */
export const CALENDAR_POST_TYPES = [
  'Tin',
  'Video ngắn',
  'Video',
  'Ảnh',
  'Văn bản',
]

/**
 * Mỗi nhóm = một checkbox sidebar; `statuses` = OR trong nhóm.
 * `color` dùng cho chấm/icon màu cạnh nhãn.
 */
export const CALENDAR_STATUS_GROUPS = [
  { key: 'draft', label: 'Nháp', statuses: [1], color: '#94a3b8', defaultOn: false },
  {
    key: 'generating',
    label: 'Đang tạo nội dung',
    statuses: [2, 3, 12, 14],
    color: '#38bdf8',
    defaultOn: false,
  },
  { key: 'need-media', label: 'Cần media', statuses: [13], color: '#f59e0b', defaultOn: false },
  { key: 'waiting-review', label: 'Chờ duyệt', statuses: [10], color: '#a78bfa', defaultOn: false },
  { key: 'need-fix', label: 'Từ chối', statuses: [15], color: '#f97316', defaultOn: false },
  {
    key: 'ready',
    label: 'Chờ xuất bản',
    statuses: [4, 11],
    color: '#34d399',
    defaultOn: false,
  },
  { key: 'scheduled', label: 'Chờ đăng', statuses: [5], color: '#3b82f6', defaultOn: true },
  { key: 'publishing', label: 'Đang đăng', statuses: [6], color: '#6366f1', defaultOn: false },
  { key: 'published', label: 'Thành công', statuses: [7], color: '#22c55e', defaultOn: true },
  { key: 'failed', label: 'Thất bại', statuses: [8], color: '#ef4444', defaultOn: true },
  { key: 'cancelled', label: 'Huỷ đăng', statuses: [9], color: '#64748b', defaultOn: false },
]

export const DEFAULT_STATUS_KEYS = CALENDAR_STATUS_GROUPS
  .filter((g) => g.defaultOn)
  .map((g) => g.key)

export function expandStatusKeys(keys) {
  const set = new Set(keys ?? [])
  return CALENDAR_STATUS_GROUPS
    .filter((g) => set.has(g.key))
    .flatMap((g) => g.statuses)
}

export function getStatusGroupByKey(key) {
  return CALENDAR_STATUS_GROUPS.find((g) => g.key === key) ?? null
}
