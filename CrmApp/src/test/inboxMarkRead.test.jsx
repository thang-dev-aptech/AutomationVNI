import React from 'react'
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { render, screen, fireEvent, waitFor, act } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import InboxFeature from '../features/inbox/InboxFeature'
import { inboxApi } from '../features/inbox/api/inboxApi'
import { useAuthStore } from '../auth/authStore'
import crmApi from '../api/crmApi'

describe('AC a181718d (f) — Mark read on select, optimistic badge clear, prevent resurrect on polling', () => {
  const getUnreadBadge = (itemEl) =>
    itemEl ? itemEl.querySelector('span[style*="var(--crm-danger)"]') : null

  const mockMessageItemWithBadge = {
    id: '11111111-2222-3333-4444-555555555555',
    kind: 1, // Message
    socialChannelId: 'aaaa1111-bb22-cc33-dd44-ee5555555555',
    channelName: 'VNI Fanpage Tuyển sinh',
    displayName: 'Nguyễn Văn An',
    snippet: 'Em muốn hỏi về học phí khóa K45',
    lastCustomerActivityAt: '2026-10-07T06:00:00Z',
    status: 1, // Mới
    assignedUserId: null,
    assignedTo: null,
    unreadCount: 3,
    canReply: true,
    tags: [],
  }

  const mockCommentItemWithBadge = {
    id: '22222222-3333-4444-5555-666666666666',
    kind: 2, // Comment
    socialChannelId: 'aaaa1111-bb22-cc33-dd44-ee5555555555',
    channelName: 'VNI Fanpage Tuyển sinh',
    displayName: 'Trần Thị Bích',
    snippet: 'Khóa học này có cấp chứng chỉ không ạ?',
    lastCustomerActivityAt: '2026-10-07T05:30:00Z',
    status: 2, // Đang xử lý
    assignedUserId: null,
    assignedTo: null,
    unreadCount: 5,
    canReply: true,
    tags: [],
  }

  const mockMessageDetail = {
    kind: 1,
    conversation: {
      id: mockMessageItemWithBadge.id,
      participantName: 'Nguyễn Văn An',
      channelName: 'VNI Fanpage Tuyển sinh',
      canReply: true,
      inboxStatus: 1,
      messages: [
        {
          id: 'msg-1',
          text: 'Em muốn hỏi về học phí khóa K45',
          isFromPage: false,
          sentAt: '2026-10-07T06:00:00Z',
        },
      ],
    },
    tags: [],
    replyEndpoint: `/api/PageMessage/${mockMessageItemWithBadge.id}/send`,
  }

  const mockCommentDetail = {
    kind: 2,
    thread: {
      id: mockCommentItemWithBadge.id,
      authorName: 'Trần Thị Bích',
      message: 'Khóa học này có cấp chứng chỉ không ạ?',
      postMessage: 'Khai giảng khóa mới',
      postPermalinkUrl: 'https://facebook.com/posts/1',
      likeCount: 2,
      commentedAt: '2026-10-07T05:30:00Z',
      capabilities: { canReply: true },
      replies: [],
    },
    tags: [],
    replyEndpoint: `/api/SocialComment/${mockCommentItemWithBadge.id}/reply`,
  }

  beforeEach(() => {
    vi.restoreAllMocks()
    vi.spyOn(inboxApi, 'listUsers').mockResolvedValue([])
    vi.spyOn(inboxApi, 'listTags').mockResolvedValue([])
    vi.spyOn(inboxApi, 'listChannels').mockResolvedValue([])
    vi.spyOn(inboxApi, 'getMessage').mockResolvedValue(mockMessageDetail)
    vi.spyOn(inboxApi, 'getComment').mockResolvedValue(mockCommentDetail)
  })

  afterEach(() => {
    vi.restoreAllMocks()
  })

  describe('inboxApi.markRead API client', () => {
    it('normalizes kind 1 / message and calls POST /CrmInbox/message/:id/read', async () => {
      const postSpy = vi.spyOn(crmApi, 'post').mockResolvedValue({
        data: { success: true, data: { id: 'msg-uuid', kind: 'message', read: true } },
      })

      const res = await inboxApi.markRead(1, 'msg-uuid')
      expect(postSpy).toHaveBeenCalledWith('/CrmInbox/message/msg-uuid/read')
      expect(res).toEqual({ id: 'msg-uuid', kind: 'message', read: true })
    })

    it('normalizes kind 2 / comment and calls POST /CrmInbox/comment/:id/read', async () => {
      const postSpy = vi.spyOn(crmApi, 'post').mockResolvedValue({
        data: { success: true, data: { id: 'comment-uuid', kind: 'comment', read: true } },
      })

      const res = await inboxApi.markRead(2, 'comment-uuid')
      expect(postSpy).toHaveBeenCalledWith('/CrmInbox/comment/comment-uuid/read')
      expect(res).toEqual({ id: 'comment-uuid', kind: 'comment', read: true })
    })
  })

  describe('Optimistic mark read and badge disappearance for Admin & Reviewer', () => {
    it('Admin clicks item with badge → badge disappears immediately and calls inboxApi.markRead(kind, id)', async () => {
      useAuthStore.getState().setAuth('mock-token', {
        id: 'u-admin',
        userName: 'Admin User',
        roles: ['Admin'],
      })

      vi.spyOn(inboxApi, 'filter').mockResolvedValue({
        items: [mockMessageItemWithBadge],
        total: 1,
      })
      const markReadSpy = vi.spyOn(inboxApi, 'markRead').mockResolvedValue({
        id: mockMessageItemWithBadge.id,
        kind: 'message',
        read: true,
      })

      render(
        <MemoryRouter>
          <InboxFeature />
        </MemoryRouter>,
      )

      await waitFor(() => {
        expect(screen.getByTestId(`conv-item-${mockMessageItemWithBadge.id}`)).toBeInTheDocument()
      })

      const convItem = screen.getByTestId(`conv-item-${mockMessageItemWithBadge.id}`)
      expect(getUnreadBadge(convItem)?.textContent).toBe('3')

      fireEvent.click(convItem)

      expect(getUnreadBadge(convItem)).toBeNull()
      expect(markReadSpy).toHaveBeenCalledWith(
        mockMessageItemWithBadge.kind,
        mockMessageItemWithBadge.id,
      )
    })

    it('Reviewer clicks comment item with badge → badge disappears immediately and calls markRead', async () => {
      useAuthStore.getState().setAuth('mock-token', {
        id: 'u-reviewer',
        userName: 'Reviewer Care',
        roles: ['Reviewer'],
      })

      vi.spyOn(inboxApi, 'filter').mockResolvedValue({
        items: [mockCommentItemWithBadge],
        total: 1,
      })
      const markReadSpy = vi.spyOn(inboxApi, 'markRead').mockResolvedValue({
        id: mockCommentItemWithBadge.id,
        kind: 'comment',
        read: true,
      })

      render(
        <MemoryRouter>
          <InboxFeature />
        </MemoryRouter>,
      )

      await waitFor(() => {
        expect(screen.getByTestId(`conv-item-${mockCommentItemWithBadge.id}`)).toBeInTheDocument()
      })

      const convItem = screen.getByTestId(`conv-item-${mockCommentItemWithBadge.id}`)
      expect(getUnreadBadge(convItem)?.textContent).toBe('5')

      fireEvent.click(convItem)

      expect(getUnreadBadge(convItem)).toBeNull()
      expect(markReadSpy).toHaveBeenCalledWith(
        mockCommentItemWithBadge.kind,
        mockCommentItemWithBadge.id,
      )
    })
  })

  describe('Polling merge badge resurrection prevention', () => {
    it('Polling returns old unreadCount > 0 with lastCustomerActivityAt <= readAt → badge remains 0 and does not resurrect', async () => {
      vi.useFakeTimers()
      vi.setSystemTime(new Date('2026-10-07T06:00:00Z'))

      try {
        useAuthStore.getState().setAuth('mock-token', {
          id: 'u-admin',
          userName: 'Admin User',
          roles: ['Admin'],
        })

        let filterCallCount = 0
        const filterSpy = vi.spyOn(inboxApi, 'filter').mockImplementation(async () => {
          filterCallCount++
          return {
            items: [
              {
                ...mockMessageItemWithBadge,
                unreadCount: 3,
                lastCustomerActivityAt: '2026-10-07T06:00:00Z',
              },
            ],
            total: 1,
          }
        })

        vi.spyOn(inboxApi, 'markRead').mockResolvedValue({
          id: mockMessageItemWithBadge.id,
          kind: 'message',
          read: true,
        })

        render(
          <MemoryRouter>
            <InboxFeature />
          </MemoryRouter>,
        )

        await act(async () => {
          await Promise.resolve()
        })

        const convItem = screen.getByTestId(`conv-item-${mockMessageItemWithBadge.id}`)
        expect(getUnreadBadge(convItem)?.textContent).toBe('3')

        // User reads conversation
        act(() => {
          fireEvent.click(convItem)
        })

        // Badge cleared immediately
        expect(getUnreadBadge(convItem)).toBeNull()

        // Trigger background polling (every 20s)
        await act(async () => {
          vi.advanceTimersByTime(20000)
        })

        // Filter was called during polling
        expect(filterSpy).toHaveBeenCalledTimes(2)

        // Badge DOES NOT come back! unreadCount remains 0
        expect(getUnreadBadge(convItem)).toBeNull()
      } finally {
        vi.useRealTimers()
      }
    })

    it('Polling returns NEW customer activity (lastCustomerActivityAt > readAt) → badge reappears with new count', async () => {
      vi.useFakeTimers()
      vi.setSystemTime(new Date('2026-10-07T06:00:00Z'))

      try {
        useAuthStore.getState().setAuth('mock-token', {
          id: 'u-admin',
          userName: 'Admin User',
          roles: ['Admin'],
        })

        let filterCallCount = 0
        vi.spyOn(inboxApi, 'filter').mockImplementation(async () => {
          filterCallCount++
          if (filterCallCount === 1) {
            return {
              items: [
                {
                  ...mockMessageItemWithBadge,
                  unreadCount: 3,
                  lastCustomerActivityAt: '2026-10-07T06:00:00Z',
                },
              ],
              total: 1,
            }
          }
          // On polling, customer sent a brand new message at a later timestamp
          return {
            items: [
              {
                ...mockMessageItemWithBadge,
                unreadCount: 1,
                snippet: 'Tin nhắn mới từ khách hàng!',
                lastCustomerActivityAt: '2026-10-07T06:00:15Z',
              },
            ],
            total: 1,
          }
        })

        vi.spyOn(inboxApi, 'markRead').mockResolvedValue({
          id: mockMessageItemWithBadge.id,
          kind: 'message',
          read: true,
        })

        render(
          <MemoryRouter>
            <InboxFeature />
          </MemoryRouter>,
        )

        await act(async () => {
          await Promise.resolve()
        })

        const convItem = screen.getByTestId(`conv-item-${mockMessageItemWithBadge.id}`)
        expect(getUnreadBadge(convItem)?.textContent).toBe('3')

        // User reads conversation at 06:00:00Z
        act(() => {
          fireEvent.click(convItem)
        })
        expect(getUnreadBadge(convItem)).toBeNull()

        // Advance 20s for background polling (to 06:00:20Z)
        await act(async () => {
          vi.advanceTimersByTime(20000)
        })

        // New customer activity arrived (06:00:15Z > 06:00:00Z) -> badge updates to 1!
        expect(getUnreadBadge(convItem)?.textContent).toBe('1')
      } finally {
        vi.useRealTimers()
      }
    })
  })

  describe('Error handling & RBAC Viewer rules', () => {
    it('markRead fails silently (logs warn) and does NOT block opening conversation detail', async () => {
      useAuthStore.getState().setAuth('mock-token', {
        id: 'u-admin',
        userName: 'Admin User',
        roles: ['Admin'],
      })

      const warnSpy = vi.spyOn(console, 'warn').mockImplementation(() => {})
      vi.spyOn(inboxApi, 'filter').mockResolvedValue({
        items: [mockMessageItemWithBadge],
        total: 1,
      })
      const getMessageSpy = vi.spyOn(inboxApi, 'getMessage').mockResolvedValue(mockMessageDetail)
      // markRead rejects with network error
      vi.spyOn(inboxApi, 'markRead').mockRejectedValue(new Error('Network disconnected'))

      render(
        <MemoryRouter>
          <InboxFeature />
        </MemoryRouter>,
      )

      await waitFor(() => {
        expect(screen.getByTestId(`conv-item-${mockMessageItemWithBadge.id}`)).toBeInTheDocument()
      })

      const convItem = screen.getByTestId(`conv-item-${mockMessageItemWithBadge.id}`)
      fireEvent.click(convItem)

      // Badge disappears
      expect(getUnreadBadge(convItem)).toBeNull()

      // Error was logged silently to console.warn
      await waitFor(() => {
        expect(warnSpy).toHaveBeenCalledWith(
          'inboxApi.markRead error:',
          expect.any(Error),
        )
      })

      // Conversation detail is still loaded via inboxApi.getMessage and opened without blocking!
      await waitFor(() => {
        expect(getMessageSpy).toHaveBeenCalledWith(mockMessageItemWithBadge.id)
      })
    })

    it('Viewer clicks item with badge → does NOT call markRead, badge is NOT cleared', async () => {
      useAuthStore.getState().setAuth('mock-token', {
        id: 'u-viewer',
        userName: 'Viewer ReadOnly',
        roles: ['Viewer'],
      })

      vi.spyOn(inboxApi, 'filter').mockResolvedValue({
        items: [mockMessageItemWithBadge],
        total: 1,
      })
      const markReadSpy = vi.spyOn(inboxApi, 'markRead').mockResolvedValue({})

      render(
        <MemoryRouter>
          <InboxFeature />
        </MemoryRouter>,
      )

      await waitFor(() => {
        expect(screen.getByTestId(`conv-item-${mockMessageItemWithBadge.id}`)).toBeInTheDocument()
      })

      const convItem = screen.getByTestId(`conv-item-${mockMessageItemWithBadge.id}`)
      expect(getUnreadBadge(convItem)?.textContent).toBe('3')

      // Viewer clicks item
      fireEvent.click(convItem)

      // markRead is NOT called
      expect(markReadSpy).not.toHaveBeenCalled()

      // Badge remains visible for Viewer
      expect(getUnreadBadge(convItem)?.textContent).toBe('3')
    })
  })
})
