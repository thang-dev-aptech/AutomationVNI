import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { fireEvent, render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { createMemoryRouter, MemoryRouter, RouterProvider } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { NAV_GROUPS } from '@/app/layouts/MainLayout'
import PostListPage from '@/modules/posts/pages/PostListPage'
import CampaignListPage from '../pages/CampaignListPage'
import CampaignFormModal from '../components/CampaignFormModal'
import {
  CAMPAIGN_MEDIA_TYPE,
  CAMPAIGN_SCHEDULE_MODE,
} from '../constants/campaignEnums'
import {
  buildCampaignPayload,
  emptyCampaignForm,
  RUN_MODE_UNTIL_STOPPED,
  RUN_MODE_WITH_END,
  tryAddPublishTime,
  validateCampaignForm,
} from '../utils/campaignForm'
import { campaignApi } from '../services/campaignApi'

vi.mock('@/shared/stores/toastStore', () => ({
  toast: { success: vi.fn(), error: vi.fn(), warning: vi.fn() },
}))

vi.mock('@/modules/posts/hooks/usePosts', () => ({
  usePosts: () => ({
    data: { items: [], total: 0, size: 20 },
    isLoading: false,
    isError: false,
    error: null,
    refetch: vi.fn(),
  }),
  useDeletePost: () => ({ mutateAsync: vi.fn(), isPending: false }),
  useDeleteAllPosts: () => ({ mutateAsync: vi.fn(), isPending: false }),
}))

vi.mock('@/modules/social-channels/hooks/useSocialChannels', () => ({
  useSocialChannelAll: () => ({
    data: [
      { id: 'ch-1', pageName: 'Page One' },
      { id: 'ch-2', pageName: 'Page Two' },
    ],
  }),
}))

vi.mock('@/modules/social-channels/hooks/useChannelGroups', () => ({
  useChannelGroupAll: () => ({
    data: [{ id: 'grp-1', name: 'Nhóm A' }],
  }),
}))

vi.mock('@/shared/hooks/usePermissions', () => ({
  usePermissions: () => ({
    canCreatePost: true,
    canDeletePost: () => false,
    canDeleteAllPosts: false,
    hasRole: () => true,
  }),
}))

vi.mock('../services/campaignApi', async (importOriginal) => {
  const actual = await importOriginal()
  return {
    ...actual,
    campaignApi: {
      getSummaries: vi.fn(),
      getById: vi.fn(),
      getDetail: vi.fn(),
      create: vi.fn(),
      update: vi.fn(),
      pause: vi.fn(),
      resume: vi.fn(),
    },
  }
})

vi.mock('@/shared/utils/confirmAction', () => ({
  confirmAction: vi.fn(() => true),
}))

const CAMP_ID = 'c0000000-0000-4000-8000-000000000001'

function wrap(data) {
  return { data: { success: true, data } }
}

function newClient() {
  return new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  })
}

describe('campaign-ui-test (a) menu + bỏ Tái sử dụng bài', () => {
  it('menu Bài đăng có mục Chiến dịch', () => {
    const baiDang = NAV_GROUPS.find((g) => g.key === 'bai-dang')
    const labels = (baiDang?.children ?? []).map((c) => c.label)
    expect(labels).toContain('Chiến dịch')
    const item = baiDang.children.find((c) => c.label === 'Chiến dịch')
    expect(item.to).toBe('/campaigns')
  })

  it('PostListPage không còn nút Tái sử dụng bài', () => {
    render(
      <QueryClientProvider client={newClient()}>
        <MemoryRouter>
          <PostListPage />
        </MemoryRouter>
      </QueryClientProvider>,
    )
    expect(screen.queryByRole('button', { name: 'Tái sử dụng bài' })).not.toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Tạo bài viết' })).toBeInTheDocument()
  })
})

