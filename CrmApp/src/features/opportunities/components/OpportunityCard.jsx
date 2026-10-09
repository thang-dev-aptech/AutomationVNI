import React, { useState, useRef, useEffect } from 'react'
import { Link } from 'react-router-dom'
import { SourceBadge } from '../../inbox/components/SourceBadge'
import { formatCurrencyVnd } from './OpportunityStatsBar'

/**
 * Format relative activity time in Vietnam timezone:
 * "Hôm nay 10:34", "Hôm qua", "Chưa có hoạt động", hoặc ngày tháng nếu lâu hơn.
 */
export function formatRelativeVietnamActivity(dateStr) {
  if (!dateStr) return 'Chưa có hoạt động'
  try {
    const d = new Date(dateStr)
    if (isNaN(d.getTime())) return 'Chưa có hoạt động'

    const optionsDate = { timeZone: 'Asia/Ho_Chi_Minh', year: 'numeric', month: '2-digit', day: '2-digit' }
    const optionsTime = { timeZone: 'Asia/Ho_Chi_Minh', hour: '2-digit', minute: '2-digit', hour12: false }

    const now = new Date()
    const nowParts = new Intl.DateTimeFormat('en-CA', optionsDate).format(now) // YYYY-MM-DD
    const targetParts = new Intl.DateTimeFormat('en-CA', optionsDate).format(d) // YYYY-MM-DD

    const nowDate = new Date(nowParts + 'T00:00:00Z')
    const targetDate = new Date(targetParts + 'T00:00:00Z')
    const diffDays = Math.round((nowDate - targetDate) / (1000 * 60 * 60 * 24))

    const timeStr = new Intl.DateTimeFormat('vi-VN', optionsTime).format(d)

    if (diffDays === 0) {
      return `Hôm nay ${timeStr}`
    } else if (diffDays === 1) {
      return 'Hôm qua'
    } else if (diffDays > 1 && diffDays <= 7) {
      return `${diffDays} ngày trước`
    } else {
      return new Intl.DateTimeFormat('vi-VN', optionsDate).format(d)
    }
  } catch {
    return 'Chưa có hoạt động'
  }
}

