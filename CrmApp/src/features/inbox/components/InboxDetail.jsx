import React, { useState } from 'react'
import Button from '../../../shared/components/Button'
import Badge from '../../../shared/components/Badge'
import Icon from '../../../shared/components/Icon'
import { formatVietnamDateTime } from '../../../shared/utils/dateUtils'

const STATUS_OPTIONS = [
  { value: 1, label: 'Mới', variant: 'danger' },
  { value: 2, label: 'Đang xử lý', variant: 'warning' },
  { value: 3, label: 'Đã trả lời', variant: 'success' },
  { value: 4, label: 'Bỏ qua', variant: 'default' },
]

export const InboxDetail = ({
  item,
  detail,
  loading = false,
  users = [],
  tags = [],
  canCare = true,
  isReadOnly = false,
  onBack,
  onSendReply,
  onAddNote,
  onChangeStatus,
  onAssign,
  onAttachTag,
  onDetachTag,
  onNavigateCustomer,
  onCreateReminder,
}) => {
  const [replyText, setReplyText] = useState('')
  const [sending, setSending] = useState(false)
  const [noteOpen, setNoteOpen] = useState(false)
  const [noteText, setNoteText] = useState('')
  const [tagSelectOpen, setTagSelectOpen] = useState(false)

  if (!item) {
    return (
      <div className="crm-chat-pane" style={{ display: 'flex', alignItems: 'center', justifyContent: 'center', color: 'var(--crm-text-muted)' }}>
        <p>Chọn một hội thoại hoặc bình luận để xem nội dung chi tiết</p>
      </div>
    )
  }

  const isMessage = item.kind === 1
  const conv = isMessage ? detail?.conversation || {} : null
  const thread = !isMessage ? detail?.thread || {} : null

  // 24h response window logic:
  // For Messenger (isMessage): canReply flag is provided by backend based on 24h window
  // For Comments: canReply is generally true unless restricted
  const canSend = isMessage ? (conv?.canReply ?? item.canReply) : (thread?.capabilities?.canReply ?? true)

  const handleSend = async (e) => {
    e.preventDefault()
    if (!replyText.trim() || sending || isReadOnly || !canSend) return
    setSending(true)
    try {
      await onSendReply(replyText.trim())
      setReplyText('')
    } catch (err) {
      alert('Lỗi gửi phản hồi: ' + (err?.response?.data?.message || err?.message))
    } finally {
      setSending(false)
    }
  }

  const handleSaveNote = async () => {
    if (!noteText.trim()) return
    try {
      await onAddNote(noteText.trim())
      setNoteText('')
      setNoteOpen(false)
    } catch (err) {
      alert('Lỗi lưu ghi chú: ' + (err?.response?.data?.message || err?.message))
    }
  }

  const handleStatusChange = async (e) => {
    const newStatus = parseInt(e.target.value, 10)
    if (newStatus && onChangeStatus) {
      await onChangeStatus(newStatus)
    }
  }

  const handleAssignChange = async (e) => {
    const userId = e.target.value || null
    const selectedUser = users.find((u) => u.id === userId)
    const userName = selectedUser ? (selectedUser.displayName || selectedUser.userName) : null
    if (onAssign) {
      await onAssign(userId, userName)
    }
  }

  const handleAttachTagSelect = async (e) => {
    const tagId = e.target.value
    if (tagId && onAttachTag) {
      await onAttachTag(tagId)
      setTagSelectOpen(false)
    }
  }

  // Active tags attached to this item
  const currentTags = detail?.tags || item.tags || []
  const availableTags = tags.filter((t) => !currentTags.some((ct) => ct.id === t.id))

  return (
    <div className="crm-chat-pane" data-testid="inbox-detail-pane">
      {/* Header */}
      <div className="crm-chat-header" style={{ flexWrap: 'wrap', gap: '12px' }}>
        <div style={{ display: 'flex', alignItems: 'center', gap: '10px' }}>
          {/* Mobile Back Button */}
          {onBack && (
            <Button
              variant="ghost"
              size="sm"
              onClick={onBack}
              data-testid="btn-back-to-list"
              style={{ padding: '6px 8px' }}
            >
              ← Danh sách
            </Button>
          )}

          <div>
            <div style={{ display: 'flex', alignItems: 'center', gap: '8px' }}>
              <h3 style={{ margin: 0, fontSize: '16px', fontWeight: '700' }} data-testid="detail-display-name">
                {item.displayName || 'Khách hàng'}
              </h3>
              <Badge variant={isMessage ? 'primary' : 'default'} size="sm">
                {isMessage ? '💬 Tin nhắn' : '📝 Bình luận'}
              </Badge>
            </div>
            <div style={{ fontSize: '13px', color: 'var(--crm-text-muted)', marginTop: '2px', display: 'flex', gap: '8px', alignItems: 'center', flexWrap: 'wrap' }}>
              <span>Kênh: <strong>{item.channelName || 'Facebook'}</strong></span>
              <span>•</span>
              <span>Thời gian: {formatVietnamDateTime(item.lastCustomerActivityAt)}</span>

              {/* View Customer Profile Link */}
              {onNavigateCustomer && (
                <>
                  <span>•</span>
                  <button
                    type="button"
                    onClick={() => onNavigateCustomer(item)}
                    style={{
                      background: 'none',
                      border: 'none',
                      color: 'var(--crm-primary)',
                      cursor: 'pointer',
                      padding: 0,
                      fontWeight: 600,
                      textDecoration: 'underline',
                    }}
                    data-testid="btn-view-customer-profile"
                  >
                    Xem hồ sơ khách →
                  </button>
                </>
              )}
            </div>
          </div>
        </div>

        {/* Care Action Bar: Status, Assign, Note, Reminder, Tag */}
        <div className="crm-chat-actions" data-testid="care-actions" style={{ flexWrap: 'wrap' }}>
          {canCare && (
            <>
              {/* Status Select */}
              <select
                className="crm-form-input"
                style={{ fontSize: '12px', padding: '6px 10px', width: 'auto', cursor: 'pointer' }}
                value={item.status || 1}
                onChange={handleStatusChange}
                data-testid="select-change-status"
              >
                {STATUS_OPTIONS.map((st) => (
                  <option key={st.value} value={st.value}>
                    {st.label}
                  </option>
                ))}
              </select>

              {/* Assignee Select */}
              <select
                className="crm-form-input"
                style={{ fontSize: '12px', padding: '6px 10px', width: 'auto', cursor: 'pointer' }}
                value={item.assignedUserId || ''}
                onChange={handleAssignChange}
                data-testid="select-assign-user"
              >
                <option value="">⚪ Chưa gán</option>
                {users.map((u) => (
                  <option key={u.id} value={u.id}>
                    👤 {u.displayName || u.userName}
                  </option>
                ))}
              </select>

              {/* Add Note Button */}
              <Button
                variant="secondary"
                size="sm"
                onClick={() => setNoteOpen(!noteOpen)}
                data-testid="btn-add-note"
              >
                Ghi chú
              </Button>

              {/* Reminder Button */}
              <Button
                variant="outline"
                size="sm"
                onClick={() => {
                  if (onCreateReminder) {
                    onCreateReminder(item)
                  } else {
                    alert('Đã tạo việc nhắc xử lý cho khách hàng')
                  }
                }}
                data-testid="btn-create-reminder"
              >
                Nhắc việc
              </Button>

              {/* Tag Button */}
              <Button
                variant="ghost"
                size="sm"
                onClick={() => setTagSelectOpen(!tagSelectOpen)}
                data-testid="btn-toggle-tag-select"
              >
                🏷️ Thêm tag
              </Button>
            </>
          )}
        </div>
      </div>

      {/* Tag Chips Bar */}
      <div style={{ padding: '8px 20px', background: 'var(--crm-surface-subtle)', borderBottom: '1px solid var(--crm-border)', display: 'flex', gap: '8px', alignItems: 'center', flexWrap: 'wrap' }} data-testid="detail-tags-bar">
        <span style={{ fontSize: '12px', fontWeight: 600, color: 'var(--crm-text-muted)' }}>Tags:</span>
        {currentTags.length === 0 ? (
          <span style={{ fontSize: '12px', color: 'var(--crm-text-muted)' }}>Chưa có tag</span>
        ) : (
          currentTags.map((t) => (
            <span
              key={t.id}
              style={{
                display: 'inline-flex',
                alignItems: 'center',
                gap: '4px',
                fontSize: '11px',
                padding: '2px 8px',
                borderRadius: '4px',
                backgroundColor: t.color || '#607D8B',
                color: '#ffffff',
                fontWeight: 600,
              }}
              data-testid={`tag-badge-${t.id}`}
            >
              {t.name}
              {canCare && onDetachTag && (
                <button
                  type="button"
                  onClick={() => onDetachTag(t.id)}
                  style={{
                    background: 'none',
                    border: 'none',
                    color: '#ffffff',
                    cursor: 'pointer',
                    padding: 0,
                    marginLeft: '2px',
                    fontSize: '12px',
                    fontWeight: 700,
                  }}
                  title="Gỡ tag"
                  data-testid={`btn-detach-tag-${t.id}`}
                >
                  ×
                </button>
              )}
            </span>
          ))
        )}

        {/* Tag Selection Dropdown */}
        {tagSelectOpen && canCare && (
          <select
            className="crm-form-input"
            style={{ fontSize: '11px', padding: '4px 8px', width: 'auto', cursor: 'pointer' }}
            onChange={handleAttachTagSelect}
            defaultValue=""
            data-testid="select-attach-tag"
          >
            <option value="" disabled>Chọn tag để gắn...</option>
            {availableTags.map((t) => (
              <option key={t.id} value={t.id}>
                + {t.name}
              </option>
            ))}
          </select>
        )}
      </div>

      {/* Internal Note Panel */}
      {noteOpen && canCare && (
        <div style={{ padding: '12px 20px', background: 'var(--crm-warning-light)', borderBottom: '1px solid var(--crm-border)' }} data-testid="inbox-note-panel">
          <div style={{ display: 'flex', gap: '8px' }}>
            <input
              type="text"
              className="crm-chat-input"
              placeholder="Nhập ghi chú nội bộ cho hội thoại/bình luận này..."
              value={noteText}
              onChange={(e) => setNoteText(e.target.value)}
              data-testid="input-note-text"
            />
            <Button variant="primary" size="sm" onClick={handleSaveNote} data-testid="btn-save-note">
              Lưu
            </Button>
          </div>
          {(conv?.internalNote || thread?.internalNote) && (
            <div style={{ fontSize: '12px', color: 'var(--crm-text-muted)', marginTop: '6px' }}>
              Ghi chú hiện tại: <em>{conv?.internalNote || thread?.internalNote}</em>
            </div>
          )}
        </div>
      )}

      {/* Messages Thread Body */}
      <div className="crm-chat-messages" data-testid="chat-messages">
        {loading ? (
          <div style={{ textAlign: 'center', padding: '32px', color: 'var(--crm-text-muted)' }}>
            Đang tải nội dung chi tiết...
          </div>
        ) : isMessage ? (
          /* Messenger Thread */
          conv?.messages && conv.messages.length > 0 ? (
            conv.messages.map((m) => {
              const isOutgoing = m.isFromPage
              return (
                <div
                  key={m.id || m.externalMessageId}
                  className={`crm-msg-bubble ${isOutgoing ? 'crm-msg-outgoing' : 'crm-msg-incoming'}`}
                  data-testid={`msg-bubble-${m.id || m.externalMessageId}`}
                >
                  <p style={{ margin: 0 }}>{m.text}</p>
                  <span
                    style={{
                      display: 'block',
                      fontSize: '11px',
                      marginTop: '4px',
                      opacity: 0.8,
                      textAlign: isOutgoing ? 'right' : 'left',
                    }}
                  >
                    {formatVietnamDateTime(m.sentAt)}
                  </span>
                </div>
              )
            })
          ) : (
            <div style={{ textAlign: 'center', color: 'var(--crm-text-muted)', padding: '24px' }}>
              Chưa có tin nhắn trong hội thoại này
            </div>
          )
        ) : (
          /* Comment Thread */
          <div>
            {/* Post Preview Info */}
            {thread?.postMessage && (
              <div
                style={{
                  background: 'var(--crm-surface)',
                  border: '1px solid var(--crm-border)',
                  borderRadius: 'var(--crm-radius-md)',
                  padding: '12px 16px',
                  marginBottom: '16px',
                }}
                data-testid="comment-post-preview"
              >
                <div style={{ fontSize: '12px', fontWeight: 600, color: 'var(--crm-primary)', marginBottom: '4px' }}>
                  Bài viết gốc:
                </div>
                <div style={{ fontSize: '13px', color: 'var(--crm-text)' }}>{thread.postMessage}</div>
                {thread.postPermalinkUrl && (
                  <a
                    href={thread.postPermalinkUrl}
                    target="_blank"
                    rel="noreferrer"
                    style={{ fontSize: '12px', color: 'var(--crm-primary)', marginTop: '4px', display: 'inline-block' }}
                  >
                    Xem bài viết trên Facebook ↗
                  </a>
                )}
              </div>
            )}

            {/* Main Customer Comment */}
            <div
              className="crm-msg-bubble crm-msg-incoming"
              style={{ width: '100%', maxWidth: '100%', marginBottom: '12px' }}
              data-testid="main-comment-bubble"
            >
              <div style={{ fontWeight: 700, fontSize: '13px', marginBottom: '4px', color: 'var(--crm-primary)' }}>
                {thread?.authorName || item.displayName || 'Khách hàng'}
              </div>
              <p style={{ margin: 0 }}>{thread?.message || item.snippet}</p>
              <div style={{ display: 'flex', justifyContent: 'space-between', fontSize: '11px', marginTop: '6px', opacity: 0.8 }}>
                <span>{formatVietnamDateTime(thread?.commentedAt || item.lastCustomerActivityAt)}</span>
                <span>👍 {thread?.likeCount || 0} lượt thích</span>
              </div>
            </div>

            {/* Replies List */}
            {thread?.replies && thread.replies.length > 0 && (
              <div style={{ paddingLeft: '24px', display: 'flex', flexDirection: 'column', gap: '8px' }}>
                <div style={{ fontSize: '12px', fontWeight: 600, color: 'var(--crm-text-muted)' }}>
                  Các câu trả lời ({thread.replies.length}):
                </div>
                {thread.replies.map((rep) => (
                  <div
                    key={rep.id || rep.externalCommentId}
                    className={`crm-msg-bubble ${rep.isFromPage ? 'crm-msg-outgoing' : 'crm-msg-incoming'}`}
                    style={{ maxWidth: '85%' }}
                    data-testid={`comment-reply-${rep.id}`}
                  >
                    <div style={{ fontWeight: 600, fontSize: '12px', marginBottom: '2px' }}>
                      {rep.authorName || (rep.isFromPage ? 'Fanpage' : 'Khách')}
                    </div>
                    <p style={{ margin: 0 }}>{rep.message}</p>
                    <span style={{ display: 'block', fontSize: '10px', marginTop: '4px', opacity: 0.8, textAlign: 'right' }}>
                      {formatVietnamDateTime(rep.commentedAt)}
                    </span>
                  </div>
                ))}
              </div>
            )}
          </div>
        )}
      </div>

      {/* Footer / Reply Area */}
      <div className="crm-chat-footer">
        {isReadOnly ? (
          <div className="crm-readonly-notice" data-testid="viewer-readonly-notice">
            🔒 Chế độ Chỉ đọc (Viewer) — Thao tác trả lời và chỉnh sửa bị vô hiệu hoá
          </div>
        ) : isMessage && !canSend ? (
          /* 24h locked message box with explanation */
          <div
            style={{
              padding: '16px',
              backgroundColor: '#FEF2F2',
              border: '1px solid #FCA5A5',
              borderRadius: 'var(--crm-radius-md)',
              color: '#991B1B',
            }}
            data-testid="reply-locked-24h"
          >
            <div style={{ display: 'flex', alignItems: 'center', gap: '8px', fontWeight: 700, fontSize: '14px', marginBottom: '4px' }}>
              <span>⚠️</span>
              <span>Khoá gửi tin nhắn (Quá 24 giờ)</span>
            </div>
            <p style={{ margin: 0, fontSize: '13px', lineHeight: 1.5 }}>
              Đã quá cửa sổ 24 giờ kể từ tin nhắn cuối của khách hàng. Theo chính sách của Meta (Standard Messaging 24-hour RESPONSE window), trang không thể gửi thêm tin nhắn trả lời tự do cho khách.
            </p>
          </div>
        ) : (
          /* Active Reply Form */
          <form onSubmit={handleSend} className="crm-chat-input-box" data-testid="reply-form">
            <input
              type="text"
              className="crm-chat-input"
              placeholder={isMessage ? 'Nhập nội dung phản hồi tin nhắn Messenger...' : 'Nhập câu trả lời bình luận Facebook...'}
              value={replyText}
              onChange={(e) => setReplyText(e.target.value)}
              disabled={sending}
              data-testid="reply-input"
            />
            <Button
              type="submit"
              variant="primary"
              size="md"
              isLoading={sending}
              icon={<Icon name="send" size={16} />}
              data-testid="btn-send-reply"
            >
              Trả lời
            </Button>
          </form>
        )}
      </div>
    </div>
  )
}

export default InboxDetail
