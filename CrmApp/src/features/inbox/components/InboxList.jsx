import React from 'react'
import Badge from '../../../shared/components/Badge'
import { formatVietnamDateTime } from '../../../shared/utils/dateUtils'

const STATUS_CONFIG = {
  1: { label: 'Mới', variant: 'danger' },
  2: { label: 'Đang xử lý', variant: 'warning' },
  3: { label: 'Đã trả lời', variant: 'success' },
  4: { label: 'Bỏ qua', variant: 'default' },
}

export const InboxList = ({
  items = [],
  selectedId,
  onSelectItem,
  loading = false,
  error = null,
}) => {
  return (
    <div className="crm-conv-list" data-testid="conversation-list">
      {loading && items.length === 0 ? (
        <div style={{ textAlign: 'center', padding: '32px', color: 'var(--crm-text-muted)' }}>
          Đang tải hộp thư hợp nhất...
        </div>
      ) : error && items.length === 0 ? (
        <div style={{ padding: '16px', color: 'var(--crm-danger-text)', textAlign: 'center' }}>
          {error}
        </div>
      ) : items.length === 0 ? (
        <div style={{ textAlign: 'center', padding: '40px', color: 'var(--crm-text-muted)' }}>
          📭 Không có tin nhắn hoặc bình luận nào phù hợp.
        </div>
      ) : (
        items.map((item) => {
          const isActive = item.id === selectedId
          const statusInfo = STATUS_CONFIG[item.status] || { label: 'Khác', variant: 'default' }
          const name = item.displayName || 'Khách hàng'
          const initial = name.charAt(0).toUpperCase() || 'K'
          const isMessage = item.kind === 1

          return (
            <div
              key={`${item.kind}-${item.id}`}
              className={`crm-conv-item ${isActive ? 'active' : ''}`}
              onClick={() => onSelectItem && onSelectItem(item)}
              data-testid={`conv-item-${item.id}`}
              style={{ position: 'relative' }}
            >
              {/* Avatar with kind icon */}
              <div
                className="crm-conv-avatar"
                style={{
                  backgroundColor: isMessage ? '#EEF2FF' : '#F5F3FF',
                  color: isMessage ? '#4F46E5' : '#7C3AED',
                }}
              >
                {initial}
              </div>

              {/* Body */}
              <div className="crm-conv-body">
                <div className="crm-conv-header">
                  <span className="crm-conv-name" data-testid={`conv-name-${item.id}`}>
                    {name}
                  </span>
                  <span className="crm-conv-time">
                    {formatVietnamDateTime(item.lastCustomerActivityAt)}
                  </span>
                </div>

                <div className="crm-conv-preview" data-testid={`conv-snippet-${item.id}`}>
                  {item.snippet || 'Chưa có nội dung'}
                </div>

                {/* Badges line */}
                <div style={{ display: 'flex', flexWrap: 'wrap', gap: '4px', marginTop: '6px', alignItems: 'center' }}>
                  {/* Kind */}
                  <Badge variant={isMessage ? 'primary' : 'default'} size="sm">
                    {isMessage ? '💬 Tin nhắn' : '📝 Bình luận'}
                  </Badge>

                  {/* Channel */}
                  {item.channelName && (
                    <span
                      style={{
                        fontSize: '11px',
                        color: 'var(--crm-text-muted)',
                        background: 'var(--crm-surface-subtle)',
                        padding: '2px 6px',
                        borderRadius: '4px',
                        maxWidth: '120px',
                        overflow: 'hidden',
                        textOverflow: 'ellipsis',
                        whiteSpace: 'nowrap',
                      }}
                      title={item.channelName}
                    >
                      {item.channelName}
                    </span>
                  )}

                  {/* Status */}
                  <Badge variant={statusInfo.variant} size="sm">
                    {statusInfo.label}
                  </Badge>

                  {/* Assignee */}
                  {item.assignedTo && (
                    <span
                      style={{
                        fontSize: '11px',
                        color: '#2563EB',
                        background: '#EFF6FF',
                        padding: '2px 6px',
                        borderRadius: '4px',
                        fontWeight: 600,
                      }}
                      title={`Người phụ trách: ${item.assignedTo}`}
                    >
                      👤 {item.assignedTo}
                    </span>
                  )}

                  {/* Tags */}
                  {item.tags &&
                    item.tags.map((t) => (
                      <span
                        key={t.id}
                        style={{
                          fontSize: '10px',
                          color: '#ffffff',
                          backgroundColor: t.color || '#607D8B',
                          padding: '1px 5px',
                          borderRadius: '4px',
                          fontWeight: 600,
                        }}
                      >
                        {t.name}
                      </span>
                    ))}

                  {/* 24h locked badge if Messenger beyond 24h */}
                  {isMessage && item.canReply === false && (
                    <span
                      style={{
                        fontSize: '10px',
                        color: 'var(--crm-danger-text)',
                        background: 'var(--crm-danger-light)',
                        padding: '1px 5px',
                        borderRadius: '4px',
                        fontWeight: 700,
                      }}
                      title="Quá 24 giờ kể từ tin nhắn cuối của khách"
                    >
                      ⏰ Quá 24h
                    </span>
                  )}

                  {/* Unread badge */}
                  {item.unreadCount > 0 && (
                    <span
                      style={{
                        marginLeft: 'auto',
                        backgroundColor: 'var(--crm-danger)',
                        color: '#ffffff',
                        fontSize: '10px',
                        fontWeight: 700,
                        padding: '1px 6px',
                        borderRadius: '10px',
                      }}
                    >
                      {item.unreadCount}
                    </span>
                  )}
                </div>
              </div>
            </div>
          )
        })
      )}
    </div>
  )
}

export default InboxList
