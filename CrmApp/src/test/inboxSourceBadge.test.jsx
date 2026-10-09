import React from 'react'
import { describe, it, expect } from 'vitest'
import { render, screen } from '@testing-library/react'
import { InboxList } from '../features/inbox/components/InboxList'
import { resolveInboxSource } from '../features/inbox/components/SourceBadge'

const items = [
  { id: 'm1', kind: 1, platform: 1, displayName: 'Khách Messenger', status: 1 },
  { id: 'c1', kind: 2, platform: 1, displayName: 'Khách Facebook', status: 1 },
  { id: 'i1', kind: 1, platform: 3, displayName: 'Khách Instagram', status: 1 },
]

describe('Badge nguồn trên avatar danh sách hội thoại', () => {
  it('xác định nguồn: tin nhắn FB = Messenger, bình luận FB = Facebook, kênh IG = Instagram', () => {
    expect(resolveInboxSource({ kind: 1, platform: 1 })).toBe('messenger')
    expect(resolveInboxSource({ kind: 2, platform: 1 })).toBe('facebook')
    expect(resolveInboxSource({ kind: 1, platform: 3 })).toBe('instagram')
    expect(resolveInboxSource({ kind: 2, platform: 3 })).toBe('instagram')
    // Dữ liệu cũ chưa có platform: suy theo loại.
    expect(resolveInboxSource({ kind: 1 })).toBe('messenger')
    expect(resolveInboxSource({ kind: 2 })).toBe('facebook')
  })

  it('mỗi item có badge nằm trong avatar, có nhãn đọc được', () => {
    const { container } = render(<InboxList items={items} />)
    const expected = { m1: 'Messenger', c1: 'Facebook', i1: 'Instagram' }
    for (const [id, label] of Object.entries(expected)) {
      const badge = screen.getByTestId(`conv-source-${id}`)
      expect(badge).toHaveAttribute('aria-label', label)
      expect(badge.closest('.crm-conv-avatar')).not.toBeNull()
    }
    // id gradient không trùng giữa các item.
    const gradientIds = [...container.querySelectorAll('linearGradient')].map((g) => g.id)
    expect(new Set(gradientIds).size).toBe(gradientIds.length)
  })
})
