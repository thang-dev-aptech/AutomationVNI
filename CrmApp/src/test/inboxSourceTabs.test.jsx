import React from 'react'
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { render, screen, fireEvent, waitFor } from '@testing-library/react'
import { MemoryRouter, useLocation } from 'react-router-dom'
import InboxSourceTabs from '../features/inbox/components/InboxSourceTabs'
import InboxFeature from '../features/inbox/InboxFeature'
import { inboxApi } from '../features/inbox/api/inboxApi'
import { useAuthStore } from '../auth/authStore'
import { INBOX_SOURCES, resolveInboxSource } from '../features/inbox/components/SourceBadge'

describe('AC ui02-source-switcher-test (e)(f) — Thanh chọn nguồn hộp thư (InboxSourceTabs)', () => {
  const mockSources = [
    { key: 'messenger', label: 'Messenger', platform: 1, unread: 3 },
    { key: 'facebook', label: 'Facebook', platform: 1, unread: 0 },
    { key: 'instagram', label: 'Instagram', platform: 3, unread: 2 },
    { key: 'zalo-test', label: 'Zalo Test', platform: 99, unread: 1 },
  ]

  describe('Unit tests cho InboxSourceTabs', () => {
    it('render "Tất cả" và các chip theo dữ liệu nguồn, badge chưa đọc và logo', () => {
      const onSelect = vi.fn()
      render(
        <InboxSourceTabs
          sources={mockSources}
          selectedSource={null}
          onSelectSource={onSelect}
        />,
      )

      // Tab "Tất cả" luôn có, active khi selectedSource = null
      const allTab = screen.getByTestId('source-tab-all')
      expect(allTab).toBeInTheDocument()
      expect(allTab).toHaveAttribute('aria-pressed', 'true')
      expect(allTab).toHaveAttribute('aria-selected', 'true')
      // Badge "Tất cả" = tổng chưa đọc: 3 + 0 + 2 + 1 = 6
      expect(screen.getByTestId('source-unread-all')).toHaveTextContent('6')

      // Các nguồn có logo tương ứng
      const messengerTab = screen.getByTestId('source-tab-messenger')
      expect(messengerTab).toBeInTheDocument()
      expect(messengerTab).toHaveAttribute('aria-pressed', 'false')
      expect(screen.getByTestId('platform-logo-messenger')).toBeInTheDocument()
      expect(screen.getByTestId('source-unread-messenger')).toHaveTextContent('3')

      const fbTab = screen.getByTestId('source-tab-facebook')
      expect(fbTab).toBeInTheDocument()
      expect(screen.getByTestId('platform-logo-facebook')).toBeInTheDocument()
      // unread = 0 thì không hiện badge
      expect(screen.queryByTestId('source-unread-facebook')).toBeNull()

      const igTab = screen.getByTestId('source-tab-instagram')
      expect(igTab).toBeInTheDocument()
      expect(screen.getByTestId('platform-logo-instagram')).toBeInTheDocument()
      expect(screen.getByTestId('source-unread-instagram')).toHaveTextContent('2')

      // Key không có logo (zalo-test) render icon chung (platform-logo-generic) + nhãn từ API
      const genericTab = screen.getByTestId('source-tab-zalo-test')
      expect(genericTab).toBeInTheDocument()
      expect(genericTab).toHaveTextContent('Zalo Test')
      expect(screen.getByTestId('platform-logo-generic')).toBeInTheDocument()
      expect(screen.getByTestId('source-unread-zalo-test')).toHaveTextContent('1')
    })

    it('bấm chip gọi onSelectSource với key tương ứng; bấm "Tất cả" gọi với null', () => {
      const onSelect = vi.fn()
      render(
        <InboxSourceTabs
          sources={mockSources}
          selectedSource="messenger"
          onSelectSource={onSelect}
        />,
      )

      const fbTab = screen.getByTestId('source-tab-facebook')
      fireEvent.click(fbTab)
      expect(onSelect).toHaveBeenCalledWith('facebook')

      const allTab = screen.getByTestId('source-tab-all')
      fireEvent.click(allTab)
      expect(onSelect).toHaveBeenCalledWith(null)
    })

    it('điều hướng bàn phím bằng các phím mũi tên và Home/End', () => {
      render(
        <InboxSourceTabs
          sources={mockSources}
          selectedSource={null}
          onSelectSource={vi.fn()}
        />,
      )

      const allTab = screen.getByTestId('source-tab-all')
      const messengerTab = screen.getByTestId('source-tab-messenger')
      const zaloTab = screen.getByTestId('source-tab-zalo-test')

      allTab.focus()
      expect(document.activeElement).toBe(allTab)

      // ArrowRight sang tab kế tiếp
      fireEvent.keyDown(allTab, { key: 'ArrowRight' })
      expect(document.activeElement).toBe(messengerTab)

      // End chuyển đến tab cuối
      fireEvent.keyDown(messengerTab, { key: 'End' })
      expect(document.activeElement).toBe(zaloTab)

      // Home chuyển về tab đầu
      fireEvent.keyDown(zaloTab, { key: 'Home' })
      expect(document.activeElement).toBe(allTab)
    })
  })

  describe('Integration tests trong InboxFeature', () => {
    const mockChannelId = '11111111-1111-1111-1111-111111111111'
    const mockMessengerItem = {
      id: 'msg-1',
      kind: 1,
      platform: 1,
      socialChannelId: mockChannelId,
      channelName: 'Page FB',
      displayName: 'Khách Messenger',
      snippet: 'Alo',
      lastCustomerActivityAt: '2026-10-09T02:00:00Z',
      status: 1,
      unreadCount: 1,
      canReply: true,
      tags: [],
    }

    const mockFacebookCommentItem = {
      id: 'cmt-1',
      kind: 2,
      platform: 1,
      socialChannelId: mockChannelId,
      channelName: 'Page FB',
      displayName: 'Khách Comment FB',
      snippet: 'Bình luận hay',
      lastCustomerActivityAt: '2026-10-09T02:05:00Z',
      status: 1,
      unreadCount: 0,
      canReply: true,
      tags: [],
    }

    const mockInstagramItem = {
      id: 'ig-1',
      kind: 1,
      platform: 3,
      socialChannelId: mockChannelId,
      channelName: 'Kênh IG',
      displayName: 'Khách Instagram',
      snippet: 'Direct IG',
      lastCustomerActivityAt: '2026-10-09T02:10:00Z',
      status: 1,
      unreadCount: 2,
      canReply: true,
      tags: [],
    }

    // Helper component để quan sát URL searchParams
    function LocationWatcher({ onLocationChange }) {
      const location = useLocation()
      React.useEffect(() => {
        onLocationChange(location)
      }, [location, onLocationChange])
      return null
    }

    beforeEach(() => {
      vi.restoreAllMocks()
      useAuthStore.getState().setAuth('mock-token', {
        id: 'u-admin',
        email: 'admin@vni.local',
        userName: 'AdminUser',
        roles: ['Admin'],
      })
      vi.spyOn(inboxApi, 'listChannels').mockResolvedValue([])
      vi.spyOn(inboxApi, 'listUsers').mockResolvedValue([])
      vi.spyOn(inboxApi, 'listTags').mockResolvedValue([])
      vi.spyOn(inboxApi, 'getSources').mockResolvedValue(mockSources)
    })

    afterEach(() => {
      vi.restoreAllMocks()
    })

    it('(e) render theo GET sources, bấm chip ghi ?source= lên URL và gọi filter {source, index: 1}', async () => {
      const filterSpy = vi.spyOn(inboxApi, 'filter').mockResolvedValue({
        items: [mockMessengerItem, mockFacebookCommentItem, mockInstagramItem],
        total: 3,
      })

      let currentLocation = null
      render(
        <MemoryRouter initialEntries={['/inbox']}>
          <LocationWatcher onLocationChange={(loc) => (currentLocation = loc)} />
          <InboxFeature />
        </MemoryRouter>,
      )

      await waitFor(() => {
        expect(screen.getByTestId('source-tab-all')).toBeInTheDocument()
        expect(screen.getByTestId('source-tab-facebook')).toBeInTheDocument()
      })

      // Bấm chip Facebook
      fireEvent.click(screen.getByTestId('source-tab-facebook'))

      await waitFor(() => {
        expect(currentLocation?.search).toContain('source=facebook')
        expect(filterSpy).toHaveBeenCalledWith(
          expect.objectContaining({
            source: 'facebook',
            index: 1,
          }),
        )
      })

      // Chip Facebook active
      expect(screen.getByTestId('source-tab-facebook')).toHaveAttribute('aria-pressed', 'true')
      expect(screen.getByTestId('source-tab-all')).toHaveAttribute('aria-pressed', 'false')

      // Bấm lại "Tất cả"
      fireEvent.click(screen.getByTestId('source-tab-all'))
      await waitFor(() => {
        expect(currentLocation?.search).not.toContain('source=facebook')
        expect(filterSpy).toHaveBeenCalledWith(
          expect.objectContaining({
            source: null,
            index: 1,
          }),
        )
      })
    })

    it('(e) vào thẳng /inbox?source=facebook thì chip Facebook active ngay từ đầu', async () => {
      const filterSpy = vi.spyOn(inboxApi, 'filter').mockResolvedValue({
        items: [mockFacebookCommentItem],
        total: 1,
      })

      render(
        <MemoryRouter initialEntries={['/inbox?source=facebook']}>
          <InboxFeature />
        </MemoryRouter>,
      )

      await waitFor(() => {
        expect(screen.getByTestId('source-tab-facebook')).toBeInTheDocument()
      })

      expect(screen.getByTestId('source-tab-facebook')).toHaveAttribute('aria-pressed', 'true')
      expect(screen.getByTestId('source-tab-all')).toHaveAttribute('aria-pressed', 'false')
      expect(filterSpy).toHaveBeenCalledWith(
        expect.objectContaining({
          source: 'facebook',
          index: 1,
        }),
      )
    })

    it('(e) ?kind=message cũ (redirect từ ClientApp) vẫn lọc đúng với kind=1', async () => {
      const filterSpy = vi.spyOn(inboxApi, 'filter').mockResolvedValue({
        items: [mockMessengerItem],
        total: 1,
      })

      render(
        <MemoryRouter initialEntries={['/inbox?kind=message']}>
          <InboxFeature />
        </MemoryRouter>,
      )

      await waitFor(() => {
        expect(filterSpy).toHaveBeenCalledWith(
          expect.objectContaining({
            kind: 1,
            index: 1,
          }),
        )
      })
    })

    it('(f) mỗi item hiện ra khi đang chọn nguồn X có SourceBadge data-source=X', async () => {
      vi.spyOn(inboxApi, 'filter').mockImplementation(async (req) => {
        if (req?.source === 'messenger') {
          return { items: [mockMessengerItem], total: 1 }
        }
        if (req?.source === 'facebook') {
          return { items: [mockFacebookCommentItem], total: 1 }
        }
        if (req?.source === 'instagram') {
          return { items: [mockInstagramItem], total: 1 }
        }
        return { items: [mockMessengerItem, mockFacebookCommentItem, mockInstagramItem], total: 3 }
      })

      render(
        <MemoryRouter initialEntries={['/inbox?source=messenger']}>
          <InboxFeature />
        </MemoryRouter>,
      )

      await waitFor(() => {
        expect(screen.getByTestId(`conv-item-${mockMessengerItem.id}`)).toBeInTheDocument()
      })

      const badge = screen.getByTestId(`conv-source-${mockMessengerItem.id}`)
      expect(badge).toBeInTheDocument()
      expect(badge).toHaveAttribute('data-source', 'messenger')
    })

    it('đồng bộ hằng số INBOX_SOURCES với resolveInboxSource', () => {
      expect(INBOX_SOURCES.MESSENGER).toBe('messenger')
      expect(INBOX_SOURCES.FACEBOOK).toBe('facebook')
      expect(INBOX_SOURCES.INSTAGRAM).toBe('instagram')
      expect(resolveInboxSource({ kind: 1, platform: 1 })).toBe(INBOX_SOURCES.MESSENGER)
      expect(resolveInboxSource({ kind: 2, platform: 1 })).toBe(INBOX_SOURCES.FACEBOOK)
      expect(resolveInboxSource({ kind: 1, platform: 3 })).toBe(INBOX_SOURCES.INSTAGRAM)
    })
  })
})