export const OpportunityCard = ({
  opp,
  stages = [],
  isReadOnly = false,
  onCardClick,
  onMoveStage,
  onEdit,
  onDelete,
  onDragStart,
}) => {
  const [menuOpen, setMenuOpen] = useState(false)
  const menuRef = useRef(null)

  useEffect(() => {
    const handleClickOutside = (e) => {
      if (menuRef.current && !menuRef.current.contains(e.target)) {
        setMenuOpen(false)
      }
    }
    if (menuOpen) {
      document.addEventListener('mousedown', handleClickOutside)
    }
    return () => document.removeEventListener('mousedown', handleClickOutside)
  }, [menuOpen])

  const handleDragStart = (e) => {
    if (isReadOnly) {
      e.preventDefault()
      return
    }
    onDragStart?.(e, opp)
  }

  // Determine conversation link for source snippet
  const snippet = opp.sourceSnippet || opp.lastSnippet || null
  const inboxKind = opp.source === 2 ? 'message' : opp.source === 3 ? 'comment' : null
  const convId = opp.source === 3
    ? (opp.socialCommentId || opp.pageConversationId || null)
    : (opp.pageConversationId || opp.socialCommentId || null)
  const hasInboxLink = Boolean(snippet && inboxKind && convId)

  return (
    <div
      className={`crm-opp-card ${isReadOnly ? 'crm-opp-card--readonly' : ''}`}
      draggable={!isReadOnly}
      onDragStart={handleDragStart}
      data-testid={`opp-card-${opp.id}`}
    >
      {/* Header of card: title & menu */}
      <div className="crm-opp-card-header">
        <strong
          className="crm-opp-card-title"
          onClick={() => onCardClick?.(opp.id)}
          title={opp.title}
          data-testid={`card-title-${opp.id}`}
        >
          {opp.title}
        </strong>

        {!isReadOnly && (
          <div className="crm-opp-card-menu-wrap" ref={menuRef}>
            <button
              type="button"
              className="crm-opp-card-menu-trigger"
              onClick={(e) => {
                e.stopPropagation()
                setMenuOpen(!menuOpen)
              }}
              aria-label="Thao tác nhanh thẻ"
              data-testid={`btn-card-menu-${opp.id}`}
            >
              ⋮
            </button>

            {menuOpen && (
              <div
                className="crm-opp-dropdown-menu crm-opp-card-dropdown"
                data-testid={`card-menu-${opp.id}`}
                onClick={(e) => e.stopPropagation()}
              >
                <div className="crm-opp-card-menu-section-lbl">Chuyển giai đoạn:</div>
                {stages
                  .filter((st) => st.id !== opp.stageId)
                  .map((st) => (
                    <button
                      key={st.id}
                      type="button"
                      className="crm-opp-dropdown-item"
                      onClick={() => {
                        setMenuOpen(false)
                        onMoveStage?.(opp, st.id)
                      }}
                      data-testid={`action-card-move-to-${st.id}`}
                    >
                      <span
                        className="crm-opp-stage-dot"
                        style={{ backgroundColor: st.color || '#6366f1' }}
                      />
                      {st.name}
                    </button>
                  ))}

                <hr className="crm-opp-menu-divider" />

                <button
                  type="button"
                  className="crm-opp-dropdown-item"
                  onClick={() => {
                    setMenuOpen(false)
                    onEdit?.(opp)
                  }}
                  data-testid={`action-card-edit-${opp.id}`}
                >
                  ✏️ Chỉnh sửa
                </button>

                <button
                  type="button"
                  className="crm-opp-dropdown-item crm-opp-dropdown-item--danger"
                  onClick={() => {
                    setMenuOpen(false)
                    const confirmed =
                      typeof window.confirm === 'function'
                        ? window.confirm(`Xác nhận xoá cơ hội "${opp.title}"?`)
                        : true
                    if (confirmed) {
                      onDelete?.(opp.id)
                    }
                  }}
                  data-testid={`action-card-delete-${opp.id}`}
                >
                  🗑️ Xoá
                </button>
              </div>
            )}
          </div>
        )}
      </div>

      {/* Customer & phone info */}
      <div className="crm-opp-card-customer">
        <span className="crm-opp-card-cust-name" data-testid={`card-customer-${opp.id}`}>
          👤 {opp.customerName || 'Khách hàng'}
        </span>
        {opp.customerPhoneE164 && (
          <span className="crm-opp-card-cust-phone" data-testid={`card-phone-${opp.id}`}>
            📞 {opp.customerPhoneE164}
          </span>
        )}
      </div>

      {/* Source platform tag */}
      <div className="crm-opp-card-source">
        {opp.channelPlatform ? (
          <span className="crm-opp-source-tag">
            <SourceBadge
              item={{
                platform: opp.channelPlatform,
                kind: opp.source === 2 ? 1 : 2,
                id: opp.id,
              }}
            />
            <span>{opp.channelName || 'Kênh'}</span>
          </span>
        ) : (
          <span className="crm-opp-source-tag">✍️ Thủ công</span>
        )}
      </div>

      {/* Expected value */}
      <div className="crm-opp-card-val" data-testid={`card-value-${opp.id}`}>
        <strong>{formatCurrencyVnd(opp.expectedValue)}</strong>
      </div>

      {/* Recent message snippet if available */}
      {snippet && (
        <div className="crm-opp-card-snippet">
          {hasInboxLink ? (
            <Link
              to={`/inbox?kind=${inboxKind}&id=${convId}`}
              className="crm-opp-snippet-bubble"
              onClick={(e) => e.stopPropagation()}
              data-testid={`card-snippet-${opp.id}`}
              title="Mở hội thoại trong Hộp thư"
            >
              💬 <span>{snippet}</span>
            </Link>
          ) : (
            <div
              className="crm-opp-snippet-bubble"
              data-testid={`card-snippet-${opp.id}`}
            >
              💬 <span>{snippet}</span>
            </div>
          )}
        </div>
      )}

      {/* Footer: Assignee avatar & Relative last activity */}
      <div className="crm-opp-card-footer">
        <div className="crm-opp-card-assignee" data-testid={`card-assignee-${opp.id}`}>
          <span className="crm-opp-assignee-avatar" title={opp.assignedTo || 'Chưa gán'}>
            {(opp.assignedTo || 'U').charAt(0).toUpperCase()}
          </span>
          <span className="crm-opp-assignee-name">
            {opp.assignedTo || <span className="crm-opp-text-muted">Chưa gán</span>}
          </span>
        </div>

        <div className="crm-opp-card-act" data-testid={`card-last-activity-${opp.id}`}>
          <small>{formatRelativeVietnamActivity(opp.lastActivityAtUtc)}</small>
        </div>
      </div>
    </div>
  )
}

export default OpportunityCard
