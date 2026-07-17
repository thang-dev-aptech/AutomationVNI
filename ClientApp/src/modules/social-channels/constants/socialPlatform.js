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

/**
 * Platform cards UI — hard-code tạm, chưa có Platform API riêng.
 * backendPlatform: map sang SocialPlatform enum nếu đã hỗ trợ backend.
 */
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
  {
    id: 'zalo',
    label: 'Zalo',
    description: 'Official Account Zalo',
    backendPlatform: null,
    supported: false,
  },
  {
    id: 'website',
    label: 'Website/Blog',
    description: 'Kênh blog hoặc website',
    backendPlatform: null,
    supported: false,
  },
]
