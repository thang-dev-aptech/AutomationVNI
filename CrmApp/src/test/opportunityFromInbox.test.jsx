import React from 'react'
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { render, screen, fireEvent, waitFor } from '@testing-library/react'
import { MemoryRouter, Routes, Route, useLocation } from 'react-router-dom'
import InboxFeature from '../features/inbox/InboxFeature'
import { inboxApi } from '../features/inbox/api/inboxApi'
import { opportunityApi } from '../features/opportunities/api/opportunityApi'
import { useAuthStore } from '../auth/authStore'
import OpportunitiesFeature from '../features/opportunities/OpportunitiesFeature'

function LocationTracker() {
  const location = useLocation()
  return (
    <div data-testid="test-location">
      <span data-testid="location-pathname">{location.pathname}</span>
      <span data-testid="location-search">{location.search}</span>
    </div>
  )
}

describe('AC fd9216b3 (e) & /inbox?kind=&id= — Tạo/Xem Cơ hội từ Inbox & Điều hướng URL', () => {
  const mockConversationItem = {
    id: 'conv-1',
    kind: 1, // message
    socialChannelId: '00000000-0000-0000-0000-000000000001',
    channelName: 'VNI Tuyển sinh',
    displayName: 'Nguyễn Thu Trang',
    snippet: 'Em muốn đăng ký tư vấn khoá IELTS',
    lastCustomerActivityAt: '2026-10-09T08:00:00Z',
    status: 1,
    assignedUserId: null,
    assignedTo: null,
    unreadCount: 0,
    canReply: true,
    tags: [],
  }

  const mockDetail = {
    kind: 1,
    conversation: {
      id: 'conv-1',
      participantName: 'Nguyễn Thu Trang',
      channelName: 'VNI Tuyển sinh',
      canReply: true,
      isReplyWindowOpen: true,
      inboxStatus: 1,
      messages: [
        {
          id: 'msg-1',
          text: 'Em muốn đăng ký tư vấn khoá IELTS',
          isFromPage: false,
          sentAt: '2026-10-09T08:00:00Z',
        },
      ],
    },
    tags: [],
    replyEndpoint: '/api/PageMessage/conv-1/send',
  }

  beforeEach(() => {
    vi.restoreAllMocks()
    localStorage.clear()

    useAuthStore.getState().setAuth('mock-token', {
      id: 'u-reviewer',
      email: 'reviewer@vni.local',
      userName: 'Reviewer Care',
      roles: ['Reviewer'],
    })

    vi.spyOn(inboxApi, 'listChannels').mockResolvedValue([])
    vi.spyOn(inboxApi, 'listUsers').mockResolvedValue([])
    vi.spyOn(inboxApi, 'listTags').mockResolvedValue([])
    vi.spyOn(inboxApi, 'getCustomer').mockResolvedValue({
      id: 'cust-1',
      displayName: 'Nguyễn Thu Trang',
      phoneE164: '+84988888888',
      channels: [],
      stats: { totalOrders: 0 },
      media: [],
      activities: [],
    })
    vi.spyOn(inboxApi, 'getMessage').mockResolvedValue(mockDetail)
    vi.spyOn(inboxApi, 'filter').mockResolvedValue({
      items: [mockConversationItem],
      total: 1,
    })

    // Opportunities mocks
    vi.spyOn(opportunityApi, 'listStages').mockResolvedValue([
      { id: 'stg-1', name: 'Mới', color: '#3b82f6', kind: 1, sortOrder: 1 },
    ])
    vi.spyOn(opportunityApi, 'listUsers').mockResolvedValue([])
    vi.spyOn(opportunityApi, 'stats').mockResolvedValue({
      total: 1,
      open: 1,
      won: 0,
      lost: 0,
      activity: 0,
      rev: 0,
    })
  })

  afterEach(() => {
    vi.restoreAllMocks()
  })

  // =========================================================================
  // AC fd9216b3 (e) — Nút Tạo cơ hội / Xem cơ hội trong Inbox
  // =========================================================================
  describe('Nút "Tạo cơ hội" và "Xem cơ hội" trong Inbox', () => {
    it('Admin/Reviewer thấy "Tạo cơ hội"; bấm → gọi from-conversation với đúng kind/id; sau đó hiện "Xem cơ hội"', async () => {
      // Chưa có cơ hội mở
      vi.spyOn(opportunityApi, 'byConversation').mockResolvedValue(null)
      const fromConvSpy = vi
        .spyOn(opportunityApi, 'fromConversation')
        .mockResolvedValue({
          id: 'opp-created-1',
          title: 'Cơ hội từ tin nhắn của Nguyễn Thu Trang',
          status: 1, // Open
        })

      render(
        <MemoryRouter initialEntries={['/inbox']}>
          <Routes>
            <Route path="/inbox" element={<InboxFeature />} />
          </Routes>
        </MemoryRouter>
      )

      // Chờ inbox load xong danh sách conv-1
      expect(
        await screen.findByTestId('conv-item-conv-1')
      ).toBeInTheDocument()

      expect(
        await screen.findByTestId('btn-create-opportunity')
      ).toBeInTheDocument()
      expect(screen.getByTestId('btn-create-opportunity')).toHaveTextContent(
        'Tạo cơ hội'
      )

      // Bấm "Tạo cơ hội"
      fireEvent.click(screen.getByTestId('btn-create-opportunity'))

      // Kiểm tra gọi from-conversation
      await waitFor(() => {
        expect(fromConvSpy).toHaveBeenCalledWith({
          kind: 'message',
          id: 'conv-1',
        })
      })

      // Sau khi tạo thành công, chuyển thành nút "Xem cơ hội"
      expect(
        await screen.findByTestId('btn-view-opportunity')
      ).toBeInTheDocument()
      expect(screen.getByTestId('btn-view-opportunity')).toHaveTextContent(
        'Xem cơ hội'
      )
    })

    it('hội thoại đã có cơ hội Open → hiện nút "Xem cơ hội", bấm vào điều hướng /tasks?opportunity=<id>', async () => {
      // Đã có cơ hội Open
      vi.spyOn(opportunityApi, 'byConversation').mockResolvedValue({
        id: 'opp-existing-99',
        title: 'Cơ hội tư vấn IELTS',
        status: 1,
        isArchived: false,
      })

      render(
        <MemoryRouter initialEntries={['/inbox']}>
          <LocationTracker />
          <Routes>
            <Route path="/inbox" element={<InboxFeature />} />
            <Route path="/tasks" element={<OpportunitiesFeature />} />
          </Routes>
        </MemoryRouter>
      )

      // Nút "Xem cơ hội" hiển thị, nút "Tạo cơ hội" không hiển thị
      expect(
        await screen.findByTestId('btn-view-opportunity')
      ).toBeInTheDocument()
      expect(
        screen.queryByTestId('btn-create-opportunity')
      ).not.toBeInTheDocument()

      // Bấm "Xem cơ hội"
      fireEvent.click(screen.getByTestId('btn-view-opportunity'))

      // Điều hướng sang /tasks?opportunity=opp-existing-99
      await waitFor(() => {
        expect(screen.getByTestId('location-pathname').textContent).toBe(
          '/tasks'
        )
        expect(screen.getByTestId('location-search').textContent).toContain(
          'opportunity=opp-existing-99'
        )
      })
    })

    it('Viewer: KHÔNG thấy nút "+ Tạo cơ hội"', async () => {
      useAuthStore.getState().setAuth('mock-token', {
        id: 'u-viewer',
        email: 'viewer@vni.local',
        userName: 'Viewer Only',
        roles: ['Viewer'],
      })
      vi.spyOn(opportunityApi, 'byConversation').mockResolvedValue(null)

      render(
        <MemoryRouter initialEntries={['/inbox']}>
          <Routes>
            <Route path="/inbox" element={<InboxFeature />} />
          </Routes>
        </MemoryRouter>
      )

      await waitFor(() => {
        expect(screen.getByTestId('inbox-feature')).toBeInTheDocument()
      })

      // Viewer không được thấy nút "Tạo cơ hội"
      expect(
        screen.queryByTestId('btn-create-opportunity')
      ).not.toBeInTheDocument()
    })
  })

  // =========================================================================
  // /inbox?kind=&id= — Mở đúng hội thoại kể cả khi không nằm ở trang 1
  // =========================================================================
  describe('/inbox?kind=&id= — Mở đúng hội thoại ngoài trang 1', () => {
    it('/inbox?kind=message&id=conv-deep gọi getMessage và chèn vào đầu danh sách nếu chưa có', async () => {
      // Giả sử trang 1 của filter chỉ trả conv-1, KHÔNG có conv-deep
      vi.spyOn(inboxApi, 'filter').mockResolvedValue({
        items: [mockConversationItem],
        total: 100,
      })

      const deepDetail = {
        kind: 1,
        conversation: {
          id: 'conv-deep',
          participantName: 'Khách Hàng Nằm Ở Trang 3',
          channelName: 'VNI Tuyển sinh',
          canReply: true,
          isReplyWindowOpen: true,
          inboxStatus: 1,
          messages: [
            {
              id: 'msg-deep',
              text: 'Tin nhắn cũ nằm tận trang 3',
              isFromPage: false,
              sentAt: '2026-10-01T08:00:00Z',
            },
          ],
        },
        tags: [],
      }

      const getMsgSpy = vi
        .spyOn(inboxApi, 'getMessage')
        .mockResolvedValue(deepDetail)

      render(
        <MemoryRouter initialEntries={['/inbox?kind=message&id=conv-deep']}>
          <Routes>
            <Route path="/inbox" element={<InboxFeature />} />
          </Routes>
        </MemoryRouter>
      )

      // Kiểm tra gọi getMessage với conv-deep
      await waitFor(() => {
        expect(getMsgSpy).toHaveBeenCalledWith('conv-deep')
      })

      // conv-deep được chèn vào danh sách và chọn active
      expect(
        await screen.findByTestId('conv-item-conv-deep')
      ).toBeInTheDocument()
      expect(
        screen.getByTestId('conv-name-conv-deep')
      ).toHaveTextContent('Khách Hàng Nằm Ở Trang 3')
      expect(
        screen.getByTestId('conv-snippet-conv-deep')
      ).toHaveTextContent('Tin nhắn cũ nằm tận trang 3')
    })

    it('/inbox?kind=comment&id=comm-deep gọi getComment và chèn vào danh sách', async () => {
      vi.spyOn(inboxApi, 'filter').mockResolvedValue({
        items: [mockConversationItem],
        total: 100,
      })

      const deepCommentDetail = {
        kind: 2,
        thread: {
          id: 'comm-deep',
          authorName: 'Khách Bình Luận Bài Viết',
          channelName: 'Fanpage Tuyển sinh',
          capabilities: { canReply: true },
          status: 1,
          message: 'Bình luận hỏi học phí từ tuần trước',
          commentedAt: '2026-10-02T10:00:00Z',
        },
        tags: [],
      }

      const getCommentSpy = vi
        .spyOn(inboxApi, 'getComment')
        .mockResolvedValue(deepCommentDetail)

      render(
        <MemoryRouter initialEntries={['/inbox?kind=comment&id=comm-deep']}>
          <Routes>
            <Route path="/inbox" element={<InboxFeature />} />
          </Routes>
        </MemoryRouter>
      )

      await waitFor(() => {
        expect(getCommentSpy).toHaveBeenCalledWith('comm-deep')
      })

      expect(
        await screen.findByTestId('conv-item-comm-deep')
      ).toBeInTheDocument()
      expect(
        screen.getByTestId('conv-name-comm-deep')
      ).toHaveTextContent('Khách Bình Luận Bài Viết')
    })
  })
})
