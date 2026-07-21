export const COMMENT_INBOX_STATUS = {
  1: { label: 'Mới', tone: 'info' },
  2: { label: 'Đang xử lý', tone: 'warning' },
  3: { label: 'Đã trả lời', tone: 'success' },
  4: { label: 'Bỏ qua', tone: 'neutral' },
  5: { label: 'Đã xóa', tone: 'danger' },
}

export const COMMENT_PLATFORM = {
  1: 'Facebook',
  5: 'Threads',
}

export function getInboxStatusMeta(status) {
  return COMMENT_INBOX_STATUS[status] || { label: `Status ${status}`, tone: 'neutral' }
}

export function getPlatformLabel(platform) {
  return COMMENT_PLATFORM[platform] || `Platform ${platform}`
}

export const COMMENT_ACTION_TYPE = {
  1: 'Trả lời',
  2: 'Ẩn',
  3: 'Hiện',
  4: 'Xóa',
  5: 'Duyệt pending',
  6: 'Bỏ qua pending',
  7: 'Gán',
  8: 'Đổi trạng thái',
  9: 'Ghi chú',
}

export function getActionTypeLabel(type) {
  return COMMENT_ACTION_TYPE[type] || `Action ${type}`
}

export function truncate(text, max = 120) {
  if (!text) return '—'
  const value = String(text).trim()
  return value.length <= max ? value : `${value.slice(0, max)}…`
}
