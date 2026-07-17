export const JOB_STATUS = {
  1: { label: 'Pending', tone: 'neutral' },
  2: { label: 'Running', tone: 'info' },
  3: { label: 'Succeeded', tone: 'success' },
  4: { label: 'Failed', tone: 'danger' },
  5: { label: 'Retrying', tone: 'warning' },
  6: { label: 'Cancelled', tone: 'neutral' },
  7: { label: 'Dead letter', tone: 'danger' },
}

export const JOB_STATUS_OPTIONS = Object.entries(JOB_STATUS).map(([value, meta]) => ({
  value: Number(value),
  label: meta.label,
}))

export const JOB_TYPE = {
  1: { label: 'Text Generation', tone: 'info' },
  2: { label: 'Image Generation', tone: 'info' },
  3: { label: 'Image Overlay', tone: 'warning' },
  4: { label: 'Media Match', tone: 'warning' },
  5: { label: 'Publish', tone: 'neutral' },
}

export const JOB_TYPE_OPTIONS = Object.entries(JOB_TYPE).map(([value, meta]) => ({
  value: Number(value),
  label: meta.label,
}))

export const PUBLISH_STATUS = {
  0: { label: 'Pending', tone: 'neutral' },
  1: { label: 'Success', tone: 'success' },
  2: { label: 'Failed', tone: 'danger' },
  3: { label: 'Rate limited', tone: 'warning' },
  4: { label: 'Cancelled', tone: 'neutral' },
}

export const PUBLISH_STATUS_OPTIONS = Object.entries(PUBLISH_STATUS).map(([value, meta]) => ({
  value: Number(value),
  label: meta.label,
}))

export function getJobStatusMeta(status) {
  return JOB_STATUS[status] ?? { label: `Status ${status}`, tone: 'neutral' }
}

export function getJobTypeMeta(type) {
  return JOB_TYPE[type] ?? { label: `Type ${type}`, tone: 'neutral' }
}

export function getPublishStatusMeta(status) {
  return PUBLISH_STATUS[status] ?? { label: `Status ${status}`, tone: 'neutral' }
}

export function shortId(id) {
  if (!id) return '—'
  return `${String(id).slice(0, 8)}…`
}

export function truncateText(text, maxLength = 120) {
  if (!text) return ''
  if (text.length <= maxLength) return text
  return `${text.slice(0, maxLength)}…`
}

/** Actions khả dụng theo JobStatus backend */
export function getGenerationJobActions(status) {
  return {
    canProcess: status === 1 || status === 5,
    canRetry: status === 4 || status === 7,
    canCancel: status === 1 || status === 2 || status === 5,
    hasError: Boolean(status === 4 || status === 7),
  }
}
