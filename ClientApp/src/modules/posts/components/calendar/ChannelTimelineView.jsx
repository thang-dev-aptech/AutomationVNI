import { useMemo, useState } from 'react'
import { Link } from 'react-router-dom'
import Modal from '@/shared/components/Modal'
import { formatDateTime, formatTimeShort } from '@/shared/utils/apiHelpers'
import { sortChannelsVniFirst } from '@/shared/utils/channelSort'
import { CALENDAR_POST_TYPES, CHANNEL_FILTER_MODES } from '../../constants/calendarStatus'
import { toVnYmd } from '../../utils/calendarGrid'
import './ChannelTimelineView.css'

/** Màu chú giải loại bài (backend PostTypeClassifier — FE không suy ra). */
export const TIMELINE_TYPE_COLORS = {
  'Văn bản': '#5b21b6',
  Ảnh: '#eab308',
  Video: '#2563eb',
  'Video ngắn': '#f97316',
  Tin: '#0d9488',
}

const STATUS_SCHEDULED = 5

export function eventTimeOf(post) {
  return post.status === STATUS_SCHEDULED
    ? post.scheduledPublishAt
    : (post.publishedAt || post.scheduledPublishAt)
}

/** Số ngày trong tháng (lịch dân sự, khớp lưới ngày 1..N). */
export function daysInMonth(year, month) {
  return new Date(Date.UTC(year, month, 0)).getUTCDate()
}

/** true nếu ngày civil YYYY-MM-DD là T7 hoặc CN (UTC date math). */
export function isWeekendYmd(ymd) {
  const [y, m, d] = ymd.split('-').map(Number)
  const dow = new Date(Date.UTC(y, m - 1, d)).getUTCDay()
  return dow === 0 || dow === 6
}

function pad2(n) {
  return String(n).padStart(2, '0')
}

function dayYmd(year, month, day) {
  return `${year}-${pad2(month)}-${pad2(day)}`
}

function monthTitle(year, month) {
  return `Tháng ${pad2(month)} ${year}`
}

/**
 * Gom bài theo rowKey + ymd; mỗi ô: { total, byType, posts }.
 */
export function buildTimelineCells(posts, rowKeyOf) {
  const map = {}
  for (const post of posts ?? []) {
    const time = eventTimeOf(post)
    if (!time) continue
    const ymd = toVnYmd(time)
    if (!ymd) continue
    const rowKey = rowKeyOf(post)
    if (!rowKey) continue
    const key = `${rowKey}|${ymd}`
    if (!map[key]) map[key] = { total: 0, byType: {}, posts: [] }
    const type = post.postType || 'Văn bản'
    map[key].total += 1
    map[key].byType[type] = (map[key].byType[type] || 0) + 1
    map[key].posts.push(post)
  }
  for (const cell of Object.values(map)) {
    cell.posts.sort((a, b) => new Date(eventTimeOf(a)) - new Date(eventTimeOf(b)))
  }
  return map
}

/**
 * Lưới Theo kênh: hàng = kênh (hoặc nhóm), cột = ngày 1..cuối tháng.
 * Cuối tuần tô nền; ô = chấm màu loại bài + số; bấm mở danh sách.
 */
