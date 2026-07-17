export const CONFIRM_MESSAGES = {
  deleteChannel: (name) =>
    `Ngắt kết nối kênh "${name}"? Hành động này không thể hoàn tác.`,
  disconnectAccount: (name) =>
    `Ngắt kết nối tài khoản "${name}"? Các kênh thuộc tài khoản sẽ bị tắt.`,
  deletePost: (title) => `Xóa bài viết "${title}"? Hành động này không thể hoàn tác.`,
  deleteMedia: (name) => `Xóa media "${name}"? Hành động này không thể hoàn tác.`,
  detachMedia: () => 'Gỡ media khỏi bài viết?',
  cancelJob: (id) => `Hủy job ${String(id).slice(0, 8)}…?`,
  processJob: (id) => `Chạy process job ${String(id).slice(0, 8)}…?`,
  retryJob: (id) => `Retry job ${String(id).slice(0, 8)}…?`,
  publishNow: () => 'Đăng bài ngay bây giờ?',
  cancelSchedule: () => 'Hủy lịch đăng bài?',
  submitReview: () => 'Gửi bài viết để duyệt?',
  approvePost: () => 'Duyệt bài viết này?',
}

export function confirmAction(message) {
  return window.confirm(message)
}
