/** Backend SocialPlatform enum */
export const SOCIAL_PLATFORMS = {
  1: { value: 1, label: 'Facebook' },
  2: { value: 2, label: 'LinkedIn' },
  3: { value: 3, label: 'Instagram' },
  4: { value: 4, label: 'TikTok' },
}

export const SOCIAL_PLATFORM_OPTIONS = Object.values(SOCIAL_PLATFORMS)

export function getSocialPlatformLabel(value) {
  return SOCIAL_PLATFORMS[value]?.label ?? `Platform ${value}`
}

/** Backend SocialChannelType enum */
export const CHANNEL_TYPES = {
  1: { value: 1, label: 'Page', key: 'page' },
  2: { value: 2, label: 'Instagram', key: 'instagram' },
  3: { value: 3, label: 'Group', key: 'group' },
}

export function getChannelTypeLabel(value) {
  return CHANNEL_TYPES[value]?.label ?? `Type ${value}`
}

/** Backend SocialProvider enum */
export const SOCIAL_PROVIDERS = {
  1: { value: 1, label: 'Meta', key: 'meta' },
  2: { value: 2, label: 'LinkedIn', key: 'linkedin' },
  3: { value: 3, label: 'Threads', key: 'threads' },
}

export function getProviderLabel(value) {
  return SOCIAL_PROVIDERS[value]?.label ?? `Provider ${value}`
}

/**
 * Connectable providers catalog — add LinkedIn/Threads here when ready.
 * connectAction: 'meta' | null (coming soon)
 */
export const PROVIDER_CATALOG = [
  {
    id: 'meta',
    label: 'Meta (Facebook / Instagram / Groups)',
    description: 'Login một tài khoản Meta → đồng bộ Pages, Instagram và Groups',
    provider: 1,
    connectAction: 'meta',
    supported: true,
  },
  {
    id: 'linkedin',
    label: 'LinkedIn',
    description: 'Sắp hỗ trợ',
    provider: 2,
    connectAction: null,
    supported: false,
  },
  {
    id: 'threads',
    label: 'Threads',
    description: 'Sắp hỗ trợ',
    provider: 3,
    connectAction: null,
    supported: false,
  },
  {
    id: 'zalo',
    label: 'Zalo',
    description: 'Kết nối thủ công (token)',
    provider: null,
    connectAction: 'manual',
    supported: true,
  },
]

/** @deprecated Prefer PROVIDER_CATALOG — kept for form platform options only */
export const PLATFORM_CARDS = [
  {
    id: 'facebook',
    label: 'Facebook',
    description: 'Page Facebook đã kết nối',
    backendPlatform: 1,
    supported: true,
  },
  {
    id: 'instagram',
    label: 'Instagram',
    description: 'Tài khoản Instagram',
    backendPlatform: 3,
    supported: true,
  },
]