export default function ChannelTimelineView({
  year,
  month,
  posts = [],
  channels = [],
  groups = [],
  channelMode = CHANNEL_FILTER_MODES.channel,
  selectedChannelIds = [],
  selectedGroupIds = [],
  onPrevMonth,
  onNextMonth,
}) {
  const [detail, setDetail] = useState(null)

  const dayCount = daysInMonth(year, month)
  const days = useMemo(
    () => Array.from({ length: dayCount }, (_, i) => {
      const day = i + 1
      const ymd = dayYmd(year, month, day)
      return { day, ymd, isWeekend: isWeekendYmd(ymd) }
    }),
    [year, month, dayCount],
  )

  const rows = useMemo(() => {
    if (channelMode === CHANNEL_FILTER_MODES.group) {
      const list = (groups ?? []).filter((g) =>
        selectedGroupIds.length === 0 || selectedGroupIds.includes(g.id),
      )
      return list
        .slice()
        .sort((a, b) => (a.name || '').localeCompare(b.name || '', 'vi'))
        .map((g) => ({
          key: `g:${g.id}`,
          kind: 'group',
          id: g.id,
          label: g.name,
          memberIds: new Set((g.channels ?? []).map((c) => c.id)),
          subtitle: `${g.channelCount ?? g.channels?.length ?? 0} kênh`,
        }))
    }

    const selected = new Set(selectedChannelIds)
    let list = sortChannelsVniFirst(channels)
    if (selected.size > 0) {
      list = list.filter((c) => selected.has(c.id))
    }
    return list.map((c) => ({
      key: `c:${c.id}`,
      kind: 'channel',
      id: c.id,
      label: c.pageName || c.name || c.id,
      subtitle: c.externalPageId ? `@${c.externalPageId}` : null,
      avatarLetter: (c.pageName || '?').trim().charAt(0).toUpperCase(),
    }))
  }, [channelMode, channels, groups, selectedChannelIds, selectedGroupIds])

  const cells = useMemo(() => {
    if (channelMode === CHANNEL_FILTER_MODES.group) {
      // Bài thuộc nhiều nhóm đã chọn → đếm vào từng nhóm (không chỉ nhóm đầu).
      const map = {}
      const bump = (rowKey, post, ymd) => {
        const key = `${rowKey}|${ymd}`
        if (!map[key]) map[key] = { total: 0, byType: {}, posts: [] }
        const type = post.postType || 'Văn bản'
        map[key].total += 1
        map[key].byType[type] = (map[key].byType[type] || 0) + 1
        map[key].posts.push(post)
      }
      for (const post of posts ?? []) {
        const time = eventTimeOf(post)
        if (!time) continue
        const ymd = toVnYmd(time)
        if (!ymd) continue
        for (const row of rows) {
          if (row.memberIds?.has(post.socialChannelId)) bump(row.key, post, ymd)
        }
      }
      for (const cell of Object.values(map)) {
        cell.posts.sort((a, b) => new Date(eventTimeOf(a)) - new Date(eventTimeOf(b)))
      }
      return map
    }
    return buildTimelineCells(posts, (post) => (
      post.socialChannelId ? `c:${post.socialChannelId}` : null
    ))
  }, [posts, channelMode, rows])

  function openCell(row, day) {
    const cell = cells[`${row.key}|${day.ymd}`]
    if (!cell?.posts?.length) return
    setDetail({
      title: `${row.label} · ${day.day}/${month}/${year}`,
      posts: cell.posts,
    })
  }

  return (
    <div className="channel-timeline" data-testid="channel-timeline-view">
      <div className="channel-timeline-toolbar">
        <div className="channel-timeline-nav">
          <button
            type="button"
            className="btn btn-ghost btn-sm"
            onClick={onPrevMonth}
            aria-label="Tháng trước"
          >
            ‹
          </button>
          <span className="channel-timeline-month" data-testid="timeline-month-label">
            {monthTitle(year, month)}
          </span>
          <button
            type="button"
            className="btn btn-ghost btn-sm"
            onClick={onNextMonth}
            aria-label="Tháng sau"
          >
            ›
          </button>
        </div>

        <div className="channel-timeline-legend" aria-label="Chú giải loại bài">
          {CALENDAR_POST_TYPES.map((type) => (
            <span key={type} className="channel-timeline-legend-item">
              <span
                className="channel-timeline-dot"
                style={{ background: TIMELINE_TYPE_COLORS[type] }}
                aria-hidden
              />
              {type}
            </span>
          ))}
        </div>
      </div>

      <div className="channel-timeline-scroll" data-testid="timeline-scroll">
        <table className="channel-timeline-grid">
          <thead>
            <tr>
              <th className="channel-timeline-corner" scope="col">
                Lịch đăng bài
              </th>
              <th
                className="channel-timeline-month-span"
                colSpan={dayCount}
                scope="colgroup"
              >
                {monthTitle(year, month)}
              </th>
            </tr>
            <tr>
              <th className="channel-timeline-corner is-sub" scope="col" aria-hidden />
              {days.map((d) => (
                <th
                  key={d.ymd}
                  scope="col"
                  className={`channel-timeline-dayhead${d.isWeekend ? ' is-weekend' : ''}`}
                  data-ymd={d.ymd}
                >
                  {d.day}
                </th>
              ))}
            </tr>
          </thead>
          <tbody>
            {rows.length === 0 ? (
              <tr>
                <td className="channel-timeline-corner" colSpan={dayCount + 1}>
                  <div className="channel-timeline-empty">
                    Không có kênh/nhóm để hiển thị. Chọn kênh hoặc nhóm ở bộ lọc.
                  </div>
                </td>
              </tr>
            ) : (
              rows.map((row) => (
                <tr key={row.key} data-testid={`timeline-row-${row.id}`}>
                  <th scope="row" className="channel-timeline-channel">
                    <div className="channel-timeline-channel-inner">
                      {row.kind === 'channel' ? (
                        <span className="channel-timeline-avatar" aria-hidden>
                          {row.avatarLetter}
                        </span>
                      ) : (
                        <span className="channel-timeline-avatar is-group" aria-hidden>
                          G
                        </span>
                      )}
                      <span className="channel-timeline-channel-text">
                        <span className="channel-timeline-channel-name">{row.label}</span>
                        {row.subtitle ? (
                          <span className="channel-timeline-channel-sub">{row.subtitle}</span>
                        ) : null}
                      </span>
                    </div>
                  </th>
                  {days.map((d) => {
                    const cell = cells[`${row.key}|${d.ymd}`]
                    const types = cell
                      ? Object.keys(cell.byType).sort(
                        (a, b) => CALENDAR_POST_TYPES.indexOf(a) - CALENDAR_POST_TYPES.indexOf(b),
                      )
                      : []
                    const clickable = Boolean(cell?.total)
                    return (
                      <td
                        key={d.ymd}
                        className={[
                          'channel-timeline-cell',
                          d.isWeekend ? 'is-weekend' : '',
                          clickable ? 'is-clickable' : '',
                        ].filter(Boolean).join(' ')}
                        data-ymd={d.ymd}
                        data-testid={`timeline-cell-${row.id}-${d.day}`}
                      >
                        {clickable ? (
                          <button
                            type="button"
                            className="channel-timeline-cell-btn"
                            onClick={() => openCell(row, d)}
                            title={`${cell.total} bài`}
                          >
                            <span className="channel-timeline-dots">
                              {types.map((type) => (
                                <span
                                  key={type}
                                  className="channel-timeline-dot"
                                  style={{ background: TIMELINE_TYPE_COLORS[type] || '#94a3b8' }}
                                  title={type}
                                />
                              ))}
                            </span>
                            <span className="channel-timeline-count">{cell.total}</span>
                          </button>
                        ) : null}
                      </td>
                    )
                  })}
                </tr>
              ))
            )}
          </tbody>
        </table>
      </div>

      <Modal
        open={Boolean(detail)}
        title={detail?.title || ''}
        onClose={() => setDetail(null)}
      >
        {detail ? (
          <ul className="channel-timeline-detail-list" data-testid="timeline-cell-detail">
            {detail.posts.map((post) => (
              <li key={post.id}>
                <Link to={`/posts/${post.id}`} onClick={() => setDetail(null)}>
                  <strong>{post.title || '(Không tiêu đề)'}</strong>
                </Link>
                <span className="channel-timeline-detail-meta">
                  {formatTimeShort(eventTimeOf(post)) || formatDateTime(eventTimeOf(post))}
                  {' · '}
                  <span
                    className="channel-timeline-dot"
                    style={{
                      background: TIMELINE_TYPE_COLORS[post.postType] || '#94a3b8',
                      display: 'inline-block',
                      verticalAlign: 'middle',
                    }}
                  />
                  {' '}
                  {post.postType || 'Văn bản'}
                </span>
              </li>
            ))}
          </ul>
        ) : null}
      </Modal>
    </div>
  )
}
