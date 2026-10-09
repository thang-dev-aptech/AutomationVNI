import React from 'react'
import { PlatformLogo, PLATFORM_LABELS } from './PlatformLogo'

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

/**
 * Badge tròn nhỏ cho biết nguồn hội thoại.
 * variant="inline" (mặc định): nằm trong dòng, dùng ở bảng/thẻ/drawer cơ hội.
 * variant="overlay": absolute ở góc dưới bên phải avatar (cha phải position:relative) — danh sách inbox.
 */
export function SourceBadge({ item, variant = 'inline' }) {
  const source = resolveInboxSource(item)
  const label = PLATFORM_LABELS[source]

  return (
    <span
      className={`crm-conv-source-badge crm-conv-source-${source}${variant === 'overlay' ? ' crm-conv-source-badge--overlay' : ''}`}
      data-testid={`conv-source-${item.id}`}
      data-source={source}
      title={label}
      aria-label={label}
      role="img"
    >
      <PlatformLogo name={source} />
    </span>
  )
}

export default SourceBadge
