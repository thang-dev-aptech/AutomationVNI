import React from 'react'
import { Link } from 'react-router-dom'
import { SourceBadge } from '../../inbox/components/SourceBadge'
import { ActionMenu } from '../../../shared/components/ActionMenu'
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
          <ActionMenu
            triggerLabel="Thao tác nhanh thẻ"
            triggerTestId={`btn-card-menu-${opp.id}`}
            triggerClassName="crm-opp-card-menu-trigger"
            menuTestId={`card-menu-${opp.id}`}
            menuClassName="crm-opp-card-dropdown"
            items={[
              {
                type: 'header',
                key: 'header-stage',
                label: 'Chuyển giai đoạn:',
              },
              ...stages
                .filter((st) => st.id !== opp.stageId)
                .map((st) => ({
                  key: `stage-${st.id}`,
                  label: (
                    <>
                      <span
                        className="crm-opp-stage-dot"
                        style={{ backgroundColor: st.color || '#6366f1' }}
                      />
                      {st.name}
                    </>
                  ),
                  testId: `action-card-move-to-${st.id}`,
                  onSelect: () => onMoveStage?.(opp, st.id),
                })),
              {
                type: 'divider',
                key: 'divider-1',
              },
              {
                key: 'edit',
                label: '✏️ Chỉnh sửa',
                testId: `action-card-edit-${opp.id}`,
                onSelect: () => onEdit?.(opp),
              },
              {
                key: 'delete',
                label: '🗑️ Xoá',
                danger: true,
                testId: `action-card-delete-${opp.id}`,
                onSelect: () => {
                  const confirmed =
                    typeof window.confirm === 'function'
                      ? window.confirm(`Xác nhận xoá cơ hội "${opp.title}"?`)
                      : true
                  if (confirmed) {
                    onDelete?.(opp.id)
                  }
                },
              },
            ]}
          />
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
