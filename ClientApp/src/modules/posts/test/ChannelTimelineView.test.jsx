import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { describe, expect, it, vi } from 'vitest'
import ChannelTimelineView, {
  buildTimelineCells,
  daysInMonth,
  isWeekendYmd,
} from '../components/calendar/ChannelTimelineView'
import { CHANNEL_FILTER_MODES } from '../constants/calendarStatus'

function renderTimeline(props = {}) {
  const onPrevMonth = vi.fn()
  const onNextMonth = vi.fn()
  render(
    <MemoryRouter>
      <ChannelTimelineView
        year={2026}
        month={10}
        posts={props.posts ?? []}
        channels={props.channels ?? [
          { id: 'ch-1', pageName: 'VNi Fanpage', externalPageId: '111' },
          { id: 'ch-2', pageName: 'Alpha Page', externalPageId: '222' },
        ]}
        groups={props.groups ?? []}
        channelMode={props.channelMode ?? CHANNEL_FILTER_MODES.channel}
        selectedChannelIds={props.selectedChannelIds ?? []}
        selectedGroupIds={props.selectedGroupIds ?? []}
        onPrevMonth={onPrevMonth}
        onNextMonth={onNextMonth}
        {...props}
      />
    </MemoryRouter>,
  )
  return { onPrevMonth, onNextMonth }
}

describe('ChannelTimeline helpers', () => {
  it('daysInMonth and weekend flags for Oct 2026', () => {
    expect(daysInMonth(2026, 10)).toBe(31)
    // 2026-10-03 = Saturday, 2026-10-04 = Sunday, 2026-10-05 = Monday
    expect(isWeekendYmd('2026-10-03')).toBe(true)
    expect(isWeekendYmd('2026-10-04')).toBe(true)
    expect(isWeekendYmd('2026-10-05')).toBe(false)
  })

  it('buildTimelineCells groups by row + VN day and counts postType', () => {
    const posts = [
      {
        id: 'p1',
        socialChannelId: 'ch-1',
        status: 5,
        scheduledPublishAt: '2026-10-05T03:00:00Z', // 10:00 VN Oct 5
        postType: 'Ảnh',
      },
      {
        id: 'p2',
        socialChannelId: 'ch-1',
        status: 5,
        scheduledPublishAt: '2026-10-05T04:00:00Z',
        postType: 'Video',
      },
    ]
    const cells = buildTimelineCells(posts, (p) => `c:${p.socialChannelId}`)
    const cell = cells['c:ch-1|2026-10-05']
    expect(cell.total).toBe(2)
    expect(cell.byType).toEqual({ Ảnh: 1, Video: 1 })
  })
})

describe('ChannelTimelineView — AC calendar-views-ui-test (d)', () => {
  it('(d) one row per channel, day columns = days in month, weekend class', () => {
    renderTimeline()

    expect(screen.getByTestId('channel-timeline-view')).toBeInTheDocument()
    expect(screen.getByTestId('timeline-month-label')).toHaveTextContent('Tháng 10 2026')
    expect(screen.getByTestId('timeline-row-ch-1')).toBeInTheDocument()
    expect(screen.getByTestId('timeline-row-ch-2')).toBeInTheDocument()

    const heads = document.querySelectorAll('.channel-timeline-dayhead')
    expect(heads).toHaveLength(31)
    expect(heads[0]).toHaveTextContent('1')
    expect(heads[30]).toHaveTextContent('31')

    // Oct 3 & 4 2026 are weekend
    expect(heads[2].className).toMatch(/is-weekend/)
    expect(heads[3].className).toMatch(/is-weekend/)
    expect(heads[4].className).not.toMatch(/is-weekend/)

    // Legend types
    expect(screen.getByText('Văn bản')).toBeInTheDocument()
    expect(screen.getByText('Video ngắn')).toBeInTheDocument()
    expect(screen.getByText('Tin')).toBeInTheDocument()
  })

  it('(d) cell shows colored dots + count; click opens post list', async () => {
    const user = userEvent.setup()
    const posts = [
      {
        id: 'p1',
        title: 'Ảnh sáng',
        socialChannelId: 'ch-1',
        status: 5,
        scheduledPublishAt: '2026-10-05T03:00:00Z',
        postType: 'Ảnh',
      },
      {
        id: 'p2',
        title: 'Video chiều',
        socialChannelId: 'ch-1',
        status: 5,
        scheduledPublishAt: '2026-10-05T08:00:00Z',
        postType: 'Video',
      },
    ]
    renderTimeline({ posts, selectedChannelIds: ['ch-1'] })

    const cell = screen.getByTestId('timeline-cell-ch-1-5')
    expect(within(cell).getByText('2')).toBeInTheDocument()
    expect(cell.querySelectorAll('.channel-timeline-dot')).toHaveLength(2)

    await user.click(within(cell).getByRole('button'))
    const detail = await screen.findByTestId('timeline-cell-detail')
    expect(within(detail).getByText('Ảnh sáng')).toBeInTheDocument()
    expect(within(detail).getByText('Video chiều')).toBeInTheDocument()
  })

  it('(d) group mode: one row per group aggregating member posts', () => {
    const groups = [
      {
        id: 'g1',
        name: 'Miền Bắc',
        channelCount: 2,
        channels: [
          { id: 'ch-1', pageName: 'VNi Fanpage' },
          { id: 'ch-2', pageName: 'Alpha' },
        ],
      },
    ]
    const posts = [
      {
        id: 'p1',
        title: 'Trong nhóm',
        socialChannelId: 'ch-2',
        status: 5,
        scheduledPublishAt: '2026-10-10T02:00:00Z',
        postType: 'Tin',
      },
    ]
    renderTimeline({
      channelMode: CHANNEL_FILTER_MODES.group,
      groups,
      selectedGroupIds: ['g1'],
      posts,
    })

    expect(screen.getByTestId('timeline-row-g1')).toBeInTheDocument()
    expect(screen.getByText('Miền Bắc')).toBeInTheDocument()
    expect(within(screen.getByTestId('timeline-cell-g1-10')).getByText('1')).toBeInTheDocument()
  })

  it('month nav buttons fire callbacks', async () => {
    const user = userEvent.setup()
    const { onPrevMonth, onNextMonth } = renderTimeline()
    await user.click(screen.getByLabelText('Tháng trước'))
    await user.click(screen.getByLabelText('Tháng sau'))
    expect(onPrevMonth).toHaveBeenCalledTimes(1)
    expect(onNextMonth).toHaveBeenCalledTimes(1)
  })
})
