import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { createMemoryRouter, RouterProvider } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import CampaignDetailPage from '../pages/CampaignDetailPage'
import CampaignPagePostsPage from '../pages/CampaignPagePostsPage'
import { campaignApi } from '../services/campaignApi'
import { confirmAction } from '@/shared/utils/confirmAction'

const CAMP_ID = 'c0000000-0000-4000-8000-000000000001'
const CH_ID = 'a0000000-0000-4000-8000-0000000000aa'
const POST_ID = 'p0000000-0000-4000-8000-000000000001'

vi.mock('@/shared/stores/toastStore', () => ({
  toast: { success: vi.fn(), error: vi.fn(), warning: vi.fn() },
}))

vi.mock('@/shared/utils/confirmAction', () => ({
  confirmAction: vi.fn(() => true),
  CONFIRM_MESSAGES: {},
}))

vi.mock('@/modules/social-channels/hooks/useSocialChannels', () => ({
  useSocialChannelAll: () => ({ data: [] }),
}))

vi.mock('@/modules/social-channels/hooks/useChannelGroups', () => ({
  useChannelGroupAll: () => ({ data: [] }),
}))

const permissionsState = { canManage: true }

vi.mock('@/shared/hooks/usePermissions', () => ({
  usePermissions: () => ({
    hasRole: () => permissionsState.canManage,
  }),
}))

vi.mock('../services/campaignApi', async (importOriginal) => {
  const actual = await importOriginal()
  return {
    ...actual,
    campaignApi: {
      getDetail: vi.fn(),
      listPagePosts: vi.fn(),
      pause: vi.fn(),
      resume: vi.fn(),
      end: vi.fn(),
      softDelete: vi.fn(),
      update: vi.fn(),
      getById: vi.fn(),
      getSummaries: vi.fn(),
      create: vi.fn(),
    },
  }
})

function wrap(data) {
  return { data: { success: true, data } }
}

function newClient() {
  return new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  })
}

const detailPayload = {
  campaign: {
    id: CAMP_ID,
    name: 'Camp CE',
    status: 1,
    mediaType: 1,
    scheduleMode: 2,
    weekdays: [],
    publishTimes: ['09:00', '15:00'],
    jitterMinutes: 10,
    startDate: '2026-10-01T00:00:00Z',
    endDate: null,
    channelIds: [CH_ID],
    channelGroupIds: [],
    imageStrategy: 1,
  },
  pages: [
    {
      socialChannelId: CH_ID,
      channelName: 'Page Alpha',
      scheduledCount: 2,
      publishedCount: 1,
      failedCount: 0,
      nextScheduledAt: '2026-10-07T02:00:00Z',
      warning: 'Không có bài nguồn phù hợp',
    },
  ],
}

describe('campaign-ui-test (c) chi tiết page → bài', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    permissionsState.canManage = true
    confirmAction.mockReturnValue(true)
    campaignApi.getDetail.mockResolvedValue(wrap(detailPayload))
    campaignApi.listPagePosts.mockResolvedValue(
      wrap({
        items: [
          {
            id: POST_ID,
            scheduledPublishAt: '2026-10-07T02:00:00Z',
            status: 5,
            contentSnippet: 'Nội dung rút gọn demo',
            title: 'T',
            mediaCount: 1,
            thumbnailUrl: '/api/mediaasset/m1/preview',
            primaryMediaId: 'm1',
          },
        ],
        total: 1,
        index: 1,
        size: 20,
      }),
    )
  })

  it('chi tiết: table page đủ cột + cảnh báo; bấm page → table bài (giờ, trạng thái, nội dung, media, link)', async () => {
    const router = createMemoryRouter(
      [
        { path: '/campaigns/:id', element: <CampaignDetailPage /> },
        { path: '/campaigns/:id/pages/:channelId', element: <CampaignPagePostsPage /> },
      ],
      { initialEntries: [`/campaigns/${CAMP_ID}`] },
    )

    render(
      <QueryClientProvider client={newClient()}>
        <RouterProvider router={router} />
      </QueryClientProvider>,
    )

    expect(await screen.findByTestId('campaign-info')).toBeInTheDocument()
    const table = await screen.findByTestId('campaign-pages-table')
    const headers = within(table).getAllByRole('columnheader').map((h) => h.textContent)
    expect(headers).toEqual(
      expect.arrayContaining(['Page', 'Lên lịch', 'Đã đăng', 'Thất bại', 'Bài kế tiếp', 'Cảnh báo']),
    )
    expect(screen.getByText('Page Alpha')).toBeInTheDocument()
    expect(screen.getByTestId(`page-warning-${CH_ID}`)).toHaveTextContent(
      'Không có bài nguồn phù hợp',
    )

    await userEvent.click(screen.getByTestId(`page-link-${CH_ID}`))
    expect(router.state.location.pathname).toBe(`/campaigns/${CAMP_ID}/pages/${CH_ID}`)

    const postsTable = await screen.findByTestId('campaign-posts-table')
    const postHeaders = within(postsTable).getAllByRole('columnheader').map((h) => h.textContent)
    expect(postHeaders).toEqual(
      expect.arrayContaining(['Media', 'Giờ đăng', 'Trạng thái', 'Nội dung']),
    )
    expect(screen.getByText('Nội dung rút gọn demo')).toBeInTheDocument()
    expect(screen.getByTestId(`post-thumb-${POST_ID}`)).toHaveAttribute(
      'src',
      '/api/mediaasset/m1/preview',
    )
    expect(screen.getByTestId(`post-link-${POST_ID}`)).toHaveAttribute('href', `/posts/${POST_ID}`)
  })
})

