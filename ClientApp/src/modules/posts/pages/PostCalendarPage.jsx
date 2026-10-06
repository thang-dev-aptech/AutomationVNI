import { useMemo, useState } from 'react'
import PageHeader from '@/shared/components/PageHeader'
import LoadingState from '@/shared/components/LoadingState'
import ErrorState from '@/shared/components/ErrorState'
import { getErrorMessage } from '@/shared/utils/apiHelpers'
import { useSocialChannelAll } from '@/modules/social-channels/hooks/useSocialChannels'
import { useChannelGroupAll } from '@/modules/social-channels/hooks/useChannelGroups'
import CalendarFilterSidebar from '../components/calendar/CalendarFilterSidebar'
import {
  useCalendarFacets,
  useCalendarPosts,
  useCalendarQuery,
} from '../hooks/useCalendarQuery'
import {
  CALENDAR_VIEW_OPTIONS,
  CALENDAR_VIEWS,
} from '../constants/calendarStatus'
import '../components/calendar/CalendarShell.css'

function ViewPlaceholder({ title, hint }) {
  return (
    <div className="calendar-view-placeholder" data-testid="calendar-view-placeholder">
      <strong>{title}</strong>
      <span>{hint}</span>
    </div>
  )
}

/**
 * Khung trang Lịch SO9: sidebar bộ lọc + chuyển chế độ + state trên URL.
 * Nội dung từng chế độ (Tuần/Tháng, danh sách, theo kênh) do t6–t8 lắp vào.
 */
export default function PostCalendarPage() {
  const [filterOpen, setFilterOpen] = useState(false)
  const [channelSearch, setChannelSearch] = useState('')

  const query = useCalendarQuery()
  const {
    view,
    setView,
    channelMode,
    setChannelMode,
    statusKeys,
    toggleStatusKey,
    channelIds,
    toggleChannelId,
    groupIds,
    toggleGroupId,
    authorIds,
    toggleAuthorId,
    categoryIds,
    toggleCategoryId,
    postTypes,
    togglePostType,
    filterRequest,
  } = query

  const { data: channels = [] } = useSocialChannelAll()
  const { data: groups = [] } = useChannelGroupAll()

  const facetsQuery = useCalendarFacets(filterRequest)
  const postsQuery = useCalendarPosts(filterRequest, {
    enabled: view === CALENDAR_VIEWS.calendar,
  })

  const authors = facetsQuery.data?.authors ?? []
  const categories = facetsQuery.data?.categories ?? []

  const viewLabel = useMemo(
    () => CALENDAR_VIEW_OPTIONS.find((o) => o.key === view)?.label ?? 'Lịch',
    [view],
  )

  const shellClass = [
    'calendar-shell',
    filterOpen ? 'is-filter-open' : '',
  ].filter(Boolean).join(' ')

  return (
    <div>
      <PageHeader
        title="Lịch đăng bài"
        description="Bộ lọc bên trái, chuyển chế độ Lịch / Danh sách / Theo kênh. Trạng thái lưu trên URL."
      />

      <div className={shellClass}>
        {filterOpen ? (
          <button
            type="button"
            className="calendar-shell-backdrop"
            aria-label="Đóng bộ lọc"
            onClick={() => setFilterOpen(false)}
          />
        ) : null}

        <div className="calendar-shell-sidebar">
          <CalendarFilterSidebar
            channelMode={channelMode}
            onChannelModeChange={setChannelMode}
            channelSearch={channelSearch}
            onChannelSearchChange={setChannelSearch}
            channels={channels}
            selectedChannelIds={channelIds}
            onToggleChannel={toggleChannelId}
            groups={groups}
            selectedGroupIds={groupIds}
            onToggleGroup={toggleGroupId}
            statusKeys={statusKeys}
            onToggleStatus={toggleStatusKey}
            authors={authors}
            selectedAuthorIds={authorIds}
            onToggleAuthor={toggleAuthorId}
            categories={categories}
            selectedCategoryIds={categoryIds}
            onToggleCategory={toggleCategoryId}
            selectedPostTypes={postTypes}
            onTogglePostType={togglePostType}
          />
        </div>

        <div className="calendar-shell-main">
          <div className="calendar-shell-toolbar">
            <button
              type="button"
              className="btn btn-secondary calendar-shell-filter-toggle"
              onClick={() => setFilterOpen((v) => !v)}
            >
              Bộ lọc
            </button>

            <div className="calendar-view-tabs" role="tablist" aria-label="Chế độ xem lịch">
              {CALENDAR_VIEW_OPTIONS.map((opt) => (
                <button
                  key={opt.key}
                  type="button"
                  role="tab"
                  aria-selected={view === opt.key}
                  className={`calendar-view-tab${view === opt.key ? ' is-active' : ''}`}
                  onClick={() => {
                    setView(opt.key)
                    setFilterOpen(false)
                  }}
                >
                  {opt.label}
                </button>
              ))}
            </div>
          </div>

          {facetsQuery.isError && (
            <ErrorState
              message={getErrorMessage(facetsQuery.error)}
              onRetry={facetsQuery.refetch}
            />
          )}

          {view === CALENDAR_VIEWS.calendar && postsQuery.isLoading && <LoadingState />}
          {view === CALENDAR_VIEWS.calendar && postsQuery.isError && (
            <ErrorState
              message={getErrorMessage(postsQuery.error)}
              onRetry={postsQuery.refetch}
            />
          )}

          {view === CALENDAR_VIEWS.calendar && !postsQuery.isLoading && !postsQuery.isError && (
            <ViewPlaceholder
              title="Chế độ Lịch"
              hint={`Placeholder t6 — Tuần/Tháng. Đang có ${postsQuery.data?.length ?? 0} bài trong khoảng lọc (${viewLabel}).`}
            />
          )}

          {view === CALENDAR_VIEWS.list && (
            <ViewPlaceholder
              title="Chế độ Danh sách"
              hint="Placeholder t7 — bảng phân trang, 7D/14D/30D, hành động hàng loạt."
            />
          )}

          {view === CALENDAR_VIEWS.byChannel && (
            <ViewPlaceholder
              title="Chế độ Theo kênh"
              hint="Placeholder t8 — lưới kênh × ngày, chấm màu loại bài."
            />
          )}
        </div>
      </div>
    </div>
  )
}
