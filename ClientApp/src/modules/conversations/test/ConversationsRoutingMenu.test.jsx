import { render, screen } from '@testing-library/react'
import { MemoryRouter, useLocation } from 'react-router-dom'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import AppRouter from '@/app/router'
import MainLayout, { NAV_GROUPS } from '@/app/layouts/MainLayout'
import { inboxApi } from '../services/inboxApi'

vi.mock('@/shared/hooks/usePermissions', () => ({
  usePermissions: () => ({
    canViewDashboard: true,
    canViewPosts: true,
    canCreatePost: true,
    canViewCrawl: true,
    canViewComments: true,
    canViewMessages: true,
    canViewPlatforms: true,
    canViewMedia: true,
    canViewJobs: true,
    canManageTemplates: true,
    hasRole: () => true,
  }),
}))

vi.mock('@/app/router/ProtectedRoute', () => ({
  default: ({ children }) => {
    const { Outlet } = require('react-router-dom')
    return children || <Outlet />
  },
}))

vi.mock('@/app/router/GuestRoute', () => ({
  default: ({ children }) => {
    const { Outlet } = require('react-router-dom')
    return children || <Outlet />
  },
}))

vi.mock('../pages/ConversationsPage', () => ({
  default: function MockConversationsPage() {
    const location = useLocation()
    return (
      <div data-testid="mock-conversations-page">
        Màn hình Hội thoại: {location.search}
      </div>
    )
  },
}))

vi.mock('../services/inboxApi', () => ({
  inboxApi: {
    summary: vi.fn(),
    filter: vi.fn(),
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

function renderWithRouter(ui, { route = '/' } = {}) {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false, gcTime: 0 } },
  })

  return render(
    <QueryClientProvider client={queryClient}>
      <MemoryRouter initialEntries={[route]}>
        {ui}
      </MemoryRouter>
    </QueryClientProvider>,
  )
}

describe('Routing redirects & MainLayout Menu (AC 458fbe60 (d), (e))', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    inboxApi.summary.mockResolvedValue({
      data: {
        success: true,
        data: {
          unread: 8,
          newCount: 4,
          inProgress: 2,
        },
      },
    })
  })

  it('(d) /messages redirects to /conversations?kind=message', async () => {
    renderWithRouter(<AppRouter />, { route: '/messages' })

    const page = await screen.findByTestId('mock-conversations-page')
    expect(page).toHaveTextContent('?kind=message')
  })

  it('(d) /comments redirects to /conversations?kind=comment', async () => {
    renderWithRouter(<AppRouter />, { route: '/comments' })

    const page = await screen.findByTestId('mock-conversations-page')
    expect(page).toHaveTextContent('?kind=comment')
  })

  it('(e) MainLayout menu has "Hội thoại" and does not have old "Comments" and "Tin nhắn Page"', async () => {
    renderWithRouter(<MainLayout />, { route: '/dashboard' })

    // "Hội thoại" exists
    const convLink = screen.getByText('Hội thoại')
    expect(convLink).toBeInTheDocument()

    // 2 old menu items do not exist
    expect(screen.queryByText('Comments')).not.toBeInTheDocument()
    expect(screen.queryByText('Tin nhắn Page')).not.toBeInTheDocument()

    // Badge shows unread count from inbox summary (8)
    const badge = await screen.findByTestId('inbox-summary-badge')
    expect(badge).toHaveTextContent('8')
  })

  it('(e) NAV_GROUPS contains /conversations item and no /comments or /messages items', () => {
    const allLinks = NAV_GROUPS.flatMap((g) => (g.children ?? [g])).map((item) => item.to)
    expect(allLinks).toContain('/conversations')
    expect(allLinks).not.toContain('/comments')
    expect(allLinks).not.toContain('/messages')
  })
})
