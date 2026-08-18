import { useMemo, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { formatTimeShort } from '@/shared/utils/apiHelpers'
import {
  WEEKDAY_LABELS,
  buildMonthGrid,
  monthLabel,
  toVnYmd,
} from '../utils/calendarGrid'
import './PostCalendar.css'

/** Số bài hiện tối đa trong 1 ô trước khi thu gọn thành "+N bài". */
const MAX_VISIBLE_PER_DAY = 3

const STATUS_SCHEDULED = 5
const STATUS_PUBLISHED = 7
const STATUS_FAILED = 8

/**
 * Mốc thời gian dùng để xếp bài vào ô ngày: bài chưa đăng thì theo giờ đã lên lịch,
 * bài đã đăng thì theo giờ đăng thật.
 */
function eventTimeOf(post) {
  return post.status === STATUS_SCHEDULED
    ? post.scheduledPublishAt
    : (post.publishedAt || post.scheduledPublishAt)
}

function toneOf(post, isOverdue) {
  if (isOverdue) return 'overdue'
  if (post.status === STATUS_PUBLISHED) return 'published'
  if (post.status === STATUS_FAILED) return 'failed'
  return 'scheduled'
}

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
}) {
  const navigate = useNavigate()
  const [expandedDay, setExpandedDay] = useState(null)
  const [dragOverYmd, setDragOverYmd] = useState(null)

  const cells = useMemo(() => buildMonthGrid(year, month), [year, month])

  /** Gom bài vào từng ô theo ngày VN, mỗi ô đã sắp xếp theo giờ tăng dần. */
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

  function handleDragStart(event, post) {
    event.dataTransfer.setData('text/plain', post.id)
    event.dataTransfer.effectAllowed = 'move'
  }

  function handleDragOver(event, cell) {
    // Ngày quá khứ không nhận thả: backend từ chối mọi lịch <= hiện tại, chặn sớm ở đây
    // để khỏi tốn một vòng gọi API chỉ để nhận lỗi.
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
    if (!post) return
    if (toVnYmd(eventTimeOf(post)) === cell.ymd) return // thả lại đúng chỗ cũ

    onReschedule?.(post, cell.ymd)
  }

  return (
    <div className="post-calendar">
      <div className="post-calendar-toolbar">
        <div className="post-calendar-nav">
          <button type="button" className="btn btn-ghost btn-sm" onClick={onPrevMonth}>‹</button>
          <span className="post-calendar-month">{monthLabel(year, month)}</span>
          <button type="button" className="btn btn-ghost btn-sm" onClick={onNextMonth}>›</button>
        </div>
        <button type="button" className="btn btn-secondary btn-sm" onClick={onToday}>
          Hôm nay
        </button>
      </div>

      <div className="post-calendar-weekdays">
        {WEEKDAY_LABELS.map((label) => (
          <div key={label} className="post-calendar-weekday">{label}</div>
        ))}
      </div>

      <div className={`post-calendar-grid${isRescheduling ? ' is-busy' : ''}`}>
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
              onDragOver={(e) => handleDragOver(e, cell)}
              onDragLeave={() => setDragOverYmd((prev) => (prev === cell.ymd ? null : prev))}
              onDrop={(e) => handleDrop(e, cell)}
            >
              <div className="post-calendar-daynum">{cell.day}</div>

              <div className="post-calendar-events">
                {visible.map((post) => {
                  const time = eventTimeOf(post)
                  const isOverdue = post.status === STATUS_SCHEDULED
                    && toVnYmd(time) < todayYmd
                  const draggable = post.status === STATUS_SCHEDULED

                  return (
                    <button
                      key={post.id}
                      type="button"
                      draggable={draggable}
                      onDragStart={(e) => handleDragStart(e, post)}
                      onClick={() => navigate(`/posts/${post.id}`)}
                      className={`post-calendar-chip tone-${toneOf(post, isOverdue)}${draggable ? ' is-draggable' : ''}`}
                      title={`${formatTimeShort(time)} · ${post.title}${
                        channelMap[post.socialChannelId] ? ` · ${channelMap[post.socialChannelId]}` : ''
                      }${isOverdue ? ' · Quá hạn chưa đăng' : ''}`}
                    >
                      <span className="post-calendar-chip-time">{formatTimeShort(time)}</span>
                      <span className="post-calendar-chip-title">{post.title}</span>
                    </button>
                  )
                })}

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
    </div>
  )
}
