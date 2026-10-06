import { cleanup, render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { createMemoryRouter, RouterProvider } from 'react-router-dom'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import PostCalendarPage from '../pages/PostCalendarPage'
import {
  mergeMonthAndWeekRangeUtc,
  monthRangeUtc,
  normalizeWeekStartYmd,
  weekRangeUtc,
  withVnDate,
} from '../utils/calendarGrid'

vi.mock('@/modules/social-channels/hooks/useSocialChannels', () => ({
  useSocialChannelAll: () => ({
    data: [{ id: 'ch-1', pageName: 'VNi Fanpage', platform: 1 }],
    isLoading: false,
  }),
}))

vi.mock('@/modules/social-channels/hooks/useChannelGroups', () => ({
  useChannelGroupAll: () => ({
    data: [],
    isLoading: false,
  }),
}))

function vnLocalIso(ymd, hour, minute) {
  return withVnDate(
    new Date(Date.UTC(
      Number(ymd.slice(0, 4)),
      Number(ymd.slice(5, 7)) - 1,
      Number(ymd.slice(8, 10)),
      hour - 7,
      minute,
    )),
    ymd,
  ).toISOString()
}

/** Posts spanning Mon 2026-09-28 … Sun 2026-10-04 (tuần vắt tháng). */
const CROSS_MONTH_POSTS = [
  {
    id: 'p-sep',
    title: 'Cuối tháng 9',
    status: 5,
    scheduledPublishAt: vnLocalIso('2026-09-29', 10, 0),
    socialChannelId: 'ch-1',
    postType: 'Văn bản',
  },
  {
    id: 'p-oct',
    title: 'Đầu tháng 10',
    status: 5,
    scheduledPublishAt: vnLocalIso('2026-10-03', 14, 0),
    socialChannelId: 'ch-1',
    postType: 'Ảnh',
  },
]

vi.mock('../hooks/useCalendarQuery', async (importOriginal) => {
  const actual = await importOriginal()
  return {
    ...actual,
    useCalendarFacets: () => ({
      data: { authors: [], categories: [] },
      isLoading: false,
      isError: false,
      refetch: vi.fn(),
    }),
    useCalendarPosts: () => ({
      data: CROSS_MONTH_POSTS,
      isLoading: false,
      isError: false,
      refetch: vi.fn(),
    }),
    useCalendarList: () => ({
      data: { items: [], total: 0, index: 1, size: 20 },
      isLoading: false,
      isError: false,
      refetch: vi.fn(),
    }),
  }
})

vi.mock('../hooks/usePosts', () => ({
  useSchedulePost: () => ({ mutate: vi.fn(), isPending: false }),
}))

function renderPage(initialEntry = '/calendar') {
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  })
  const router = createMemoryRouter(
    [{ path: '/calendar', element: <PostCalendarPage /> }],
    { initialEntries: [initialEntry] },
  )
  render(
    <QueryClientProvider client={client}>
      <RouterProvider router={router} />
    </QueryClientProvider>,
  )
  return router
}

describe('calendarGrid week URL helpers — AC calendar-week-url-test', () => {
  it('(e) normalizeWeekStartYmd: sai định dạng → ""; không phải T2 → căn T2', () => {
    expect(normalizeWeekStartYmd('')).toBe('')
    expect(normalizeWeekStartYmd('not-a-date')).toBe('')
    expect(normalizeWeekStartYmd('2026-13-01')).toBe('')
    expect(normalizeWeekStartYmd('2026-02-31')).toBe('')
    expect(normalizeWeekStartYmd('2026-10-07')).toBe('2026-10-05')
    expect(normalizeWeekStartYmd('2026-10-05')).toBe('2026-10-05')
  })

  it('(b) mergeMonthAndWeekRangeUtc mở rộng khi tuần ngoài lưới tháng cursor', () => {
    // Lưới tháng 10/2026 kết thúc ~2026-11-08; tuần 09/11–15/11 nằm ngoài.
    const monthOnly = monthRangeUtc(2026, 10)
    const weekOnly = weekRangeUtc('2026-11-09')
    const merged = mergeMonthAndWeekRangeUtc(2026, 10, '2026-11-09')
    expect(merged.fromUtc).toBe(monthOnly.fromUtc)
    expect(merged.toUtc).toBe(weekOnly.toUtc)
    expect(merged.toUtc > monthOnly.toUtc).toBe(true)
  })
})

