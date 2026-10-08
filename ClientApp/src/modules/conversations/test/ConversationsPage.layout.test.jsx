import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import ConversationsPage from '../pages/ConversationsPage'
import { inboxApi } from '../services/inboxApi'

vi.mock('@/shared/hooks/usePermissions', () => ({
  usePermissions: () => ({
    canViewComments: true,
    canViewMessages: true,
    hasRole: () => true,
  }),
}))

vi.mock('@/modules/social-channels/hooks/useSocialChannels', () => ({
  useSocialChannelAll: () => ({
    data: [
      { id: 'chan-1', pageName: 'Fanpage VNi 1', platform: 1 },
      { id: 'chan-2', pageName: 'Fanpage VNi 2', platform: 1 },
    ],
  }),
}))

vi.mock('@/modules/social-channels/hooks/useChannelGroups', () => ({
  useChannelGroupAll: () => ({
    data: [],
  }),
}))

vi.mock('../services/inboxApi', () => ({
  inboxApi: {
    filter: vi.fn(),
    summary: vi.fn(),
    getProfile: vi.fn(),
    suggestReply: vi.fn(),
  },
  inboxQueryKeys: {
    all: ['inbox'],
    list: (p) => ['inbox', 'list', p],
    summary: ['inbox', 'summary'],
    profile: (k, id) => ['inbox', 'profile', k, id],
  },
}))

vi.mock('@/api/axiosInstance', () => ({
  default: {
    get: vi.fn((url) => {
      if (url === '/api/Users') {
        return Promise.resolve({
          data: {
            data: [
              { id: 'user-1', displayName: 'NV Sale 1', roles: ['Reviewer'], isActive: true },
              { id: 'user-2', displayName: 'NV Sale 2', roles: ['Reviewer'], isActive: true },
            ],
          },
        })
      }
      return Promise.resolve({ data: { data: [] } })
    }),
    post: vi.fn(),
  },
}))

const MOCK_ITEMS = [
  {
    id: 'conv-1',
    kind: 'message',
    socialChannelId: 'chan-1',
    channelName: 'Fanpage VNi 1',
    platform: 1, // Facebook
    participantName: 'Nguyễn Văn A',
    participantAvatarUrl: 'https://example.com/avatar.jpg',
    snippet: 'Em muốn hỏi học phí khóa học',
    lastActivityAt: new Date().toISOString(),
    unreadCount: 2,
    inboxStatus: 1,
    assignedUserId: 'user-1',
    assignedTo: 'NV Sale 1',
  },
  {
    id: 'conv-2',
    kind: 'comment',
    socialChannelId: 'chan-2',
    channelName: 'Fanpage VNi 2',
    platform: 1,
    participantName: 'Trần Thị B',
    participantAvatarUrl: '',
    snippet: 'Bài viết hay quá ạ!',
    lastActivityAt: new Date(Date.now() - 3600000).toISOString(),
    unreadCount: 0,
    inboxStatus: 2,
    assignedUserId: null,
    assignedTo: null,
  },
]

function renderWithProviders(ui, { route = '/conversations' } = {}) {
  const queryClient = new QueryClient({
    defaultOptions: {
      queries: { retry: false, gcTime: 0 },
    },
  })

  return render(
    <QueryClientProvider client={queryClient}>
      <MemoryRouter initialEntries={[route]}>
        {ui}
      </MemoryRouter>
    </QueryClientProvider>,
  )
}

