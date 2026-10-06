import { useMemo, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { formatTimeShort } from '@/shared/utils/apiHelpers'
import { CALENDAR_STATUS_GROUPS } from '../../constants/calendarStatus'
import { buildWeekGrid, toVnYmd } from '../../utils/calendarGrid'

const STATUS_SCHEDULED = 5

const POST_TYPE_COLORS = {
  Tin: '#8b5cf6',
  'Video ngắn': '#ec4899',
  Video: '#ef4444',
  Ảnh: '#3b82f6',
  'Văn bản': '#64748b',
}

function eventTimeOf(post) {
  return post.status === STATUS_SCHEDULED
    ? post.scheduledPublishAt
    : (post.publishedAt || post.scheduledPublishAt)
}

function statusBorderColor(status) {
  const group = CALENDAR_STATUS_GROUPS.find((g) => g.statuses.includes(status))
  return group?.color ?? '#94a3b8'
}

/**
 * Lưới tuần Thứ 2→Chủ nhật (giờ VN). Cột header "Thứ 2 05"; hôm nay tô xanh;
 * kéo-thả bài Chờ đăng sang ngày tương lai (chặn quá khứ).
 */
export default function WeekView({
  weekStartYmd,
  posts = [],
  channelMap = {},
  onReschedule,
  isRescheduling = false,
}) {
  const navigate = useNavigate()
  const [dragOverYmd, setDragOverYmd] = useState(null)
  const cells = useMemo(() => buildWeekGrid(weekStartYmd), [weekStartYmd])
  const todayYmd = toVnYmd(new Date())

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

  return (
    <div className={`post-calendar-week${isRescheduling ? ' is-busy' : ''}`} data-testid="week-view">
      <div className="post-calendar-week-headers">
        {cells.map((cell) => (
          <div
            key={cell.ymd}
            className={`post-calendar-week-header${cell.isToday ? ' is-today' : ''}${cell.isWeekend ? ' is-weekend' : ''}`}
            data-ymd={cell.ymd}
          >
            {cell.headerLabel}
          </div>
        ))}
      </div>

      <div className="post-calendar-week-grid">
        {cells.map((cell) => {
          const dayPosts = postsByDay[cell.ymd] ?? []
          const classes = [
            'post-calendar-week-cell',
            cell.isToday ? 'is-today' : '',
            cell.isPast ? 'is-past' : '',
            cell.isWeekend ? 'is-weekend' : '',
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
              {dayPosts.map((post) => {
                const time = eventTimeOf(post)
                const channelName = channelMap[post.socialChannelId]
                  || post.channelName
                  || ''
                const typeColor = POST_TYPE_COLORS[post.postType] || POST_TYPE_COLORS['Văn bản']
                const border = statusBorderColor(post.status)
                const draggable = post.status === STATUS_SCHEDULED

                return (
                  <button
                    key={post.id}
                    type="button"
                    draggable={draggable}
                    onDragStart={(e) => handleDragStart(e, post)}
                    onClick={() => navigate(`/posts/${post.id}`)}
                    className={`post-calendar-chip is-rich${draggable ? ' is-draggable' : ''}`}
                    style={{ borderLeftColor: border }}
                    title={`${formatTimeShort(time)} · ${post.title}${channelName ? ` · ${channelName}` : ''}`}
                    data-testid={`week-chip-${post.id}`}
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
              })}
            </div>
          )
        })}
      </div>
    </div>
  )
}

export { eventTimeOf, POST_TYPE_COLORS, statusBorderColor }
