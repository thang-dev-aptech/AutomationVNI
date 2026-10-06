import { fireEvent, render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import PostCalendar from '../components/PostCalendar'
import {
  buildMonthGrid,
  buildWeekGrid,
  startOfVnWeek,
  shiftYmd,
  toVnYmd,
  weekRangeLabel,
  withVnDate,
} from '../utils/calendarGrid'

const STATUS_SCHEDULED = 5

function renderCalendar(props = {}) {
  const onReschedule = vi.fn()
  const today = new Date()
  const year = today.getFullYear()
  const month = today.getMonth() + 1

  render(
    <MemoryRouter>
      <PostCalendar
        year={year}
        month={month}
        posts={props.posts ?? []}
        channelMap={props.channelMap ?? { 'ch-1': 'VNi Fanpage' }}
        onPrevMonth={vi.fn()}
        onNextMonth={vi.fn()}
        onToday={vi.fn()}
        onReschedule={onReschedule}
        onMonthCursorChange={vi.fn()}
        {...props}
      />
    </MemoryRouter>,
  )
  return { onReschedule }
}

/** UTC ISO for VN local wall-clock ymd + hour:minute. */
function vnLocalIso(ymd, hour, minute) {
  return withVnDate(
    // seed time so withVnDate keeps hour/minute — use a known VN morning
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

describe('calendarGrid week helpers', () => {
  it('startOfVnWeek returns Monday for a mid-week VN day', () => {
    // 2026-10-07 is Wednesday VN → week starts 2026-10-05
    expect(startOfVnWeek('2026-10-07')).toBe('2026-10-05')
    expect(startOfVnWeek('2026-10-05')).toBe('2026-10-05')
    expect(startOfVnWeek('2026-10-11')).toBe('2026-10-05')
  })

  it('buildWeekGrid headers are Thứ 2 dd … Chủ Nhật dd Mon→Sun', () => {
    const cells = buildWeekGrid('2026-10-05')
    expect(cells).toHaveLength(7)
    expect(cells[0].headerLabel).toBe('Thứ 2 05')
    expect(cells[6].headerLabel).toBe('Chủ Nhật 11')
    expect(cells.map((c) => c.ymd)).toEqual([
      '2026-10-05', '2026-10-06', '2026-10-07', '2026-10-08',
      '2026-10-09', '2026-10-10', '2026-10-11',
    ])
  })

  it('weekRangeLabel is dd/MM - dd/MM', () => {
    expect(weekRangeLabel('2026-10-05')).toBe('05/10 - 11/10')
  })

  it('withVnDate keeps hour-minute when changing day', () => {
    const original = vnLocalIso('2026-10-08', 14, 30)
    const moved = withVnDate(original, '2026-10-10')
    expect(toVnYmd(moved)).toBe('2026-10-10')
    const parts = new Intl.DateTimeFormat('en-GB', {
      timeZone: 'Asia/Ho_Chi_Minh',
      hour: '2-digit',
      minute: '2-digit',
      hour12: false,
    }).formatToParts(moved)
    const get = (t) => parts.find((p) => p.type === t)?.value
    expect(get('hour')).toBe('14')
    expect(get('minute')).toBe('30')
  })

  it('buildMonthGrid has 42 cells Monday-first', () => {
    const cells = buildMonthGrid(2026, 10)
    expect(cells).toHaveLength(42)
    // Oct 2026 starts Thursday → grid starts Mon 2026-09-28
    expect(cells[0].ymd).toBe('2026-09-28')
  })
})

describe('PostCalendar week/month — AC calendar-views-ui-test (b)', () => {
  beforeEach(() => {
    vi.useFakeTimers({ shouldAdvanceTime: true })
    // Fixed "today" = Wednesday 2026-10-07 10:00 VN = 03:00 UTC
    vi.setSystemTime(new Date('2026-10-07T03:00:00.000Z'))
  })

  afterEach(() => {
    vi.useRealTimers()
  })

  it('(b) Tuần: headers Thứ 2…Chủ Nhật, hôm nay tô, ‹ › nhảy 7 ngày', async () => {
    const user = userEvent.setup({ advanceTimers: vi.advanceTimersByTime })
    renderCalendar()

    await user.click(screen.getByRole('tab', { name: 'Tuần' }))
    expect(screen.getByTestId('week-view')).toBeInTheDocument()
    expect(screen.getByTestId('week-range-label')).toHaveTextContent('05/10 - 11/10')
    expect(screen.getByText('Thứ 2 05')).toBeInTheDocument()
    expect(screen.getByText('Chủ Nhật 11')).toBeInTheDocument()

    const todayHeader = screen.getByText('Thứ 4 07')
    expect(todayHeader.className).toMatch(/is-today/)

    await user.click(screen.getByLabelText('Tuần sau'))
    expect(screen.getByTestId('week-range-label')).toHaveTextContent('12/10 - 18/10')
    expect(screen.getByText('Thứ 2 12')).toBeInTheDocument()

    await user.click(screen.getByLabelText('Tuần trước'))
    expect(screen.getByTestId('week-range-label')).toHaveTextContent('05/10 - 11/10')
  })

  it('(b) Tháng: lưới đúng ngày; chip hiện giờ + kênh + chấm loại', async () => {
    const posts = [{
      id: 'p-sched',
      title: 'Bài lịch',
      status: STATUS_SCHEDULED,
      scheduledPublishAt: vnLocalIso('2026-10-09', 9, 15),
      socialChannelId: 'ch-1',
      postType: 'Ảnh',
    }]
    renderCalendar({ posts })

    expect(screen.getByTestId('month-grid')).toBeInTheDocument()
    expect(screen.getByTestId('month-chip-p-sched')).toBeInTheDocument()
    const chip = screen.getByTestId('month-chip-p-sched')
    expect(chip.textContent).toMatch(/09:15|9:15/)
    expect(chip.textContent).toContain('VNi Fanpage')
    expect(chip.querySelector('.post-calendar-type-dot')).toBeTruthy()
  })

  it('(b) kéo-thả Tháng sang ngày tương lai gọi onReschedule; quá khứ bị chặn', () => {
    const futureYmd = '2026-10-10'
    const pastYmd = '2026-10-05'
    const posts = [{
      id: 'p-drag',
      title: 'Kéo tôi',
      status: STATUS_SCHEDULED,
      scheduledPublishAt: vnLocalIso('2026-10-09', 16, 45),
      socialChannelId: 'ch-1',
      postType: 'Văn bản',
    }]
    const { onReschedule } = renderCalendar({ posts })

    const chip = screen.getByTestId('month-chip-p-drag')
    const futureCell = document.querySelector(`[data-ymd="${futureYmd}"]`)
    const pastCell = document.querySelector(`[data-ymd="${pastYmd}"]`)
    expect(futureCell).toBeTruthy()
    expect(pastCell).toBeTruthy()

    // Drop vào tương lai
    const dt = {
      getData: () => 'p-drag',
      setData: vi.fn(),
      effectAllowed: 'move',
      dropEffect: 'move',
    }
    fireEvent.dragStart(chip, { dataTransfer: dt })
    fireEvent.dragOver(futureCell, { dataTransfer: dt })
    fireEvent.drop(futureCell, { dataTransfer: dt })
    expect(onReschedule).toHaveBeenCalledWith(
      expect.objectContaining({ id: 'p-drag' }),
      futureYmd,
    )

    onReschedule.mockClear()
    fireEvent.dragStart(chip, { dataTransfer: dt })
    fireEvent.dragOver(pastCell, { dataTransfer: dt })
    fireEvent.drop(pastCell, { dataTransfer: dt })
    expect(onReschedule).not.toHaveBeenCalled()
  })

  it('(b) kéo-thả Tuần: tương lai gọi onReschedule, quá khứ chặn', async () => {
    const user = userEvent.setup({ advanceTimers: vi.advanceTimersByTime })
    const posts = [{
      id: 'p-week',
      title: 'Tuần kéo',
      status: STATUS_SCHEDULED,
      scheduledPublishAt: vnLocalIso('2026-10-08', 11, 0),
      socialChannelId: 'ch-1',
      postType: 'Video',
    }]
    const { onReschedule } = renderCalendar({ posts })
    await user.click(screen.getByRole('tab', { name: 'Tuần' }))

    const chip = screen.getByTestId('week-chip-p-week')
    const futureCell = document.querySelector(
      `.post-calendar-week-cell[data-ymd="2026-10-10"]`,
    )
    const pastCell = document.querySelector(
      `.post-calendar-week-cell[data-ymd="2026-10-05"]`,
    )
    const dt = {
      getData: () => 'p-week',
      setData: vi.fn(),
      effectAllowed: 'move',
      dropEffect: 'move',
    }

    fireEvent.dragStart(chip, { dataTransfer: dt })
    fireEvent.drop(futureCell, { dataTransfer: dt })
    expect(onReschedule).toHaveBeenCalledWith(
      expect.objectContaining({ id: 'p-week' }),
      '2026-10-10',
    )

    onReschedule.mockClear()
    fireEvent.dragStart(chip, { dataTransfer: dt })
    fireEvent.drop(pastCell, { dataTransfer: dt })
    expect(onReschedule).not.toHaveBeenCalled()
  })

  it('shiftYmd moves by 7 for week nav', () => {
    expect(shiftYmd('2026-10-05', 7)).toBe('2026-10-12')
    expect(shiftYmd('2026-10-05', -7)).toBe('2026-09-28')
  })
})