describe('campaign-ui-test (e) lifecycle buttons + role gate + confirm', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    permissionsState.canManage = true
    confirmAction.mockReturnValue(true)
    campaignApi.getDetail.mockResolvedValue(wrap(detailPayload))
    campaignApi.pause.mockResolvedValue(wrap({ ...detailPayload.campaign, status: 2 }))
    campaignApi.resume.mockResolvedValue(wrap({ ...detailPayload.campaign, status: 1 }))
    campaignApi.end.mockResolvedValue(wrap({ ...detailPayload.campaign, status: 3 }))
    campaignApi.softDelete.mockResolvedValue(wrap(true))
  })

  function renderDetail(status = 1) {
    campaignApi.getDetail.mockResolvedValue(
      wrap({
        ...detailPayload,
        campaign: { ...detailPayload.campaign, status },
      }),
    )
    const router = createMemoryRouter(
      [
        { path: '/campaigns/:id', element: <CampaignDetailPage /> },
        { path: '/campaigns', element: <div>list</div> },
      ],
      { initialEntries: [`/campaigns/${CAMP_ID}`] },
    )
    render(
      <QueryClientProvider client={newClient()}>
        <RouterProvider router={router} />
      </QueryClientProvider>,
    )
    return router
  }

  it('Admin/CM thấy nút; mỗi thao tác gọi confirm rồi API', async () => {
    renderDetail(1)
    expect(await screen.findByTestId('campaign-lifecycle-actions')).toBeInTheDocument()
    expect(screen.getByTestId('campaign-pause')).toBeInTheDocument()
    expect(screen.getByTestId('campaign-edit')).toBeInTheDocument()
    expect(screen.getByTestId('campaign-end')).toBeInTheDocument()
    expect(screen.getByTestId('campaign-delete')).toBeInTheDocument()

    fireEvent.click(screen.getByTestId('campaign-pause'))
    expect(confirmAction).toHaveBeenCalled()
    await waitFor(() => expect(campaignApi.pause).toHaveBeenCalledWith(CAMP_ID))
  })

  it('Reviewer/Viewer không thấy nút thao tác', async () => {
    permissionsState.canManage = false
    renderDetail(1)
    await screen.findByTestId('campaign-pages-table')
    expect(screen.queryByTestId('campaign-lifecycle-actions')).not.toBeInTheDocument()
    expect(screen.queryByTestId('campaign-pause')).not.toBeInTheDocument()
    expect(screen.queryByTestId('campaign-delete')).not.toBeInTheDocument()
  })

  it('Paused hiện Tiếp tục; huỷ confirm thì không gọi API', async () => {
    confirmAction.mockReturnValueOnce(false)
    renderDetail(2)
    expect(await screen.findByTestId('campaign-resume')).toBeInTheDocument()
    fireEvent.click(screen.getByTestId('campaign-resume'))
    expect(campaignApi.resume).not.toHaveBeenCalled()

    confirmAction.mockReturnValue(true)
    fireEvent.click(screen.getByTestId('campaign-resume'))
    await waitFor(() => expect(campaignApi.resume).toHaveBeenCalledWith(CAMP_ID))
  })
})
