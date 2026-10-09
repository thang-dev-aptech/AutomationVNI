import React, { useId } from 'react'

// Giá trị SocialPlatform của backend (backend/Modules/SocialChannel/Enums/SocialChannelEnums.cs).
const PLATFORM_INSTAGRAM = 3

const MESSAGE_KIND = 1

/**
 * Nguồn của một item inbox:
 * - Instagram: kênh có Platform = Instagram (tin nhắn hoặc bình luận).
 * - Messenger: tin nhắn của kênh Facebook.
 * - Facebook: bình luận của kênh Facebook.
 * Chưa có Zalo vì backend chưa có SocialPlatform Zalo.
 */
export function resolveInboxSource(item) {
  if (Number(item?.platform) === PLATFORM_INSTAGRAM) return 'instagram'
  return item?.kind === MESSAGE_KIND ? 'messenger' : 'facebook'
}

const SOURCE_LABELS = {
  messenger: 'Messenger',
  facebook: 'Facebook',
  instagram: 'Instagram',
}

function MessengerIcon({ gradientId }) {
  return (
    <svg viewBox="0 0 24 24" width="100%" height="100%" aria-hidden="true">
      <defs>
        <linearGradient id={gradientId} x1="0" y1="1" x2="1" y2="0">
          <stop offset="0" stopColor="#0099FF" />
          <stop offset="0.6" stopColor="#A033FF" />
          <stop offset="1" stopColor="#FF5C87" />
        </linearGradient>
      </defs>
      <circle cx="12" cy="12" r="12" fill={`url(#${gradientId})`} />
      <path d="M5.8 14.6 9.6 10.6l2.4 2.1 4.2-2.3-3.8 4-2.4-2.1z" fill="#fff" />
    </svg>
  )
}

function FacebookIcon() {
  return (
    <svg viewBox="0 0 24 24" width="100%" height="100%" aria-hidden="true">
      <circle cx="12" cy="12" r="12" fill="#1877F2" />
      <path
        d="M13.4 19.5v-6.1h2.1l.3-2.5h-2.4V9.4c0-.7.2-1.2 1.2-1.2h1.3V6c-.2 0-1-.1-1.9-.1-1.9 0-3.1 1.1-3.1 3.2v1.8H8.8v2.5h2.1v6.1z"
        fill="#fff"
      />
    </svg>
  )
}

function InstagramIcon({ gradientId }) {
  return (
    <svg viewBox="0 0 24 24" width="100%" height="100%" aria-hidden="true">
      <defs>
        <linearGradient id={gradientId} x1="0" y1="1" x2="1" y2="0">
          <stop offset="0" stopColor="#FEDA75" />
          <stop offset="0.35" stopColor="#FA7E1E" />
          <stop offset="0.65" stopColor="#D62976" />
          <stop offset="1" stopColor="#4F5BD5" />
        </linearGradient>
      </defs>
      <circle cx="12" cy="12" r="12" fill={`url(#${gradientId})`} />
      <rect x="6.5" y="6.5" width="11" height="11" rx="3.2" fill="none" stroke="#fff" strokeWidth="1.6" />
      <circle cx="12" cy="12" r="2.6" fill="none" stroke="#fff" strokeWidth="1.6" />
      <circle cx="15.3" cy="8.7" r="0.9" fill="#fff" />
    </svg>
  )
}

/**
 * Badge tròn nhỏ cho biết nguồn hội thoại.
 * variant="inline" (mặc định): nằm trong dòng, dùng ở bảng/thẻ/drawer cơ hội.
 * variant="overlay": absolute ở góc dưới bên phải avatar (cha phải position:relative) — danh sách inbox.
 */
export function SourceBadge({ item, variant = 'inline' }) {
  // id gradient phải duy nhất trên trang: mỗi item một <defs> riêng.
  const gradientId = `src-grad-${useId().replace(/:/g, '')}`
  const source = resolveInboxSource(item)
  const label = SOURCE_LABELS[source]

  return (
    <span
      className={`crm-conv-source-badge crm-conv-source-${source}${variant === 'overlay' ? ' crm-conv-source-badge--overlay' : ''}`}
      data-testid={`conv-source-${item.id}`}
      data-source={source}
      title={label}
      aria-label={label}
      role="img"
    >
      {source === 'messenger' && <MessengerIcon gradientId={gradientId} />}
      {source === 'facebook' && <FacebookIcon />}
      {source === 'instagram' && <InstagramIcon gradientId={gradientId} />}
    </span>
  )
}

export default SourceBadge
