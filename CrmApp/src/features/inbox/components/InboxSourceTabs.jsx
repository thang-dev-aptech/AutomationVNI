import React, { useRef } from 'react'
import PlatformLogo from './PlatformLogo'

/**
 * Thanh chọn nguồn hội thoại (Tất cả / Messenger / Facebook / Instagram / ...)
 * Thay thế cho 3 chip loại tin (Tất cả / Tin nhắn / Bình luận).
 */
export const InboxSourceTabs = ({
  sources = [],
  selectedSource = null,
  onSelectSource,
}) => {
  const tabsRef = useRef([])

  const totalUnread = sources.reduce((sum, s) => sum + (Number(s.unread) || 0), 0)

  const handleKeyDown = (e, currentIndex) => {
    const totalTabs = 1 + sources.length
    let nextIndex = -1
    if (e.key === 'ArrowRight') {
      nextIndex = (currentIndex + 1) % totalTabs
    } else if (e.key === 'ArrowLeft') {
      nextIndex = (currentIndex - 1 + totalTabs) % totalTabs
    } else if (e.key === 'Home') {
      nextIndex = 0
    } else if (e.key === 'End') {
      nextIndex = totalTabs - 1
    }

    if (nextIndex >= 0) {
      e.preventDefault()
      tabsRef.current[nextIndex]?.focus()
    }
  }

  const isAllActive = !selectedSource

  return (
    <div
      className="crm-source-tabs crm-channel-filters"
      role="tablist"
      aria-label="Nguồn hội thoại"
      data-testid="inbox-source-tabs"
      style={{
        display: 'flex',
        gap: '6px',
        overflowX: 'auto',
        whiteSpace: 'nowrap',
        alignItems: 'center',
        paddingBottom: '2px',
        scrollbarWidth: 'thin',
      }}
    >
      {/* Tab: Tất cả */}
      <button
        ref={(el) => (tabsRef.current[0] = el)}
        type="button"
        role="tab"
        className={`crm-filter-chip crm-source-chip ${isAllActive ? 'active' : ''}`}
        aria-selected={isAllActive}
        aria-pressed={isAllActive}
        tabIndex={0}
        onClick={() => onSelectSource && onSelectSource(null)}
        onKeyDown={(e) => handleKeyDown(e, 0)}
        data-testid="source-tab-all"
        data-source="all"
        style={{
          display: 'inline-flex',
          alignItems: 'center',
          gap: '6px',
          flexShrink: 0,
        }}
      >
        <span>Tất cả</span>
        {totalUnread > 0 && (
          <span
            className="crm-source-tab-badge"
            data-testid="source-unread-all"
            style={{
              display: 'inline-flex',
              alignItems: 'center',
              justifyContent: 'center',
              minWidth: '16px',
              height: '16px',
              padding: '0 4px',
              fontSize: '10px',
              fontWeight: 700,
              borderRadius: '9999px',
              background: isAllActive ? 'var(--crm-primary, #4F46E5)' : 'var(--crm-danger, #EF4444)',
              color: '#fff',
              lineHeight: 1,
            }}
          >
            {totalUnread}
          </span>
        )}
      </button>

      {/* Tabs theo từng nguồn từ API */}
      {sources.map((s, idx) => {
        const isActive = selectedSource === s.key
        const tabIndex = idx + 1
        const unreadCount = Number(s.unread) || 0

        return (
          <button
            key={s.key}
            ref={(el) => (tabsRef.current[tabIndex] = el)}
            type="button"
            role="tab"
            className={`crm-filter-chip crm-source-chip ${isActive ? 'active' : ''}`}
            aria-selected={isActive}
            aria-pressed={isActive}
            tabIndex={0}
            onClick={() => onSelectSource && onSelectSource(s.key)}
            onKeyDown={(e) => handleKeyDown(e, tabIndex)}
            data-testid={`source-tab-${s.key}`}
            data-source={s.key}
            style={{
              display: 'inline-flex',
              alignItems: 'center',
              gap: '6px',
              flexShrink: 0,
            }}
          >
            <span
              className="crm-source-tab-logo"
              style={{
                width: '16px',
                height: '16px',
                display: 'inline-flex',
                alignItems: 'center',
                justifyContent: 'center',
                flexShrink: 0,
              }}
            >
              <PlatformLogo name={s.key} />
            </span>
            <span>{s.label}</span>
            {unreadCount > 0 && (
              <span
                className="crm-source-tab-badge"
                data-testid={`source-unread-${s.key}`}
                style={{
                  display: 'inline-flex',
                  alignItems: 'center',
                  justifyContent: 'center',
                  minWidth: '16px',
                  height: '16px',
                  padding: '0 4px',
                  fontSize: '10px',
                  fontWeight: 700,
                  borderRadius: '9999px',
                  background: isActive ? 'var(--crm-primary, #4F46E5)' : 'var(--crm-danger, #EF4444)',
                  color: '#fff',
                  lineHeight: 1,
                }}
              >
                {unreadCount}
              </span>
            )}
          </button>
        )
      })}
    </div>
  )
}

export default InboxSourceTabs
