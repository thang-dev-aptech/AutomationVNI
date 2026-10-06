import { useEffect, useMemo, useRef, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { formatTimeShort } from '@/shared/utils/apiHelpers'
import { CALENDAR_STATUS_GROUPS } from '../constants/calendarStatus'
import WeekView, {
  POST_TYPE_COLORS,
  eventTimeOf,
  statusBorderColor,
} from './calendar/WeekView'
import {
  WEEKDAY_LABELS,
  buildMonthGrid,
  monthLabel,
  shiftYmd,
  startOfVnWeek,
  toVnYmd,
  weekRangeLabel,
} from '../utils/calendarGrid'
import './PostCalendar.css'

/** Số bài hiện tối đa trong 1 ô tháng trước khi thu gọn thành "+N bài". */
const MAX_VISIBLE_PER_DAY = 3
const STATUS_SCHEDULED = 5

function toneOf(post, isOverdue) {
  if (isOverdue) return 'overdue'
  const group = CALENDAR_STATUS_GROUPS.find((g) => g.statuses.includes(post.status))
  if (group?.key === 'published') return 'published'
  if (group?.key === 'failed') return 'failed'
  return 'scheduled'
}

/** Tuần mặc định khi mở dạng Tuần: tuần chứa hôm nay nếu cùng tháng cursor, không thì tuần ngày 1. */
function defaultWeekStart(year, month) {
  const todayYmd = toVnYmd(new Date())
  const [ty, tm] = todayYmd.split('-').map(Number)
  if (ty === year && tm === month) return startOfVnWeek(todayYmd)
  return startOfVnWeek(`${year}-${String(month).padStart(2, '0')}-01`)
}

/**
 * Lịch Tuần / Tháng (giờ VN). Toolbar: Tuần|Tháng, ‹ ›, Hôm nay.
 * Kéo-thả bài Chờ đăng sang ngày tương lai giữ giờ; chặn quá khứ.
 */
export default function PostCalendar({
  year,
  month,
  posts = [],
  channelMap = {},
  onPrevMonth,
  onNextMonth,
  onToday,
  onReschedule,
  isRescheduling = false,
  onMonthCursorChange,
}) {
  const navigate = useNavigate()
  const [density, setDensity] = useState('month')
  const [weekStartYmd, setWeekStartYmd] = useState(() => defaultWeekStart(year, month))
  const [expandedDay, setExpandedDay] = useState(null)
  const [dragOverYmd, setDragOverYmd] = useState(null)
  const monthCursorRef = useRef({ year, month })

  // Khi cursor tháng đổi từ URL/toolbar — căn tuần về tuần hợp lý của tháng mới.
  useEffect(() => {
    if (monthCursorRef.current.year === year && monthCursorRef.current.month === month) return
    monthCursorRef.current = { year, month }
    setWeekStartYmd(defaultWeekStart(year, month))
  }, [year, month])

  const cells = useMemo(() => buildMonthGrid(year, month), [year, month])

  const postsByDay = useMemo(() => {
    const map = {}
    for (const post of posts) {
      const time = eventTimeOf(post)
      if (!time) continue
      const ymd = toVnYmd(time)
      if (!ymd) continue
      if (!map[ymd]) map[ymd] = []
      map[ymd].push(post)
    }
    for (const list of Object.values(map)) {
      list.sort((a, b) => new Date(eventTimeOf(a)) - new Date(eventTimeOf(b)))
    }
    return map
  }, [posts])

  const todayYmd = toVnYmd(new Date())

  function syncMonthFromYmd(ymd) {
    const [y, m] = ymd.split('-').map(Number)
    if (y !== year || m !== month) {
      onMonthCursorChange?.(y, m)
    }
  }

  function goToday() {
    const start = startOfVnWeek(new Date())
    setWeekStartYmd(start)
    const [y, m] = todayYmd.split('-').map(Number)
    onToday?.()
    onMonthCursorChange?.(y, m)
  }

  function shiftWeek(deltaWeeks) {
    const next = shiftYmd(weekStartYmd, deltaWeeks * 7)
    setWeekStartYmd(next)
    syncMonthFromYmd(next)
  }

  function handleDragStart(event, post) {
    event.dataTransfer.setData('text/plain', post.id)
    event.dataTransfer.effectAllowed = 'move'
  }

  function handleDragOver(event, cell) {
    if (cell.isPast) return
    event.preventDefault()
    event.dataTransfer.dropEffect = 'move'
    if (dragOverYmd !== cell.ymd) setDragOverYmd(cell.ymd)
  }

  function handleDrop(event, cell) {
    event.preventDefault()
    setDragOverYmd(null)
    if (cell.isPast) return

    const postId = event.dataTransfer.getData('text/plain')
    const post = posts.find((p) => p.id === postId)
    if (!post || post.status !== STATUS_SCHEDULED) return
    if (toVnYmd(eventTimeOf(post)) === cell.ymd) return

    onReschedule?.(post, cell.ymd)
  }

  function renderChip(post) {
    const time = eventTimeOf(post)
    const isOverdue = post.status === STATUS_SCHEDULED && toVnYmd(time) < todayYmd
    const draggable = post.status === STATUS_SCHEDULED
    const channelName = channelMap[post.socialChannelId] || post.channelName || ''
    const typeColor = POST_TYPE_COLORS[post.postType] || POST_TYPE_COLORS['Văn bản']
    const border = statusBorderColor(post.status)

    return (
      <button
        key={post.id}
        type="button"
        draggable={draggable}
        onDragStart={(e) => handleDragStart(e, post)}
        onClick={() => navigate(`/posts/${post.id}`)}
        className={`post-calendar-chip is-rich tone-${toneOf(post, isOverdue)}${draggable ? ' is-draggable' : ''}`}
        style={{ borderLeftColor: border }}
        title={`${formatTimeShort(time)} · ${post.title}${
          channelName ? ` · ${channelName}` : ''
        }${isOverdue ? ' · Quá hạn chưa đăng' : ''}`}
        data-testid={`month-chip-${post.id}`}
      >
        <span className="post-calendar-chip-time">{formatTimeShort(time)}</span>
        <span
          className="post-calendar-type-dot"
          style={{ background: typeColor }}
          title={post.postType || 'Văn bản'}
          aria-hidden
        />
        {channelName ? (
          <span className="post-calendar-chip-channel">{channelName}</span>
        ) : null}
        <span className="post-calendar-chip-title">{post.title}</span>
      </button>
    )
  }

  return (
    <div className="post-calendar" data-testid="post-calendar">
      <div className="post-calendar-toolbar">
        <div className="post-calendar-density" role="tablist" aria-label="Dạng lịch">
          <button
            type="button"
            role="tab"
            aria-selected={density === 'week'}
            className={`post-calendar-density-btn${density === 'week' ? ' is-active' : ''}`}
            onClick={() => {
              setWeekStartYmd(defaultWeekStart(year, month))
              setDensity('week')
            }}
          >
            Tuần
          </button>
          <button
            type="button"
            role="tab"
            aria-selected={density === 'month'}
            className={`post-calendar-density-btn${density === 'month' ? ' is-active' : ''}`}
            onClick={() => setDensity('month')}
          >
            Tháng
          </button>
        </div>

        <div className="post-calendar-nav">
          {density === 'week' ? (
            <>
              <button
                type="button"
                className="btn btn-ghost btn-sm"
                onClick={() => shiftWeek(-1)}
                aria-label="Tuần trước"
              >
                ‹
              </button>
              <span className="post-calendar-month" data-testid="week-range-label">
                {weekRangeLabel(weekStartYmd)}
              </span>
              <button
                type="button"
                className="btn btn-ghost btn-sm"
                onClick={() => shiftWeek(1)}
                aria-label="Tuần sau"
              >
                ›
              </button>
            </>
          ) : (
            <>
              <button type="button" className="btn btn-ghost btn-sm" onClick={onPrevMonth} aria-label="Tháng trước">
                ‹
              </button>
              <span className="post-calendar-month">{monthLabel(year, month)}</span>
              <button type="button" className="btn btn-ghost btn-sm" onClick={onNextMonth} aria-label="Tháng sau">
                ›
              </button>
            </>
          )}
        </div>

        <button type="button" className="btn btn-secondary btn-sm" onClick={goToday}>
          Hôm nay
        </button>
      </div>

      {density === 'week' ? (
        <WeekView
          weekStartYmd={weekStartYmd}
          posts={posts}
          channelMap={channelMap}
          onReschedule={onReschedule}
          isRescheduling={isRescheduling}
        />
      ) : (
        <>
          <div className="post-calendar-weekdays">
            {WEEKDAY_LABELS.map((label) => (
              <div key={label} className="post-calendar-weekday">{label}</div>
            ))}
          </div>

          <div
            className={`post-calendar-grid${isRescheduling ? ' is-busy' : ''}`}
            data-testid="month-grid"
          >
            {cells.map((cell) => {
              const dayPosts = postsByDay[cell.ymd] ?? []
              const isExpanded = expandedDay === cell.ymd
              const visible = isExpanded ? dayPosts : dayPosts.slice(0, MAX_VISIBLE_PER_DAY)
              const hiddenCount = dayPosts.length - visible.length

              const classes = [
                'post-calendar-cell',
                cell.isCurrentMonth ? '' : 'is-outside',
                cell.isToday ? 'is-today' : '',
                cell.isPast ? 'is-past' : '',
                dragOverYmd === cell.ymd ? 'is-drop-target' : '',
              ].filter(Boolean).join(' ')

              return (
                <div
                  key={cell.ymd}
                  className={classes}
                  data-ymd={cell.ymd}
                  onDragOver={(e) => handleDragOver(e, cell)}
                  onDragLeave={() => setDragOverYmd((prev) => (prev === cell.ymd ? null : prev))}
                  onDrop={(e) => handleDrop(e, cell)}
                >
                  <div className="post-calendar-daynum">{cell.day}</div>
                  <div className="post-calendar-events">
                    {visible.map((post) => renderChip(post))}

                    {hiddenCount > 0 && (
                      <button
                        type="button"
                        className="post-calendar-more"
                        onClick={() => setExpandedDay(cell.ymd)}
                      >
                        +{hiddenCount} bài
                      </button>
                    )}

                    {isExpanded && dayPosts.length > MAX_VISIBLE_PER_DAY && (
                      <button
                        type="button"
                        className="post-calendar-more"
                        onClick={() => setExpandedDay(null)}
                      >
                        Thu gọn
                      </button>
                    )}
                  </div>
                </div>
              )
            })}
          </div>
        </>
      )}
    </div>
  )
}
