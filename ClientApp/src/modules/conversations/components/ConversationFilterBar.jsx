import { useEffect, useRef, useState } from 'react'
import ChannelMultiSelect from '@/shared/components/ChannelMultiSelect'
import { useSocialChannelAll } from '@/modules/social-channels/hooks/useSocialChannels'
import { useActiveUsers } from '../hooks/useInbox'
import './ConversationFilterBar.css'

const STATUS_OPTIONS = [
  { value: 1, label: 'Mới' },
  { value: 2, label: 'Đang xử lý' },
  { value: 3, label: 'Đã trả lời' },
  { value: 4, label: 'Bỏ qua / Đóng' },
]

const KIND_OPTIONS = [
  { value: 'message', label: 'Tin nhắn' },
  { value: 'comment', label: 'Bình luận' },
]

export default function ConversationFilterBar({
  filters,
  onUpdateFilter,
  onClearFilters,
}) {
  const [searchValue, setSearchValue] = useState(filters.search || '')
  const [activePopover, setActivePopover] = useState(null)
  const barRef = useRef(null)

  const { data: channels = [] } = useSocialChannelAll()
  const { data: users = [] } = useActiveUsers()

  // Sync external search change (e.g. from URL)
  useEffect(() => {
    setSearchValue(filters.search || '')
  }, [filters.search])

  // Debounce search update
  useEffect(() => {
    const handler = setTimeout(() => {
      if (searchValue !== (filters.search || '')) {
        onUpdateFilter('search', searchValue)
      }
    }, 300)
    return () => clearTimeout(handler)
  }, [searchValue, filters.search, onUpdateFilter])

  // Close popover on outside click or escape
  useEffect(() => {
    if (!activePopover) return undefined
    const handleDocClick = (e) => {
      if (barRef.current && !barRef.current.contains(e.target)) {
        setActivePopover(null)
      }
    }
    const handleKey = (e) => {
      if (e.key === 'Escape') setActivePopover(null)
    }
    document.addEventListener('mousedown', handleDocClick)
    document.addEventListener('keydown', handleKey)
    return () => {
      document.removeEventListener('mousedown', handleDocClick)
      document.removeEventListener('keydown', handleKey)
    }
  }, [activePopover])

  const togglePopover = (name) => {
    setActivePopover((prev) => (prev === name ? null : name))
  }

  // Active states for 8 filters
  const isStatusActive = (filters.statuses?.length || 0) > 0
  const isKindActive = (filters.kinds?.length || 0) > 0
  const isSourceActive =
    (filters.socialChannelIds?.length || 0) > 0 || (filters.channelGroupIds?.length || 0) > 0
  const isUnreadActive = Boolean(filters.unreadOnly)
  const isDateActive = Boolean(filters.fromUtc || filters.toUtc)
  const isAssigneeActive = (filters.assignedUserIds?.length || 0) > 0
  const isUnansweredActive = Boolean(filters.customerUnansweredOnly)
  const isOpenWindowActive = Boolean(filters.openWindowOnly)

  const hasAnyFilter =
    isStatusActive ||
    isKindActive ||
    isSourceActive ||
    isUnreadActive ||
    isDateActive ||
    isAssigneeActive ||
    isUnansweredActive ||
    isOpenWindowActive ||
    Boolean(filters.search)

  const handleToggleStatus = (val) => {
    const current = filters.statuses || []
    const next = current.includes(val) ? current.filter((x) => x !== val) : [...current, val]
    onUpdateFilter('statuses', next)
  }

  const handleToggleKind = (val) => {
    const current = filters.kinds || []
    const next = current.includes(val) ? current.filter((x) => x !== val) : [...current, val]
    onUpdateFilter('kinds', next)
  }

  const handleToggleAssignee = (userId) => {
    const current = filters.assignedUserIds || []
    const next = current.includes(userId)
      ? current.filter((x) => x !== userId)
      : [...current, userId]
    onUpdateFilter('assignedUserIds', next)
  }

  const handleSetDatePreset = (preset) => {
    const now = new Date()
    if (preset === 'today') {
      const from = new Date(now.getFullYear(), now.getMonth(), now.getDate()).toISOString()
      onUpdateFilter('fromUtc', from)
      onUpdateFilter('toUtc', null)
    } else if (preset === '7days') {
      const from = new Date(now.getTime() - 7 * 24 * 60 * 60 * 1000).toISOString()
      onUpdateFilter('fromUtc', from)
      onUpdateFilter('toUtc', null)
    } else if (preset === '30days') {
      const from = new Date(now.getTime() - 30 * 24 * 60 * 60 * 1000).toISOString()
      onUpdateFilter('fromUtc', from)
      onUpdateFilter('toUtc', null)
    } else if (preset === 'clear') {
      onUpdateFilter('fromUtc', null)
      onUpdateFilter('toUtc', null)
    }
  }

  return (
    <div className="conversation-filter-bar" ref={barRef} data-testid="conversation-filter-bar">
      {/* Ô tìm kiếm có debounce */}
      <div className="conversation-search-wrap">
        <svg
          className="conversation-search-icon"
          xmlns="http://www.w3.org/2000/svg"
          width="16"
          height="16"
          viewBox="0 0 24 24"
          fill="none"
          stroke="currentColor"
          strokeWidth="2"
          strokeLinecap="round"
          strokeLinejoin="round"
          aria-hidden
        >
          <circle cx="11" cy="11" r="8" />
          <line x1="21" y1="21" x2="16.65" y2="16.65" />
        </svg>
        <input
          type="search"
          className="conversation-search-input"
          placeholder="Tìm theo tên, nội dung, SĐT..."
          value={searchValue}
          onChange={(e) => setSearchValue(e.target.value)}
          aria-label="Tìm kiếm hội thoại"
        />
        {searchValue && (
          <button
            type="button"
            className="conversation-search-clear"
            onClick={() => {
              setSearchValue('')
              onUpdateFilter('search', '')
            }}
            aria-label="Xóa từ khóa tìm kiếm"
          >
            ×
          </button>
        )}
      </div>

      {/* Thanh 8 icon lọc + nút xoá lọc */}
      <div className="conversation-icons-row" role="toolbar" aria-label="Thanh lọc hội thoại">
        {/* 1. Trạng thái */}
        <div className="filter-icon-container">
          <button
            type="button"
            className={`filter-icon-btn${isStatusActive ? ' is-active' : ''}`}
            title="Trạng thái"
            aria-label="Trạng thái"
            data-testid="filter-status"
            aria-pressed={isStatusActive}
            onClick={() => togglePopover('status')}
          >
            <svg xmlns="http://www.w3.org/2000/svg" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
              <path d="M12 2H2v10l9.29 9.29c.94.94 2.48.94 3.42 0l6.58-6.58c.94-.94.94-2.48 0-3.42L12 2Z" />
              <path d="M7 7h.01" />
            </svg>
          </button>
          {activePopover === 'status' && (
            <div className="filter-popover" role="dialog" aria-label="Lọc trạng thái">
              <div className="filter-popover-title">Trạng thái</div>
              <div className="filter-popover-list">
                {STATUS_OPTIONS.map((opt) => (
                  <label key={opt.value} className="filter-popover-item">
                    <input
                      type="checkbox"
                      checked={(filters.statuses || []).includes(opt.value)}
                      onChange={() => handleToggleStatus(opt.value)}
                    />
                    <span>{opt.label}</span>
                  </label>
                ))}
              </div>
            </div>
          )}
        </div>

        {/* 2. Loại */}
        <div className="filter-icon-container">
          <button
            type="button"
            className={`filter-icon-btn${isKindActive ? ' is-active' : ''}`}
            title="Loại"
            aria-label="Loại"
            data-testid="filter-kind"
            aria-pressed={isKindActive}
            onClick={() => togglePopover('kind')}
          >
            <svg xmlns="http://www.w3.org/2000/svg" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
              <rect x="2" y="2" width="20" height="8" rx="2" ry="2" />
              <rect x="2" y="14" width="20" height="8" rx="2" ry="2" />
              <line x1="6" y1="6" x2="6.01" y2="6" />
              <line x1="6" y1="18" x2="6.01" y2="18" />
            </svg>
          </button>
          {activePopover === 'kind' && (
            <div className="filter-popover" role="dialog" aria-label="Lọc loại">
              <div className="filter-popover-title">Loại hội thoại</div>
              <div className="filter-popover-list">
                {KIND_OPTIONS.map((opt) => (
                  <label key={opt.value} className="filter-popover-item">
                    <input
                      type="checkbox"
                      checked={(filters.kinds || []).includes(opt.value)}
                      onChange={() => handleToggleKind(opt.value)}
                    />
                    <span>{opt.label}</span>
                  </label>
                ))}
              </div>
            </div>
          )}
        </div>

        {/* 3. Nguồn (Kênh & nhóm kênh) */}
        <div className="filter-icon-container">
          <button
            type="button"
            className={`filter-icon-btn${isSourceActive ? ' is-active' : ''}`}
            title="Nguồn"
            aria-label="Nguồn"
            data-testid="filter-source"
            aria-pressed={isSourceActive}
            onClick={() => togglePopover('source')}
          >
            <svg xmlns="http://www.w3.org/2000/svg" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
              <circle cx="12" cy="12" r="10" />
              <line x1="2" y1="12" x2="22" y2="12" />
              <path d="M12 2a15.3 15.3 0 0 1 4 10 15.3 15.3 0 0 1-4 10 15.3 15.3 0 0 1-4-10 15.3 15.3 0 0 1 4-10z" />
            </svg>
          </button>
          {activePopover === 'source' && (
            <div className="filter-popover filter-popover--wide" role="dialog" aria-label="Lọc nguồn">
              <div className="filter-popover-title">Nguồn kênh & Nhóm kênh</div>
              <ChannelMultiSelect
                channels={channels}
                value={filters.socialChannelIds || []}
                onChange={(ids) => onUpdateFilter('socialChannelIds', ids)}
                enableGroups
                label=""
                placeholder="Chọn kênh hoặc nhóm..."
              />
            </div>
          )}
        </div>

        {/* 4. Chưa đọc */}
        <div className="filter-icon-container">
          <button
            type="button"
            className={`filter-icon-btn${isUnreadActive ? ' is-active' : ''}`}
            title="Chưa đọc"
            aria-label="Chưa đọc"
            data-testid="filter-unread"
            aria-pressed={isUnreadActive}
            onClick={() => onUpdateFilter('unreadOnly', !isUnreadActive)}
          >
            <svg xmlns="http://www.w3.org/2000/svg" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
              <rect x="2" y="4" width="20" height="16" rx="2" />
              <path d="m22 7-8.97 5.7a1.94 1.94 0 0 1-2.06 0L2 7" />
            </svg>
          </button>
        </div>

        {/* 5. Khoảng thời gian */}
        <div className="filter-icon-container">
          <button
            type="button"
            className={`filter-icon-btn${isDateActive ? ' is-active' : ''}`}
            title="Khoảng thời gian"
            aria-label="Khoảng thời gian"
            data-testid="filter-date"
            aria-pressed={isDateActive}
            onClick={() => togglePopover('date')}
          >
            <svg xmlns="http://www.w3.org/2000/svg" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
              <rect x="3" y="4" width="18" height="18" rx="2" ry="2" />
              <line x1="16" y1="2" x2="16" y2="6" />
              <line x1="8" y1="2" x2="8" y2="6" />
              <line x1="3" y1="10" x2="21" y2="10" />
            </svg>
          </button>
          {activePopover === 'date' && (
            <div className="filter-popover" role="dialog" aria-label="Lọc thời gian">
              <div className="filter-popover-title">Khoảng thời gian</div>
              <div className="filter-date-presets">
                <button
                  type="button"
                  className="filter-date-btn"
                  onClick={() => handleSetDatePreset('today')}
                >
                  Hôm nay
                </button>
                <button
                  type="button"
                  className="filter-date-btn"
                  onClick={() => handleSetDatePreset('7days')}
                >
                  7 ngày qua
                </button>
                <button
                  type="button"
                  className="filter-date-btn"
                  onClick={() => handleSetDatePreset('30days')}
                >
                  30 ngày qua
                </button>
                <div className="filter-date-inputs">
                  <label>
                    <span>Từ:</span>
                    <input
                      type="date"
                      value={filters.fromUtc ? filters.fromUtc.slice(0, 10) : ''}
                      onChange={(e) =>
                        onUpdateFilter(
                          'fromUtc',
                          e.target.value ? new Date(e.target.value).toISOString() : null,
                        )
                      }
                    />
                  </label>
                  <label>
                    <span>Đến:</span>
                    <input
                      type="date"
                      value={filters.toUtc ? filters.toUtc.slice(0, 10) : ''}
                      onChange={(e) =>
                        onUpdateFilter(
                          'toUtc',
                          e.target.value
                            ? new Date(`${e.target.value}T23:59:59.999Z`).toISOString()
                            : null,
                        )
                      }
                    />
                  </label>
                </div>
                {isDateActive && (
                  <button
                    type="button"
                    className="filter-date-btn filter-date-btn--clear"
                    onClick={() => handleSetDatePreset('clear')}
                  >
                    Bỏ chọn thời gian
                  </button>
                )}
              </div>
            </div>
          )}
        </div>

        {/* 6. Nhân viên */}
        <div className="filter-icon-container">
          <button
            type="button"
            className={`filter-icon-btn${isAssigneeActive ? ' is-active' : ''}`}
            title="Nhân viên"
            aria-label="Nhân viên"
            data-testid="filter-assignee"
            aria-pressed={isAssigneeActive}
            onClick={() => togglePopover('assignee')}
          >
            <svg xmlns="http://www.w3.org/2000/svg" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
              <path d="M16 21v-2a4 4 0 0 0-4-4H6a4 4 0 0 0-4 4v2" />
              <circle cx="9" cy="7" r="4" />
              <polyline points="16 11 18 13 22 9" />
            </svg>
          </button>
          {activePopover === 'assignee' && (
            <div className="filter-popover" role="dialog" aria-label="Lọc nhân viên">
              <div className="filter-popover-title">Nhân viên phụ trách</div>
              <div className="filter-popover-list">
                {users.length === 0 ? (
                  <div className="filter-popover-empty">Không có danh sách nhân viên</div>
                ) : (
                  users.map((u) => (
                    <label key={u.id} className="filter-popover-item">
                      <input
                        type="checkbox"
                        checked={(filters.assignedUserIds || []).includes(u.id)}
                        onChange={() => handleToggleAssignee(u.id)}
                      />
                      <span>{u.displayName || u.userName}</span>
                    </label>
                  ))
                )}
              </div>
            </div>
          )}
        </div>

        {/* 7. Khách chưa trả lời */}
        <div className="filter-icon-container">
          <button
            type="button"
            className={`filter-icon-btn${isUnansweredActive ? ' is-active' : ''}`}
            title="Khách chưa trả lời"
            aria-label="Khách chưa trả lời"
            data-testid="filter-unanswered"
            aria-pressed={isUnansweredActive}
            onClick={() => onUpdateFilter('customerUnansweredOnly', !isUnansweredActive)}
          >
            <svg xmlns="http://www.w3.org/2000/svg" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
              <circle cx="12" cy="12" r="10" />
              <polyline points="12 6 12 12 16 14" />
            </svg>
          </button>
        </div>

        {/* 8. Cửa sổ 24h */}
        <div className="filter-icon-container">
          <button
            type="button"
            className={`filter-icon-btn${isOpenWindowActive ? ' is-active' : ''}`}
            title="Cửa sổ 24h"
            aria-label="Cửa sổ 24h"
            data-testid="filter-open-window"
            aria-pressed={isOpenWindowActive}
            onClick={() => onUpdateFilter('openWindowOnly', !isOpenWindowActive)}
          >
            <svg xmlns="http://www.w3.org/2000/svg" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
              <path d="M13 2 3 14h9l-1 8 10-12h-9l1-8z" />
            </svg>
          </button>
        </div>

        {/* Nút xoá lọc */}
        <button
          type="button"
          className="filter-clear-btn"
          title="Xóa lọc"
          aria-label="Xóa lọc"
          data-testid="filter-clear"
          disabled={!hasAnyFilter}
          onClick={onClearFilters}
        >
          <svg xmlns="http://www.w3.org/2000/svg" width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
            <path d="M3 12a9 9 0 1 0 9-9 9.75 9.75 0 0 0-6.74 2.74L3 8" />
            <path d="M3 3v5h5" />
          </svg>
          <span>Xóa lọc</span>
        </button>
      </div>
    </div>
  )
}
