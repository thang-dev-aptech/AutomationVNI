import { useEffect, useMemo, useState } from 'react'
import { Link } from 'react-router-dom'
import { useQuery } from '@tanstack/react-query'
import LoadingState from '@/shared/components/LoadingState'
import ErrorState from '@/shared/components/ErrorState'
import EmptyState from '@/shared/components/EmptyState'
import { formatDateTime, getErrorMessage, unwrapApiData } from '@/shared/utils/apiHelpers'
import PostStatusBadge from '../PostStatusBadge'
import { postApi, postQueryKeys } from '../../services/postApi'
import { shiftYmd, toVnYmd } from '../../utils/calendarGrid'
import BulkActionMenu from './BulkActionMenu'
import './ScheduleListView.css'

const PAGE_SIZE = 20
const DEBOUNCE_MS = 350
const RANGE_PRESETS = [
  { key: '7d', label: '7D', days: 7 },
  { key: '14d', label: '14D', days: 14 },
  { key: '30d', label: '30D', days: 30 },
]

const VN_OFFSET_MS = 7 * 60 * 60 * 1000

function vnMidnightUtc(ymd) {
  const [y, m, d] = ymd.split('-').map(Number)
  return new Date(Date.UTC(y, m - 1, d) - VN_OFFSET_MS)
}

/** Khoảng nửa mở [today, today+N) theo giờ VN → UTC. */
export function listRangeUtc(days, todayYmd = toVnYmd(new Date())) {
  const endYmd = shiftYmd(todayYmd, days)
  return {
    fromUtc: vnMidnightUtc(todayYmd).toISOString(),
    toUtc: vnMidnightUtc(endYmd).toISOString(),
  }
}

/** Khoảng tuỳ chọn [fromYmd, toYmd+1) theo giờ VN. */
export function customRangeUtc(fromYmd, toYmd) {
  if (!fromYmd || !toYmd || fromYmd > toYmd) return null
  return {
    fromUtc: vnMidnightUtc(fromYmd).toISOString(),
    toUtc: vnMidnightUtc(shiftYmd(toYmd, 1)).toISOString(),
  }
}

function eventTimeOf(post) {
  return post.publishedAt || post.scheduledPublishAt || post.createdAt
}

/**
 * Bảng danh sách bài theo lịch.
 * Không có cột Rating / Cài đặt (invariant calendar-views-ui).
 */
