export const JOB_STATUS_LABEL = {
  Queued: 'Đang chờ',
  Running: 'Đang chạy',
  Completed: 'Hoàn tất',
}

export const ITEM_STATUS_META = {
  Pending: { label: 'Chờ', tone: 'neutral' },
  Running: { label: 'Đang sinh', tone: 'info' },
  Succeeded: { label: 'Xong', tone: 'success' },
  Failed: { label: 'Lỗi', tone: 'danger' },
  Skipped: { label: 'Bỏ qua', tone: 'warning' },
}