describe('campaign-ui-test (b) danh sách table + điều hướng chi tiết', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    campaignApi.getSummaries.mockResolvedValue(
      wrap([
        {
          id: CAMP_ID,
          name: 'Camp Demo',
          status: 1,
          mediaType: 1,
          scheduleMode: 2,
          pageCount: 2,
          upcomingCount: 3,
          publishedCount: 1,
          failedCount: 0,
          startDate: '2026-10-01T00:00:00Z',
        },
      ]),
    )
  })

  it('table đủ cột và bấm dòng đi tới /campaigns/:id', async () => {
    const router = createMemoryRouter(
      [
        { path: '/campaigns', element: <CampaignListPage /> },
        { path: '/campaigns/:id', element: <div data-testid="detail-page">detail</div> },
      ],
      { initialEntries: ['/campaigns'] },
    )

    render(
      <QueryClientProvider client={newClient()}>
        <RouterProvider router={router} />
      </QueryClientProvider>,
    )

    const table = await screen.findByTestId('campaign-list-table')
    const headers = within(table).getAllByRole('columnheader').map((h) => h.textContent)
    expect(headers[0]).toBe('Chạy')
    expect(headers).toEqual(
      expect.arrayContaining(['Chạy', 'Tên', 'Trạng thái', 'Page', 'Sắp tới', 'Đã đăng', 'Thất bại']),
    )
    expect(await screen.findByText('Camp Demo')).toBeInTheDocument()

    const row = screen.getByTestId(`campaign-row-${CAMP_ID}`)
    const runSwitch = within(row).getByTestId('campaign-run-switch')
    expect(within(runSwitch).getByRole('switch')).toBeChecked()

    fireEvent.click(row)
    expect(await screen.findByTestId('detail-page')).toBeInTheDocument()
    expect(router.state.location.pathname).toBe(`/campaigns/${CAMP_ID}`)
  })

  it('slider đầu dòng tạm dừng chiến dịch Running', async () => {
    const { confirmAction } = await import('@/shared/utils/confirmAction')
    campaignApi.pause.mockResolvedValue(wrap({ id: CAMP_ID, status: 2 }))

    render(
      <QueryClientProvider client={newClient()}>
        <MemoryRouter>
          <CampaignListPage />
        </MemoryRouter>
      </QueryClientProvider>,
    )

    const row = await screen.findByTestId(`campaign-row-${CAMP_ID}`)
    const switchEl = within(row).getByRole('switch', { name: /Tạm dừng Camp Demo/i })
    expect(switchEl).toBeChecked()

    await userEvent.click(switchEl)
    expect(confirmAction).toHaveBeenCalled()
    expect(campaignApi.pause).toHaveBeenCalledWith(CAMP_ID)
  })
})