export default function ScheduleListView({
  filterRequest = {},
  initialRange = '7d',
}) {
  const [rangeKey, setRangeKey] = useState(initialRange)
  const [customFrom, setCustomFrom] = useState(() => toVnYmd(new Date()))
  const [customTo, setCustomTo] = useState(() => shiftYmd(toVnYmd(new Date()), 6))
  const [searchInput, setSearchInput] = useState(filterRequest.keyword ?? '')
  const [debouncedKeyword, setDebouncedKeyword] = useState(filterRequest.keyword ?? '')
  const [page, setPage] = useState(1)
  const [selected, setSelected] = useState(() => new Set())

  useEffect(() => {
    const t = setTimeout(() => setDebouncedKeyword(searchInput.trim()), DEBOUNCE_MS)
    return () => clearTimeout(t)
  }, [searchInput])

  // Đổi bộ lọc sidebar / khoảng / keyword → về trang 1, bỏ chọn.
  useEffect(() => {
    setPage(1)
    setSelected(new Set())
  }, [
    rangeKey,
    customFrom,
    customTo,
    debouncedKeyword,
    filterRequest.statuses,
    filterRequest.socialChannelIds,
    filterRequest.channelGroupIds,
    filterRequest.authors,
    filterRequest.categoryIds,
    filterRequest.postTypes,
  ])

  const dateRange = useMemo(() => {
    if (rangeKey === 'custom') {
      return customRangeUtc(customFrom, customTo)
        ?? listRangeUtc(7)
    }
    const preset = RANGE_PRESETS.find((p) => p.key === rangeKey) ?? RANGE_PRESETS[0]
    return listRangeUtc(preset.days)
  }, [rangeKey, customFrom, customTo])

  const listRequest = useMemo(() => ({
    ...filterRequest,
    fromUtc: dateRange.fromUtc,
    toUtc: dateRange.toUtc,
    keyword: debouncedKeyword || undefined,
    index: page,
    size: PAGE_SIZE,
  }), [filterRequest, dateRange, debouncedKeyword, page])

  const listQuery = useQuery({
    queryKey: postQueryKeys.calendarList(listRequest),
    queryFn: async () => unwrapApiData(await postApi.calendarList(listRequest)),
    enabled: Boolean(listRequest.fromUtc && listRequest.toUtc),
  })

  const items = listQuery.data?.items ?? []
  const total = listQuery.data?.total ?? 0
  const totalPages = Math.max(1, Math.ceil(total / PAGE_SIZE) || 1)
  const fromIdx = total === 0 ? 0 : (page - 1) * PAGE_SIZE + 1
  const toIdx = total === 0 ? 0 : Math.min(page * PAGE_SIZE, total)

  const postsById = useMemo(
    () => Object.fromEntries(items.map((p) => [p.id, p])),
    [items],
  )

  const allChecked = items.length > 0 && items.every((p) => selected.has(p.id))

  function toggleAll() {
    if (allChecked) {
      setSelected(new Set())
      return
    }
    setSelected(new Set(items.map((p) => p.id)))
  }

  function toggleOne(id) {
    setSelected((prev) => {
      const next = new Set(prev)
      if (next.has(id)) next.delete(id)
      else next.add(id)
      return next
    })
  }

  return (
    <div className="schedule-list" data-testid="schedule-list-view">
      <div className="schedule-list-toolbar">
        <BulkActionMenu
          selectedIds={[...selected]}
          postsById={postsById}
          onDone={() => {
            setSelected(new Set())
            listQuery.refetch()
          }}
        />

        <div className="schedule-list-ranges" role="group" aria-label="Khoảng thời gian">
          {RANGE_PRESETS.map((preset) => (
            <button
              key={preset.key}
              type="button"
              className={`schedule-list-range-btn${rangeKey === preset.key ? ' is-active' : ''}`}
              onClick={() => setRangeKey(preset.key)}
              data-testid={`range-${preset.key}`}
            >
              {preset.label}
            </button>
          ))}
          <button
            type="button"
            className={`schedule-list-range-btn${rangeKey === 'custom' ? ' is-active' : ''}`}
            onClick={() => setRangeKey('custom')}
            data-testid="range-custom"
          >
            Khoảng
          </button>
        </div>

        {rangeKey === 'custom' ? (
          <div className="schedule-list-custom-range" data-testid="custom-range-inputs">
            <label>
              Từ
              <input
                type="date"
                value={customFrom}
                onChange={(e) => setCustomFrom(e.target.value)}
              />
            </label>
            <label>
              Đến
              <input
                type="date"
                value={customTo}
                onChange={(e) => setCustomTo(e.target.value)}
              />
            </label>
          </div>
        ) : null}

        <label className="schedule-list-search">
          <span className="sr-only">Tìm kiếm</span>
          <input
            type="search"
            placeholder="Search"
            value={searchInput}
            onChange={(e) => setSearchInput(e.target.value)}
            data-testid="schedule-list-search"
          />
        </label>
      </div>

      <h2 className="schedule-list-title">
        DANH SÁCH BÀI ĐĂNG THEO LỊCH ({total})
      </h2>

      {listQuery.isLoading && <LoadingState />}
      {listQuery.isError && (
        <ErrorState
          message={getErrorMessage(listQuery.error)}
          onRetry={listQuery.refetch}
        />
      )}

      {!listQuery.isLoading && !listQuery.isError && (
        <>
          <div className="schedule-list-table-wrap">
            <table className="schedule-list-table" data-testid="schedule-list-table">
              <thead>
                <tr>
                  <th scope="col" className="col-check">
                    <input
                      type="checkbox"
                      checked={allChecked}
                      onChange={toggleAll}
                      aria-label="Chọn tất cả trên trang"
                      data-testid="select-all"
                    />
                  </th>
                  <th scope="col">Nội dung</th>
                  <th scope="col">Media</th>
                  <th scope="col">Chủ đề</th>
                  <th scope="col">Kênh đăng</th>
                  <th scope="col">Giờ đăng</th>
                  <th scope="col">Trạng thái</th>
                  <th scope="col">Người viết</th>
                </tr>
              </thead>
              <tbody>
                {items.length === 0 ? (
                  <tr>
                    <td colSpan={8}>
                      <EmptyState title="No Rows To Show" />
                    </td>
                  </tr>
                ) : (
                  items.map((post) => (
                    <tr key={post.id} data-testid={`schedule-row-${post.id}`}>
                      <td className="col-check">
                        <input
                          type="checkbox"
                          checked={selected.has(post.id)}
                          onChange={() => toggleOne(post.id)}
                          aria-label={`Chọn ${post.title}`}
                          data-testid={`select-${post.id}`}
                        />
                      </td>
                      <td className="col-content">
                        <Link to={`/posts/${post.id}`} className="schedule-list-title-link">
                          {post.title || '(Không tiêu đề)'}
                        </Link>
                        {post.content ? (
                          <div className="schedule-list-excerpt">
                            {String(post.content).slice(0, 80)}
                          </div>
                        ) : null}
                      </td>
                      <td className="col-media">
                        {post.thumbnailUrl ? (
                          <img
                            src={post.thumbnailUrl}
                            alt=""
                            className="schedule-list-thumb"
                          />
                        ) : (
                          <span className="schedule-list-thumb is-empty" aria-hidden />
                        )}
                        <span className="schedule-list-media-count">
                          {post.mediaCount ?? 0}
                        </span>
                      </td>
                      <td>{post.categoryName || '—'}</td>
                      <td>{post.channelName || '—'}</td>
                      <td className="col-time">{formatDateTime(eventTimeOf(post))}</td>
                      <td><PostStatusBadge status={post.status} /></td>
                      <td>{post.authorName || '—'}</td>
                    </tr>
                  ))
                )}
              </tbody>
            </table>
          </div>

          <div className="schedule-list-footer">
            <span data-testid="schedule-list-showing">
              Showing {fromIdx} / {toIdx} of {total} posts
            </span>
            <div className="schedule-list-pager">
              <button
                type="button"
                className="btn btn-ghost btn-sm"
                disabled={page <= 1}
                onClick={() => setPage((p) => Math.max(1, p - 1))}
                aria-label="Trang trước"
              >
                ‹
              </button>
              <span data-testid="schedule-list-page">
                Page {Math.min(page, totalPages)} / {totalPages}
              </span>
              <button
                type="button"
                className="btn btn-ghost btn-sm"
                disabled={page >= totalPages}
                onClick={() => setPage((p) => p + 1)}
                aria-label="Trang sau"
              >
                ›
              </button>
            </div>
          </div>
        </>
      )}
    </div>
  )
}
