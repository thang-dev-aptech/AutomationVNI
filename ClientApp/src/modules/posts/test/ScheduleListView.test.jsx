import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { MemoryRouter } from 'react-router-dom'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import ScheduleListView, {
  customRangeUtc,
  listRangeUtc,
} from '../components/calendar/ScheduleListView'
import { toVnYmd } from '../utils/calendarGrid'

const confirmMock = vi.fn(() => true)

vi.mock('@/shared/utils/confirmAction', () => ({
  confirmAction: (...args) => confirmMock(...args),
}))

vi.mock('../services/postApi', () => ({
  postApi: {
    calendarList: vi.fn(),
    bulkAction: vi.fn(),
  },
  postQueryKeys: {
    calendarList: (params) => ['posts', 'calendar-list', params],
  },
}))

import { postApi } from '../services/postApi'

function renderList(filterRequest = {}) {
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  })
  render(
    <QueryClientProvider client={client}>
      <MemoryRouter>
        <ScheduleListView filterRequest={filterRequest} />
      </MemoryRouter>
    </QueryClientProvider>,
  )
}

const samplePage = {
  items: [
    {
      id: 'p1',
      title: 'Bài một',
      content: 'Nội dung demo',
      status: 5,
      scheduledPublishAt: '2026-10-08T03:00:00Z',
      channelName: 'VNi Fanpage',
      categoryName: 'Thể thao',
      authorName: 'alice',
      mediaCount: 2,
      thumbnailUrl: '/thumb1.jpg',
    },
    {
      id: 'p2',
      title: 'Bài hai',
      status: 7,
      publishedAt: '2026-10-07T02:00:00Z',
      channelName: 'TikTok Shop',
      categoryName: 'Tin',
      authorName: 'bob',
      mediaCount: 0,
    },
  ],
  total: 2,
  index: 1,
  size: 20,
}

describe('listRangeUtc helpers', () => {
  it('builds half-open VN day windows for 7/14/30', () => {
    const r7 = listRangeUtc(7, '2026-10-06')
    expect(r7.fromUtc).toBe('2026-10-05T17:00:00.000Z')
    expect(r7.toUtc).toBe('2026-10-12T17:00:00.000Z')

    const r14 = listRangeUtc(14, '2026-10-06')
    expect(r14.toUtc).toBe('2026-10-19T17:00:00.000Z')

    const custom = customRangeUtc('2026-10-01', '2026-10-03')
    expect(custom.fromUtc).toBe('2026-09-30T17:00:00.000Z')
    expect(custom.toUtc).toBe('2026-10-03T17:00:00.000Z')
  })
})

describe('ScheduleListView — AC calendar-views-ui-test (c)', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    confirmMock.mockReturnValue(true)
    postApi.calendarList.mockResolvedValue({
      data: { success: true, data: samplePage },
    })
    postApi.bulkAction.mockResolvedValue({
      data: {
        success: true,
        data: {
          results: [
            { postId: 'p1', success: true, message: 'Đã huỷ lịch' },
            { postId: 'p2', success: false, message: 'không có quyền' },
          ],
        },
      },
    })
  })

  afterEach(() => {
    vi.useRealTimers()
  })

  it('(c) renders required columns (no Rating / Cài đặt)', async () => {
    renderList()
    await screen.findByTestId('schedule-list-table')

    const table = screen.getByTestId('schedule-list-table')
    expect(within(table).getByText('Nội dung')).toBeInTheDocument()
    expect(within(table).getByText('Media')).toBeInTheDocument()
    expect(within(table).getByText('Chủ đề')).toBeInTheDocument()
    expect(within(table).getByText('Kênh đăng')).toBeInTheDocument()
    expect(within(table).getByText('Giờ đăng')).toBeInTheDocument()
    expect(within(table).getByText('Trạng thái')).toBeInTheDocument()
    expect(within(table).getByText('Người viết')).toBeInTheDocument()
    expect(within(table).queryByText('Rating')).not.toBeInTheDocument()
    expect(within(table).queryByText('Cài đặt')).not.toBeInTheDocument()

    expect(screen.getByText('Bài một')).toBeInTheDocument()
    expect(screen.getByText('VNi Fanpage')).toBeInTheDocument()
    expect(screen.getByTestId('schedule-list-showing')).toHaveTextContent(
      'Showing 1 / 2 of 2 posts',
    )
    expect(screen.getByTestId('schedule-list-page')).toHaveTextContent('Page 1 / 1')
  })

  it('(c) 7D/14D/30D send the correct UTC half-open range', async () => {
    const user = userEvent.setup()
    renderList()
    await screen.findByText('Bài một')

    const today = toVnYmd(new Date())
    const expected7 = listRangeUtc(7, today)
    await waitFor(() => {
      expect(postApi.calendarList).toHaveBeenCalled()
    })
    const firstCall = postApi.calendarList.mock.calls.at(-1)[0]
    expect(firstCall.fromUtc).toBe(expected7.fromUtc)
    expect(firstCall.toUtc).toBe(expected7.toUtc)

    postApi.calendarList.mockClear()
    await user.click(screen.getByTestId('range-14d'))
    await waitFor(() => {
      expect(postApi.calendarList).toHaveBeenCalled()
    })
    const call14 = postApi.calendarList.mock.calls.at(-1)[0]
    const expected14 = listRangeUtc(14, today)
    expect(call14.fromUtc).toBe(expected14.fromUtc)
    expect(call14.toUtc).toBe(expected14.toUtc)

    postApi.calendarList.mockClear()
    await user.click(screen.getByTestId('range-30d'))
    await waitFor(() => {
      expect(postApi.calendarList).toHaveBeenCalled()
    })
    const call30 = postApi.calendarList.mock.calls.at(-1)[0]
    const expected30 = listRangeUtc(30, today)
    expect(call30.fromUtc).toBe(expected30.fromUtc)
    expect(call30.toUtc).toBe(expected30.toUtc)
  })

  it('(c) search keyword is debounced before list request', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true })
    const user = userEvent.setup({ advanceTimers: vi.advanceTimersByTime })
    renderList()
    await screen.findByText('Bài một')
    postApi.calendarList.mockClear()

    await user.type(screen.getByTestId('schedule-list-search'), 'vni')
    expect(postApi.calendarList).not.toHaveBeenCalled()

    await vi.advanceTimersByTimeAsync(400)
    await waitFor(() => {
      expect(postApi.calendarList).toHaveBeenCalled()
    })
    const last = postApi.calendarList.mock.calls.at(-1)[0]
    expect(last.keyword).toBe('vni')
  })

  it('(c) bulk action calls endpoint and shows per-post results', async () => {
    const user = userEvent.setup()
    renderList()
    await screen.findByText('Bài một')

    await user.click(screen.getByTestId('select-p1'))
    await user.click(screen.getByTestId('select-p2'))
    await user.click(screen.getByTestId('bulk-action-trigger'))
    await user.click(screen.getByTestId('bulk-action-cancelSchedule'))

    await waitFor(() => {
      expect(postApi.bulkAction).toHaveBeenCalledWith({
        action: 'cancelSchedule',
        postIds: expect.arrayContaining(['p1', 'p2']),
      })
    })
    expect(confirmMock).toHaveBeenCalled()

    expect(await screen.findByTestId('bulk-action-results')).toBeInTheDocument()
    expect(screen.getByTestId('bulk-result-p1')).toHaveTextContent('Đã huỷ lịch')
    expect(screen.getByTestId('bulk-result-p2')).toHaveTextContent('không có quyền')
  })
})
