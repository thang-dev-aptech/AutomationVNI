export const MEDIA_SOURCE = {
  1: { label: 'Upload', tone: 'neutral' },
  2: { label: 'AI Generated', tone: 'info' },
  3: { label: 'Overlay', tone: 'success' },
}

export const MEDIA_SOURCE_OPTIONS = Object.entries(MEDIA_SOURCE).map(([value, meta]) => ({
  value: Number(value),
  label: meta.label,
}))

export const MEDIA_ROLE = {
  1: { value: 1, label: 'Cover (Primary)', apiName: 'Primary' },
  2: { value: 2, label: 'Thumbnail', apiName: 'Thumbnail' },
  3: { value: 3, label: 'Attachment', apiName: 'Attachment' },
}

export const COVER_ROLE = 1

export const IMAGE_MIME_PREFIX = 'image/'
export const MAX_UPLOAD_SIZE_MB = 10
export const MAX_UPLOAD_SIZE_BYTES = MAX_UPLOAD_SIZE_MB * 1024 * 1024

export function getMediaSourceMeta(source) {
  return MEDIA_SOURCE[source] ?? { label: `Source ${source}`, tone: 'neutral' }
}

export function getMediaRoleLabel(role) {
  return MEDIA_ROLE[role]?.label ?? `Role ${role}`
}

export function isImageMime(mimeType) {
  return mimeType?.startsWith(IMAGE_MIME_PREFIX)
}

export function guessMimeFromUrl(url) {
  const lower = url.toLowerCase()
  if (lower.endsWith('.png')) return 'image/png'
  if (lower.endsWith('.gif')) return 'image/gif'
  if (lower.endsWith('.webp')) return 'image/webp'
  if (lower.endsWith('.svg')) return 'image/svg+xml'
  return 'image/jpeg'
}

export function fileNameFromUrl(url) {
  try {
    const pathname = new URL(url).pathname
    const name = pathname.split('/').filter(Boolean).pop()
    return name || `media-${Date.now()}`
  } catch {
    return `media-${Date.now()}`
  }
}
