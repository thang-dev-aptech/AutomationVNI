import React from 'react'
import Badge from '../../../shared/components/Badge'
import Button from '../../../shared/components/Button'
import { formatVietnamDateTime } from '../../../shared/utils/dateUtils'

export const ScheduledMessageList = ({
  messages = [],
  onEdit,
  onCancel,
  isReadOnly = false,
}) => {
  // Only display pending (1) and failed (4) scheduled messages in this section
  const visibleMessages = messages.filter(
    (m) => m && (m.status === 1 || m.status === 4 || m.status === 'Pending' || m.status === 'Failed'),
  )

  if (visibleMessages.length === 0) return null

  return (
    <div
      className="crm-scheduled-message-list"
      data-testid="scheduled-messages-list"
      style={{
        margin: '12px 16px',
        padding: '12px 16px',
        background: 'var(--crm-surface-subtle, #F8FAFC)',
        border: '1px solid var(--crm-border, #E2E8F0)',
        borderRadius: 'var(--crm-radius-md, 8px)',
      }}
    >
      <div
        style={{
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'space-between',
          marginBottom: '10px',
        }}
      >
        <div style={{ display: 'flex', alignItems: 'center', gap: '6px', fontWeight: 700, fontSize: '13px', color: 'var(--crm-text)' }}>
          <span>⏰</span>
          <span>Tin hẹn giờ ({visibleMessages.length})</span>
        </div>
      </div>

      <div style={{ display: 'flex', flexDirection: 'column', gap: '8px' }}>
        {visibleMessages.map((msg) => {
          const isPending = msg.status === 1 || msg.status === 'Pending'
          const isFailed = msg.status === 4 || msg.status === 'Failed'

          return (
            <div
              key={msg.id}
              className={`crm-scheduled-card ${isPending ? 'pending' : 'failed'}`}
              data-testid={`scheduled-item-${msg.id}`}
              style={{
                background: 'var(--crm-surface, #FFFFFF)',
                border: `1px solid ${isFailed ? '#FCA5A5' : 'var(--crm-border, #E2E8F0)'}`,
                borderRadius: 'var(--crm-radius-sm, 6px)',
                padding: '10px 12px',
                display: 'flex',
                flexDirection: 'column',
                gap: '6px',
              }}
            >
              <div style={{ display: 'flex', alignItems: 'flex-start', justifyContent: 'space-between', gap: '8px' }}>
                <div
                  className="crm-scheduled-text"
                  data-testid={`scheduled-text-${msg.id}`}
                  style={{
                    fontSize: '13px',
                    color: 'var(--crm-text)',
                    whiteSpace: 'pre-wrap',
                    wordBreak: 'break-word',
                    flex: 1,
                  }}
                >
                  {msg.text}
                </div>
                <div style={{ display: 'flex', alignItems: 'center', gap: '6px' }}>
                  {isPending && (
                    <Badge variant="warning" size="sm" data-testid={`scheduled-status-${msg.id}`}>
                      Chờ gửi
                    </Badge>
                  )}
                  {isFailed && (
                    <Badge variant="danger" size="sm" data-testid={`scheduled-status-${msg.id}`}>
                      Thất bại
                    </Badge>
                  )}
                </div>
              </div>

              <div
                style={{
                  display: 'flex',
                  alignItems: 'center',
                  justifyContent: 'space-between',
                  fontSize: '11px',
                  color: 'var(--crm-text-muted)',
                  marginTop: '2px',
                }}
              >
                <span data-testid={`scheduled-time-${msg.id}`}>
                  🕒 Gửi lúc: {formatVietnamDateTime(msg.scheduledAtUtc)}
                </span>

                {/* Sửa / Huỷ buttons only for pending and non-Viewer */}
                {isPending && !isReadOnly && (
                  <div style={{ display: 'flex', gap: '6px' }}>
                    <Button
                      type="button"
                      variant="secondary"
                      size="sm"
                      onClick={() => onEdit && onEdit(msg)}
                      data-testid={`btn-edit-scheduled-${msg.id}`}
                      style={{ padding: '2px 8px', fontSize: '11px', minHeight: '24px' }}
                    >
                      Sửa
                    </Button>
                    <Button
                      type="button"
                      variant="danger"
                      size="sm"
                      onClick={() => onCancel && onCancel(msg.id)}
                      data-testid={`btn-cancel-scheduled-${msg.id}`}
                      style={{ padding: '2px 8px', fontSize: '11px', minHeight: '24px' }}
                    >
                      Huỷ
                    </Button>
                  </div>
                )}
              </div>

              {isFailed && msg.error && (
                <div
                  className="crm-scheduled-error"
                  data-testid={`scheduled-error-${msg.id}`}
                  style={{
                    fontSize: '11px',
                    color: '#DC2626',
                    backgroundColor: '#FEF2F2',
                    padding: '4px 8px',
                    borderRadius: '4px',
                  }}
                >
                  ⚠️ Lý do: {msg.error}
                </div>
              )}
            </div>
          )
        })}
      </div>
    </div>
  )
}

export default ScheduledMessageList
