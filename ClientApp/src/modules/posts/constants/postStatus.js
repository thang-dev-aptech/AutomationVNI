export const POST_STATUS = {
  1: { label: 'Nháp', tone: 'neutral' },
  2: { label: 'Đã xếp hàng', tone: 'neutral' },
  3: { label: 'Đang sinh', tone: 'info' },
  4: { label: 'Sẵn sàng', tone: 'success' },
  5: { label: 'Đã lên lịch', tone: 'warning' },
  6: { label: 'Đang đăng', tone: 'info' },
  7: { label: 'Đã đăng', tone: 'success' },
  8: { label: 'Thất bại', tone: 'danger' },
  9: { label: 'Đã hủy', tone: 'neutral' },
  10: { label: 'Chờ duyệt', tone: 'warning' },
  11: { label: 'Đã duyệt', tone: 'success' },
}

export const GENERATION_FLOW = {
  1: { value: 1, label: 'Full AI (Text + Image)' },
  2: { value: 2, label: 'RAG (Text + Media nội bộ)' },
}

export const GENERATION_FLOW_OPTIONS = Object.values(GENERATION_FLOW)

export const JOB_TYPE = {
  1: 'Sinh Text',
  2: 'Sinh Image',
  3: 'Overlay Logo/CTA',
  4: 'RAG Media Match',
  5: 'Publish',
}

export const JOB_STATUS = {
  1: { label: 'Chờ xử lý', tone: 'neutral' },
  2: { label: 'Đang xử lý', tone: 'info' },
  3: { label: 'Hoàn thành', tone: 'success' },
  4: { label: 'Thất bại', tone: 'danger' },
  5: { label: 'Retry', tone: 'warning' },
  6: { label: 'Đã hủy', tone: 'neutral' },
  7: { label: 'Dead letter', tone: 'danger' },
}

export function getPostStatusMeta(status) {
  return POST_STATUS[status] ?? { label: `Status ${status}`, tone: 'neutral' }
}

export function getGenerationFlowLabel(flow) {
  return GENERATION_FLOW[flow]?.label ?? `Flow ${flow}`
}

export function getJobTypeLabel(type) {
  return JOB_TYPE[type] ?? `Job ${type}`
}

export function getJobStatusMeta(status) {
  return JOB_STATUS[status] ?? { label: `Status ${status}`, tone: 'neutral' }
}

/** Actions hiển thị theo PostStatus backend */
export function getAvailableWorkflowActions(status) {
  return {
    submitReview: status === 1 || status === 4,
    approve: status === 10,
    reject: status === 10,
    schedule: status === 11,
    cancelSchedule: status === 5,
    publishNow: status === 11 || status === 5,
    canDelete: status !== 6 && status !== 7,
    canEditContent: status !== 6 && status !== 7,
  }
}
