export function unwrapApiData(response) {
  const payload = response?.data
  if (payload && typeof payload.success === 'boolean') {
    if (!payload.success) {
      const error = new Error(payload.message || 'Yêu cầu thất bại')
      error.errorCode = payload.errorCode
      throw error
    }
    return payload.data
  }
  return payload
}

export function getErrorMessage(error, fallback = 'Đã xảy ra lỗi') {
  return (
    error?.response?.data?.message ||
    error?.message ||
    fallback
  )
}

/**
 * Backend trả DateTime UTC nhưng thiếu hậu tố 'Z' (SQLite → Kind=Unspecified).
 * Nếu là chuỗi ISO không kèm timezone, coi như UTC để hiển thị đúng giờ local.
 */
function toUtcDate(value) {
  if (typeof value === 'string'
    && /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}/.test(value)
    && !/[zZ]$|[+-]\d{2}:?\d{2}$/.test(value)) {
    return new Date(`${value}Z`)
  }
  return new Date(value)
}

export function formatDateTime(value) {
  if (!value) return '—'
  return toUtcDate(value).toLocaleString('vi-VN', {
    timeZone: 'Asia/Ho_Chi_Minh',
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
    hour: '2-digit',
    minute: '2-digit',
  })
}

export function formatFileSize(bytes) {
  if (!bytes && bytes !== 0) return '—'
  if (bytes < 1024) return `${bytes} B`
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`
  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`
}

export function slugify(text) {
  return text
    .toLowerCase()
    .normalize('NFD')
    .replace(/[\u0300-\u036f]/g, '')
    .replace(/[^a-z0-9]+/g, '-')
    .replace(/(^-|-$)/g, '')
}