describe('ConversationsPage Layout & Filter Bar (AC 76350e33)', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    window.innerWidth = 1200
    inboxApi.filter.mockResolvedValue({
      data: {
        success: true,
        data: {
          items: MOCK_ITEMS,
          total: 2,
          index: 1,
          size: 20,
        },
      },
    })
  })

  it('(a) renders 3 columns when a conversation is selected and has 8 filter icons with tooltips', async () => {
    renderWithProviders(<ConversationsPage />, { route: '/conversations?id=conv-1&kind=message' })

    // Check 3 columns
    expect(await screen.findByTestId('conversations-col-list')).toBeInTheDocument()
    expect(screen.getByTestId('conversations-col-chat')).toBeInTheDocument()
    expect(screen.getByTestId('conversations-col-info')).toBeInTheDocument()

    // Check all 8 filter icon buttons have tooltips / titles
    expect(screen.getByTitle('Trạng thái')).toBeInTheDocument()
    expect(screen.getByTitle('Loại')).toBeInTheDocument()
    expect(screen.getByTitle('Nguồn')).toBeInTheDocument()
    expect(screen.getByTitle('Chưa đọc')).toBeInTheDocument()
    expect(screen.getByTitle('Khoảng thời gian')).toBeInTheDocument()
    expect(screen.getByTitle('Nhân viên')).toBeInTheDocument()
    expect(screen.getByTitle('Khách chưa trả lời')).toBeInTheDocument()
    expect(screen.getByTitle('Cửa sổ 24h')).toBeInTheDocument()

    // Nút xóa lọc
    expect(screen.getByTestId('filter-clear')).toBeInTheDocument()
  })

  it('(a) toggling filters turns on active state, sends API params, and clear resets to defaults', async () => {
    renderWithProviders(<ConversationsPage />)

    await screen.findByTestId('conversations-col-list')

    const unreadBtn = screen.getByTestId('filter-unread')
    expect(unreadBtn).toHaveAttribute('aria-pressed', 'false')

    // Click Chưa đọc
    fireEvent.click(unreadBtn)
    expect(unreadBtn).toHaveAttribute('aria-pressed', 'true')
    expect(unreadBtn).toHaveClass('is-active')

    await waitFor(() => {
      expect(inboxApi.filter).toHaveBeenCalledWith(
        expect.objectContaining({ unreadOnly: true }),
      )
    })

    // Click Khách chưa trả lời
    const unansweredBtn = screen.getByTestId('filter-unanswered')
    fireEvent.click(unansweredBtn)
    expect(unansweredBtn).toHaveAttribute('aria-pressed', 'true')

    await waitFor(() => {
      expect(inboxApi.filter).toHaveBeenCalledWith(
        expect.objectContaining({ unreadOnly: true, customerUnansweredOnly: true }),
      )
    })

    // Click Cửa sổ 24h
    const windowBtn = screen.getByTestId('filter-open-window')
    fireEvent.click(windowBtn)
    expect(windowBtn).toHaveAttribute('aria-pressed', 'true')

    await waitFor(() => {
      expect(inboxApi.filter).toHaveBeenCalledWith(
        expect.objectContaining({ openWindowOnly: true }),
      )
    })

    // Click Xóa lọc
    const clearBtn = screen.getByTestId('filter-clear')
    expect(clearBtn).not.toBeDisabled()
    fireEvent.click(clearBtn)

    expect(unreadBtn).toHaveAttribute('aria-pressed', 'false')
    expect(unansweredBtn).toHaveAttribute('aria-pressed', 'false')
    expect(windowBtn).toHaveAttribute('aria-pressed', 'false')

    await waitFor(() => {
      expect(inboxApi.filter).toHaveBeenCalledWith(
        expect.objectContaining({
          unreadOnly: null,
          customerUnansweredOnly: null,
          openWindowOnly: null,
        }),
      )
    })
  })

  it('(b) conversation item has avatar, platform badge, kind icon, name, time, snippet, page, staff assignee, and unread dot', async () => {
    renderWithProviders(<ConversationsPage />)

    await screen.findByTestId('conversation-item-conv-1')

    const item1 = screen.getByTestId('conversation-item-conv-1')
    expect(item1).toBeInTheDocument()

    // Avatar
    const avatarImg = item1.querySelector('.conversation-avatar')
    expect(avatarImg).toBeInTheDocument()

    // Badge nền tảng
    const platformBadge = item1.querySelector('[data-testid="platform-badge"]')
    expect(platformBadge).toBeInTheDocument()

    // Icon loại
    const kindIcon = item1.querySelector('[data-testid="kind-icon"]')
    expect(kindIcon).toBeInTheDocument()
    expect(kindIcon).toHaveAttribute('title', 'Tin nhắn')

    // Tên
    expect(item1).toHaveTextContent('Nguyễn Văn A')

    // Snippet
    expect(item1).toHaveTextContent('Em muốn hỏi học phí khóa học')

    // Page
    expect(item1).toHaveTextContent('Fanpage VNi 1')

    // Nhân viên
    expect(item1).toHaveTextContent('NV Sale 1')

    // Chấm chưa đọc (conv-1 has unreadCount: 2)
    const unreadDot = item1.querySelector('[data-testid="unread-dot"]')
    expect(unreadDot).toBeInTheDocument()

    // Item 2 has unreadCount 0 -> no unread dot
    const item2 = screen.getByTestId('conversation-item-conv-2')
    expect(item2.querySelector('[data-testid="unread-dot"]')).toBeNull()
    expect(item2.querySelector('[data-testid="kind-icon"]')).toHaveAttribute('title', 'Bình luận')
  })

  it('(c) filters and opened conversation are read from and maintained in URL', async () => {
    renderWithProviders(<ConversationsPage />, {
      route: '/conversations?id=conv-2&kind=comment&unread=true&search=học%20phí&statuses=1,2',
    })

    
    await screen.findByTestId('conversations-col-chat')
    await screen.findByTestId('conversation-item-conv-2')
    expect(screen.getAllByText('Trần Thị B').length).toBeGreaterThanOrEqual(1)

    // Search bar is populated from URL
    const searchInput = screen.getByPlaceholderText('Tìm theo tên, nội dung, SĐT...')
    expect(searchInput).toHaveValue('học phí')

    // Filter unread is active
    expect(screen.getByTestId('filter-unread')).toHaveAttribute('aria-pressed', 'true')

    // API called with url params
    await waitFor(() => {
      expect(inboxApi.filter).toHaveBeenCalledWith(
        expect.objectContaining({
          search: 'học phí',
          unreadOnly: true,
          statuses: [1, 2],
        }),
      )
    })
  })

  it('(d) narrow viewport renders single column with back button', async () => {
    // Set narrow width
    window.innerWidth = 500

    renderWithProviders(<ConversationsPage />, {
      route: '/conversations?id=conv-1&kind=message',
    })

    // When conversation is selected on narrow viewport: chat column is visible
    expect(await screen.findByTestId('conversations-col-chat')).toBeInTheDocument()
    expect(screen.queryByTestId('conversations-col-list')).not.toBeInTheDocument()

    // Back button is present
    const backBtn = screen.getByTestId('chat-back-btn')
    expect(backBtn).toBeInTheDocument()

    // Click back button returns to list column
    fireEvent.click(backBtn)

    expect(await screen.findByTestId('conversations-col-list')).toBeInTheDocument()
    expect(screen.queryByTestId('conversations-col-chat')).not.toBeInTheDocument()
  })
})