describe('campaign-ui-test (d) form validate + payload', () => {
  it('validate: tên trống, thiếu kênh/nhóm, Theo thứ không chọn thứ, giờ trùng/sai, lệch, end < start', () => {
    const base = emptyCampaignForm()
    expect(validateCampaignForm({ ...base, name: '' }).errors.name).toBeTruthy()
    expect(
      validateCampaignForm({ ...base, name: 'X', channelIds: [], channelGroupIds: [] }).errors
        .targets,
    ).toBeTruthy()
    expect(
      validateCampaignForm({
        ...base,
        name: 'X',
        channelIds: ['ch-1'],
        scheduleMode: CAMPAIGN_SCHEDULE_MODE.ByWeekday,
        weekdays: [],
      }).errors.weekdays,
    ).toBeTruthy()
    expect(tryAddPublishTime(['09:00'], '25:00').ok).toBe(false)
    expect(tryAddPublishTime(['09:00'], '09:00').ok).toBe(false)
    expect(tryAddPublishTime(['09:00'], '15:00').ok).toBe(true)
    expect(
      validateCampaignForm({
        ...base,
        name: 'X',
        channelIds: ['ch-1'],
        jitterMinutes: 300,
      }).errors.jitterMinutes,
    ).toBeTruthy()
    expect(
      validateCampaignForm({
        ...base,
        name: 'X',
        channelIds: ['ch-1'],
        runMode: RUN_MODE_WITH_END,
        startDate: '2026-10-10',
        endDate: '2026-10-01',
      }).errors.endDate,
    ).toBeTruthy()
    expect(
      validateCampaignForm({
        ...base,
        name: 'X',
        channelIds: ['ch-1'],
        runMode: RUN_MODE_WITH_END,
        endDate: '',
      }).errors.endDate,
    ).toBeTruthy()
  })

  it('buildCampaignPayload gửi đúng khi Ảnh + Theo thứ + giờ + nhóm; luôn KeepOld', () => {
    const form = {
      ...emptyCampaignForm(),
      name: '  Camp  ',
      mediaType: CAMPAIGN_MEDIA_TYPE.Image,
      imageStrategy: 2,
      channelIds: ['ch-1'],
      channelGroupIds: ['grp-1'],
      scheduleMode: CAMPAIGN_SCHEDULE_MODE.ByWeekday,
      weekdays: [2, 4],
      publishTimes: ['09:00', '15:00'],
      jitterMinutes: 30,
      startDate: '2026-10-06',
      runMode: RUN_MODE_WITH_END,
      endDate: '2026-11-01',
    }
    expect(validateCampaignForm(form).ok).toBe(true)
    expect(buildCampaignPayload(form)).toEqual({
      name: 'Camp',
      mediaType: 1,
      imageStrategy: 1,
      channelIds: ['ch-1'],
      channelGroupIds: ['grp-1'],
      scheduleMode: 1,
      weekdays: [2, 4],
      publishTimes: ['09:00', '15:00'],
      jitterMinutes: 30,
      startDate: '2026-10-06T00:00:00.000Z',
      endDate: '2026-11-01T00:00:00.000Z',
    })

    const untilStopped = buildCampaignPayload({
      ...form,
      runMode: RUN_MODE_UNTIL_STOPPED,
      endDate: '2026-11-01',
    })
    expect(untilStopped.endDate).toBeNull()
  })

  it('không còn chiến lược hình ảnh; Theo thứ + giờ + chạy đến khi dừng; payload KeepOld', async () => {
    const user = userEvent.setup()
    const onSubmit = vi.fn()
    render(
      <QueryClientProvider client={newClient()}>
        <CampaignFormModal
          open
          onClose={() => {}}
          initialData={null}
          onSubmit={onSubmit}
          isSubmitting={false}
        />
      </QueryClientProvider>,
    )

    expect(screen.queryByTestId('image-strategy-block')).not.toBeInTheDocument()
    expect(screen.queryByText('Chiến lược hình ảnh')).not.toBeInTheDocument()
    expect(screen.getByTestId('run-until-stopped')).toBeChecked()
    expect(screen.queryByTestId('campaign-end')).not.toBeInTheDocument()

    await user.click(screen.getByLabelText('Video'))
    await user.click(screen.getByLabelText('Theo thứ'))
    expect(screen.getByTestId('weekday-picker')).toBeInTheDocument()
    await user.click(screen.getByLabelText('Thứ 2'))

    await user.type(screen.getByTestId('time-draft'), '10:30')
    await user.click(screen.getByTestId('add-time'))
    expect(screen.getByTestId('remove-time-10:30')).toBeInTheDocument()
    await user.click(screen.getByTestId('remove-time-10:30'))
    expect(screen.queryByTestId('remove-time-10:30')).not.toBeInTheDocument()

    // Giờ trùng
    await user.clear(screen.getByTestId('time-draft'))
    await user.type(screen.getByTestId('time-draft'), '09:00')
    await user.click(screen.getByTestId('add-time'))
    expect(screen.getByTestId('error-publishTimes')).toBeInTheDocument()

    await user.click(screen.getByLabelText('Ảnh'))
    await user.click(screen.getByTestId('run-with-end'))
    expect(screen.getByTestId('campaign-end')).toBeInTheDocument()
    await user.click(screen.getByTestId('run-until-stopped'))
    expect(screen.queryByTestId('campaign-end')).not.toBeInTheDocument()

    await user.type(screen.getByLabelText(/Tên/), 'UI Camp')
    await user.click(screen.getByLabelText('Nhóm A'))
    await user.click(screen.getByTestId('campaign-form-submit'))

    expect(onSubmit).toHaveBeenCalled()
    const payload = onSubmit.mock.calls[0][0]
    expect(payload.name).toBe('UI Camp')
    expect(payload.channelGroupIds).toContain('grp-1')
    expect(payload.scheduleMode).toBe(CAMPAIGN_SCHEDULE_MODE.ByWeekday)
    expect(payload.weekdays).toContain(1)
    expect(payload.mediaType).toBe(CAMPAIGN_MEDIA_TYPE.Image)
    expect(payload.imageStrategy).toBe(1)
    expect(payload.endDate).toBeNull()
  })
})
