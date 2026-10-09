import React, { useId } from 'react'

// SocialPlatform của backend (backend/Modules/SocialChannel/Enums/SocialChannelEnums.cs).
export const PLATFORM = { Facebook: 1, LinkedIn: 2, Instagram: 3, TikTok: 4, Threads: 5 }

const PLATFORM_KEYS = {
  [PLATFORM.Facebook]: 'facebook',
  [PLATFORM.LinkedIn]: 'linkedin',
  [PLATFORM.Instagram]: 'instagram',
  [PLATFORM.TikTok]: 'tiktok',
  [PLATFORM.Threads]: 'threads',
}

export const PLATFORM_LABELS = {
  messenger: 'Messenger',
  facebook: 'Facebook',
  instagram: 'Instagram',
  linkedin: 'LinkedIn',
  tiktok: 'TikTok',
  threads: 'Threads',
  generic: 'Mạng xã hội',
}

/** Giá trị SocialPlatform → key logo; giá trị lạ → 'generic'. */
export function platformKeyFromEnum(platform) {
  return PLATFORM_KEYS[Number(platform)] || 'generic'
}

const Circle = ({ fill }) => <circle cx="12" cy="12" r="12" fill={fill} />

function Gradient({ id, stops }) {
  return (
    <defs>
      <linearGradient id={id} x1="0" y1="1" x2="1" y2="0">
        {stops.map(([offset, color]) => (
          <stop key={offset} offset={offset} stopColor={color} />
        ))}
      </linearGradient>
    </defs>
  )
}

/**
 * Logo SVG dùng chung cho SourceBadge (inbox/cơ hội) và Kênh liên lạc (CustomerPanel).
 * `name`: messenger | facebook | instagram | linkedin | tiktok | threads | generic.
 * Thêm nền tảng mới chỉ cần thêm một nhánh ở đây (+ khoá trong PLATFORM_KEYS/registry backend).
 */
export function PlatformLogo({ name = 'generic' }) {
  const gid = `plg-${useId().replace(/:/g, '')}`
  const key = PLATFORM_LABELS[name] ? name : 'generic'
  let body
  switch (key) {
    case 'messenger':
      body = (
        <>
          <Gradient id={gid} stops={[['0', '#0099FF'], ['0.6', '#A033FF'], ['1', '#FF5C87']]} />
          <Circle fill={`url(#${gid})`} />
          <path d="M5.8 14.6 9.6 10.6l2.4 2.1 4.2-2.3-3.8 4-2.4-2.1z" fill="#fff" />
        </>
      )
      break
    case 'facebook':
      body = (
        <>
          <Circle fill="#1877F2" />
          <path
            d="M13.4 19.5v-6.1h2.1l.3-2.5h-2.4V9.4c0-.7.2-1.2 1.2-1.2h1.3V6c-.2 0-1-.1-1.9-.1-1.9 0-3.1 1.1-3.1 3.2v1.8H8.8v2.5h2.1v6.1z"
            fill="#fff"
          />
        </>
      )
      break
    case 'instagram':
      body = (
        <>
          <Gradient id={gid} stops={[['0', '#FEDA75'], ['0.35', '#FA7E1E'], ['0.65', '#D62976'], ['1', '#4F5BD5']]} />
          <Circle fill={`url(#${gid})`} />
          <rect x="6.5" y="6.5" width="11" height="11" rx="3.2" fill="none" stroke="#fff" strokeWidth="1.6" />
          <circle cx="12" cy="12" r="2.6" fill="none" stroke="#fff" strokeWidth="1.6" />
          <circle cx="15.3" cy="8.7" r="0.9" fill="#fff" />
        </>
      )
      break
    case 'linkedin':
      body = (
        <>
          <Circle fill="#0A66C2" />
          <rect x="6.4" y="9.6" width="2.4" height="7.6" fill="#fff" />
          <circle cx="7.6" cy="7.2" r="1.4" fill="#fff" />
          <path d="M10.6 9.6h2.3v1.1c.4-.7 1.3-1.3 2.5-1.3 2.3 0 2.9 1.5 2.9 3.5v4.3h-2.4v-3.8c0-.9 0-2-1.2-2s-1.4.9-1.4 1.9v3.9h-2.4z" fill="#fff" />
        </>
      )
      break
    case 'tiktok':
      body = (
        <>
          <Circle fill="#000" />
          <path d="M13.2 5.5h2.1c.2 1.5 1.1 2.5 2.7 2.7v2.1c-1 0-1.9-.3-2.7-.9v4.6a4 4 0 1 1-4-4c.2 0 .4 0 .6.1v2.2a1.8 1.8 0 1 0 1.3 1.7z" fill="#fff" />
        </>
      )
      break
    case 'threads':
      body = (
        <>
          <Circle fill="#000" />
          <path
            d="M15.9 11.4c-.3-2.4-1.8-3.6-4-3.6-2.5 0-3.9 1.8-3.9 4.4s1.4 4.2 3.9 4.2c2.1 0 3.4-1.1 3.4-2.5 0-1.3-1-2-2.4-2-1.4 0-2.2.7-2.2 1.5 0 .7.6 1.1 1.3 1.1 1.2 0 1.8-.9 1.8-2.6"
            fill="none"
            stroke="#fff"
            strokeWidth="1.4"
            strokeLinecap="round"
          />
        </>
      )
      break
    default:
      body = (
        <>
          <Circle fill="#64748B" />
          <circle cx="12" cy="12" r="5.2" fill="none" stroke="#fff" strokeWidth="1.4" />
          <path d="M6.8 12h10.4M12 6.8c1.8 1.6 1.8 8.8 0 10.4M12 6.8c-1.8 1.6-1.8 8.8 0 10.4" fill="none" stroke="#fff" strokeWidth="1.2" />
        </>
      )
  }

  return (
    <svg
      viewBox="0 0 24 24"
      width="100%"
      height="100%"
      aria-hidden="true"
      data-testid={`platform-logo-${key}`}
    >
      {body}
    </svg>
  )
}

export default PlatformLogo