describe('PostCalendar density/week trên URL — AC e90862c2 (calendar-week-url-test)', () => {
  beforeEach(() => {
    vi.useFakeTimers({ shouldAdvanceTime: true })
    // Wednesday 2026-10-07 10:00 VN
    vi.setSystemTime(new Date('2026-10-07T03:00:00.000Z'))
  })

  afterEach(() => {
    vi.useRealTimers()
  })

  it('(a) Chuyển Tuần → URL density=week&week=T2; refresh giữ dạng Tuần', async () => {
    const user = userEvent.setup({ advanceTimers: vi.advanceTimersByTime })
    const router = renderPage('/calendar?year=2026&month=10')

    expect(screen.getByTestId('month-grid')).toBeInTheDocument()
    await user.click(screen.getByRole('tab', { name: 'Tuần' }))

    await waitFor(() => {
      const sp = new URLSearchParams(router.state.location.search)
      expect(sp.get('density')).toBe('week')
      expect(sp.get('week')).toBe('2026-10-05')
    })
    expect(screen.getByTestId('week-view')).toBeInTheDocument()
    expect(screen.getByTestId('week-range-label')).toHaveTextContent('05/10 - 11/10')

    // Mô phỏng refresh: mount lại từ URL
    const search = router.state.location.search
    cleanup()
    renderPage(`/calendar${search}`)
    expect(screen.getByTestId('week-view')).toBeInTheDocument()
    expect(screen.getByTestId('week-range-label')).toHaveTextContent('05/10 - 11/10')
  })

  it('(b) ‹ › cập nhật week trên URL; tuần vắt tháng hiện đủ bài 7 ngày', async () => {
    const user = userEvent.setup({ advanceTimers: vi.advanceTimersByTime })
    const router = renderPage(
      '/calendar?year=2026&month=10&density=week&week=2026-09-28',
    )

    expect(screen.getByTestId('week-view')).toBeInTheDocument()
    expect(screen.getByTestId('week-range-label')).toHaveTextContent('28/09 - 04/10')
    expect(screen.getByTestId('week-chip-p-sep')).toBeInTheDocument()
    expect(screen.getByTestId('week-chip-p-oct')).toBeInTheDocument()

    await user.click(screen.getByLabelText('Tuần sau'))
    await waitFor(() => {
      const sp = new URLSearchParams(router.state.location.search)
      expect(sp.get('week')).toBe('2026-10-05')
      expect(sp.get('density')).toBe('week')
    })
    expect(screen.getByTestId('week-range-label')).toHaveTextContent('05/10 - 11/10')
  })

  it('(c) Hôm nay đưa week về tuần hiện tại', async () => {
    const user = userEvent.setup({ advanceTimers: vi.advanceTimersByTime })
    const router = renderPage(
      '/calendar?year=2026&month=10&density=week&week=2026-09-28',
    )

    await user.click(screen.getByRole('button', { name: 'Hôm nay' }))
    await waitFor(() => {
      const sp = new URLSearchParams(router.state.location.search)
      expect(sp.get('week')).toBe('2026-10-05')
      expect(sp.get('density')).toBe('week')
    })
    expect(screen.getByTestId('week-range-label')).toHaveTextContent('05/10 - 11/10')
  })

  it('(d) Chuyển Tháng → bỏ density; week không ảnh hưởng dạng Tháng', async () => {
    const user = userEvent.setup({ advanceTimers: vi.advanceTimersByTime })
    const router = renderPage(
      '/calendar?year=2026&month=10&density=week&week=2026-10-05',
    )

    expect(screen.getByTestId('week-view')).toBeInTheDocument()
    await user.click(screen.getByRole('tab', { name: 'Tháng' }))

    await waitFor(() => {
      const sp = new URLSearchParams(router.state.location.search)
      expect(sp.get('density')).toBeNull()
    })
    expect(screen.getByTestId('month-grid')).toBeInTheDocument()
    expect(screen.queryByTestId('week-view')).not.toBeInTheDocument()
  })

  it('(e) week không phải T2 trên URL → căn về T2; không crash', async () => {
    const router = renderPage(
      '/calendar?year=2026&month=10&density=week&week=2026-10-07',
    )

    await waitFor(() => {
      const sp = new URLSearchParams(router.state.location.search)
      expect(sp.get('week')).toBe('2026-10-05')
      expect(sp.get('density')).toBe('week')
    })
    expect(screen.getByTestId('week-view')).toBeInTheDocument()
    expect(screen.getByTestId('week-range-label')).toHaveTextContent('05/10 - 11/10')
  })

  it('(e) week sai định dạng → tuần mặc định; không crash', async () => {
    const router = renderPage(
      '/calendar?year=2026&month=10&density=week&week=garbage',
    )

    await waitFor(() => {
      const sp = new URLSearchParams(router.state.location.search)
      expect(sp.get('week')).toBe('2026-10-05')
    })
    expect(screen.getByTestId('week-view')).toBeInTheDocument()
  })
})
