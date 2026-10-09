import React, { useState, useEffect, useCallback } from 'react'
import { useNavigate, useInRouterContext } from 'react-router-dom'
import Button from '../../../shared/components/Button'
import Badge from '../../../shared/components/Badge'
import Icon from '../../../shared/components/Icon'
import { formatVietnamDateTime } from '../../../shared/utils/dateUtils'
import { inboxApi } from '../api/inboxApi'
import { scheduledMessageApi } from '../api/scheduledMessageApi'
import { opportunityApi } from '../../opportunities/api/opportunityApi'
import { toast } from '../../../shared/utils/toast'
import { useAuth } from '../../../auth/useAuth'
import ScheduleMessagePopover from './ScheduleMessagePopover'
import ScheduledMessageList from './ScheduledMessageList'

function useSafeNavigate() {
  const inRouter = useInRouterContext()
  const nav = inRouter ? useNavigate() : null
  return nav || ((to) => {
    if (typeof window !== 'undefined') window.location.href = to
  })
}

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
  canCare,
  isReadOnly,
  onBack,
  onSendReply,
  onAddNote,
  onChangeStatus,
  onAssign,
  onAttachTag,
  onDetachTag,
  onNavigateCustomer,
  linkedCustomerId,
}) => {
  const [replyText, setReplyText] = useState('')
  const [sending, setSending] = useState(false)
  const [noteOpen, setNoteOpen] = useState(false)
  const [noteText, setNoteText] = useState('')
  const [tagSelectOpen, setTagSelectOpen] = useState(false)
  const [aiLoading, setAiLoading] = useState(false)
  const [aiError, setAiError] = useState(null)

  const navigate = useSafeNavigate()
  const [openOpportunity, setOpenOpportunity] = useState(null)
  const [loadingOpportunity, setLoadingOpportunity] = useState(false)
  const [creatingOpportunity, setCreatingOpportunity] = useState(false)

  // Scheduled messages state
  const [scheduledMessages, setScheduledMessages] = useState([])
  const [scheduleModalOpen, setScheduleModalOpen] = useState(false)
  const [editingScheduleMessage, setEditingScheduleMessage] = useState(null)
  const [savingSchedule, setSavingSchedule] = useState(false)

  const auth = useAuth()
  const effectiveCanCare = canCare !== undefined ? canCare : auth.canCare
  const effectiveIsReadOnly = isReadOnly !== undefined ? isReadOnly : auth.isReadOnly
  const canSuggestAi = effectiveCanCare && !effectiveIsReadOnly

  // Load scheduled messages for message conversation
  const loadScheduledMessages = useCallback(async () => {
    if (!item?.id || item.kind !== 1 || effectiveIsReadOnly) {
      setScheduledMessages([])
      return
    }
    try {
      const list = await scheduledMessageApi.listByConversation(item.id)
      setScheduledMessages(Array.isArray(list) ? list : [])
    } catch {
      setScheduledMessages([])
    }
  }, [item?.id, item?.kind, effectiveIsReadOnly])

  useEffect(() => {
    loadScheduledMessages()
  }, [loadScheduledMessages])

  useEffect(() => {
    let active = true
    if (!item?.id) {
      setOpenOpportunity(null)
      return
    }
    const checkOpp = async () => {
      setLoadingOpportunity(true)
      try {
        const opp = await opportunityApi.byConversation(
          item.kind === 2 ? 'comment' : 'message',
          item.id,
        )
        if (active) {
          if (opp && (opp.status === 1 || !opp.status) && !opp.isArchived) {
            setOpenOpportunity(opp)
          } else {
            setOpenOpportunity(null)
          }
        }
      } catch {
        if (active) setOpenOpportunity(null)
      } finally {
        if (active) setLoadingOpportunity(false)
      }
    }
    checkOpp()
    return () => {
      active = false
    }
  }, [item?.id, item?.kind])

  const handleCreateOpportunity = async () => {
    if (creatingOpportunity || !item?.id || effectiveIsReadOnly) return
    setCreatingOpportunity(true)
    try {
      const res = await opportunityApi.fromConversation({
        kind: item.kind === 2 ? 'comment' : 'message',
        id: item.id,
      })
      toast.success('Đã tạo cơ hội từ hội thoại')
      setOpenOpportunity(res)
    } catch (err) {
      toast.error(err?.response?.data?.message || err?.message || 'Không thể tạo cơ hội')
    } finally {
      setCreatingOpportunity(false)
    }
  }

  const handleViewOpportunity = () => {
    if (openOpportunity?.id) {
      navigate(`/tasks?opportunity=${openOpportunity.id}`)
    }
  }

  if (!item) {
    return (
      <div className="crm-chat-pane" style={{ display: 'flex', alignItems: 'center', justifyContent: 'center', color: 'var(--crm-text-muted)' }}>
        <p>Chọn một hội thoại hoặc bình luận để xem nội dung chi tiết</p>
      </div>
    )
  }

  const isMessage = item.kind === 1
  const conv = isMessage ? detail?.conversation : null
  const thread = !isMessage ? detail?.thread : null

  // 24h response window & reply capability logic (N2 safety fix):
  // When detail is not yet loaded (null/undefined) or loading or error: locked (canSend = false).
  // Only unlocked when:
  // - Messenger (isMessage): conv confirms canReply === true or (isReplyWindowOpen === true && conv.canReply !== false)
  // - Comments (!isMessage): thread.capabilities confirms canReply === true
  const canSend = Boolean(
    !loading &&
      detail &&
      !effectiveIsReadOnly &&
      (isMessage
        ? conv?.canReply === true || (conv?.isReplyWindowOpen === true && conv?.canReply !== false)
        : thread?.capabilities?.canReply === true),
  )

  const isWindowClosed = Boolean(
    !loading &&
      detail &&
      isMessage &&
      (conv?.isReplyWindowOpen === false || conv?.canReply === false),
  )

  const handleSuggestAi = async () => {
    if (aiLoading || !canSuggestAi) return
    setAiLoading(true)
    setAiError(null)
    try {
      const res = await inboxApi.suggestReply(item.kind, item.id)
      const draft = res?.draft || res?.data?.draft || (typeof res === 'string' ? res : '')
      if (draft) {
        setReplyText(draft)
      }
    } catch (err) {
      const msg = err?.response?.data?.message || err?.message || 'Không thể tạo bản nháp gợi ý từ AI'
      setAiError(msg)
      toast.error(msg)
    } finally {
      setAiLoading(false)
    }
  }

  const handleSend = async (e) => {
    e.preventDefault()
    if (!replyText.trim() || sending || effectiveIsReadOnly || !canSend) return
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


  const handleOpenScheduleModal = () => {
    if (!canSend || effectiveIsReadOnly || !isMessage) return
    setEditingScheduleMessage(null)
    setScheduleModalOpen(true)
  }

  const handleOpenEditSchedule = (msg) => {
    if (effectiveIsReadOnly) return
    setEditingScheduleMessage(msg)
    setScheduleModalOpen(true)
  }

  const handleConfirmSchedule = async ({ text, scheduledAtUtc }) => {
    setSavingSchedule(true)
    try {
      if (editingScheduleMessage) {
        await scheduledMessageApi.update(editingScheduleMessage.id, { text, scheduledAtUtc })
        toast.success('Đã cập nhật tin hẹn giờ')
      } else {
        await scheduledMessageApi.create({
          pageConversationId: item.id,
          text,
          scheduledAtUtc,
        })
        toast.success('Đã hẹn giờ gửi tin')
        setReplyText('')
      }
      setScheduleModalOpen(false)
      setEditingScheduleMessage(null)
      await loadScheduledMessages()
    } catch (err) {
      toast.error(err?.response?.data?.message || err?.message || 'Không thể lưu tin hẹn giờ')
    } finally {
      setSavingSchedule(false)
    }
  }

  const handleCancelSchedule = async (id) => {
    try {
      await scheduledMessageApi.cancel(id)
      toast.success('Đã huỷ tin hẹn giờ')
      await loadScheduledMessages()
    } catch (err) {
      toast.error(err?.response?.data?.message || err?.message || 'Không thể huỷ tin hẹn giờ')
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
                <><Icon name={isMessage ? 'message' : 'note'} size={12} /> {isMessage ? 'Tin nhắn' : 'Bình luận'}</>
              </Badge>
            </div>
            <div style={{ fontSize: '13px', color: 'var(--crm-text-muted)', marginTop: '2px', display: 'flex', gap: '8px', alignItems: 'center', flexWrap: 'wrap' }}>
              <span>Kênh: <strong>{item.channelName || 'Facebook'}</strong></span>
              <span>•</span>
              <span>Thời gian: {formatVietnamDateTime(item.lastCustomerActivityAt)}</span>

              {/* View Customer Profile Link — chỉ hiện khi đã liên kết hồ sơ */}
              {linkedCustomerId && onNavigateCustomer && (
                <>
                  <span>•</span>
                  <button
                    type="button"
                    onClick={() => onNavigateCustomer(linkedCustomerId)}
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
                <option value="">Chưa gán</option>
                {users.map((u) => (
                  <option key={u.id} value={u.id}>
                    {u.displayName || u.userName}
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

              {/* Tag Button */}
              <Button
                variant="ghost"
                size="sm"
                onClick={() => setTagSelectOpen(!tagSelectOpen)}
                data-testid="btn-toggle-tag-select"
              >
                <Icon name="tag" size={16} /> Thêm tag
              </Button>

              {/* Opportunity Action: Xem cơ hội vs Tạo cơ hội */}
              {openOpportunity ? (
                <Button
                  variant="outline"
                  size="sm"
                  onClick={handleViewOpportunity}
                  data-testid="btn-view-opportunity"
                  title="Xem cơ hội đang mở"
                >
                  <Icon name="target" size={16} /> Xem cơ hội
                </Button>
              ) : (
                !effectiveIsReadOnly && (
                  <Button
                    variant="outline"
                    size="sm"
                    onClick={handleCreateOpportunity}
                    isLoading={creatingOpportunity}
                    data-testid="btn-create-opportunity"
                    title="Tạo cơ hội từ hội thoại này"
                  >
                    + Tạo cơ hội
                  </Button>
                )
              )}
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
                    Xem bài viết trên Facebook <Icon name="external-link" size={12} />
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
                <span><Icon name="thumbs-up" size={12} /> {thread?.likeCount || 0} lượt thích</span>
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

      {/* Scheduled Messages List */}
      {isMessage && scheduledMessages.length > 0 && (
        <ScheduledMessageList
          messages={scheduledMessages}
          onEdit={handleOpenEditSchedule}
          onCancel={handleCancelSchedule}
          isReadOnly={effectiveIsReadOnly}
        />
      )}

      {/* Footer / Reply Area */}
      <div className="crm-chat-footer">
        {effectiveIsReadOnly ? (
          <div className="crm-readonly-notice" data-testid="viewer-readonly-notice">
            <Icon name="lock" size={14} /> Chế độ Chỉ đọc (Viewer) — Thao tác trả lời và chỉnh sửa bị vô hiệu hoá
          </div>
        ) : (
          <>
            {/* 24h locked banner when window is confirmed closed */}
            {isWindowClosed && (
              <div
                style={{
                  padding: '12px 16px',
                  marginBottom: '10px',
                  backgroundColor: '#FEF2F2',
                  border: '1px solid #FCA5A5',
                  borderRadius: 'var(--crm-radius-md)',
                  color: '#991B1B',
                }}
                data-testid="reply-locked-24h"
              >
                <div style={{ display: 'flex', alignItems: 'center', gap: '8px', fontWeight: 700, fontSize: '14px', marginBottom: '4px' }}>
                  <Icon name="alert" size={16} />
                  <span>Khoá gửi tin nhắn (Quá 24 giờ)</span>
                </div>
                <p style={{ margin: 0, fontSize: '13px', lineHeight: 1.5 }}>
                  Đã quá cửa sổ 24 giờ kể từ tin nhắn cuối của khách hàng. Theo chính sách của Meta (Standard Messaging 24-hour RESPONSE window), trang không thể gửi thêm tin nhắn trả lời tự do cho khách.
                </p>
              </div>
            )}

            {/* AI suggest error toast banner */}
            {aiError && (
              <div
                className="crm-toast-error"
                data-testid="ai-suggest-toast"
                style={{
                  padding: '8px 12px',
                  marginBottom: '10px',
                  backgroundColor: '#FEF2F2',
                  border: '1px solid #FCA5A5',
                  borderRadius: 'var(--crm-radius-sm)',
                  color: '#991B1B',
                  fontSize: '13px',
                  display: 'flex',
                  justifyContent: 'space-between',
                  alignItems: 'center',
                }}
              >
                <span>{aiError}</span>
                <button
                  type="button"
                  onClick={() => setAiError(null)}
                  style={{ background: 'none', border: 'none', cursor: 'pointer', color: '#991B1B', fontWeight: 'bold' }}
                  aria-label="Đóng thông báo"
                >
                  ×
                </button>
              </div>
            )}

            {/* Reply Form */}
            <form onSubmit={handleSend} className="crm-chat-input-box" data-testid="reply-form">
              <input
                type="text"
                className="crm-chat-input"
                placeholder={
                  isMessage
                    ? isWindowClosed
                      ? 'Cửa sổ 24 giờ đã đóng — không thể gửi tin nhắn'
                      : loading
                        ? 'Đang tải thông tin hội thoại...'
                        : 'Nhập nội dung phản hồi tin nhắn Messenger...'
                    : loading
                      ? 'Đang tải thông tin bình luận...'
                      : 'Nhập câu trả lời bình luận Facebook...'
                }
                value={replyText}
                onChange={(e) => setReplyText(e.target.value)}
                disabled={!canSend || sending}
                data-testid="reply-input"
              />
              {canSuggestAi && (
                <Button
                  type="button"
                  variant="secondary"
                  size="md"
                  onClick={handleSuggestAi}
                  isLoading={aiLoading}
                  disabled={aiLoading}
                  data-testid="btn-ai-suggest"
                  title="AI gợi ý trả lời (chỉ tạo bản nháp)"
                >
                  <Icon name="sparkles" size={16} /> AI gợi ý
                </Button>
              )}
              {isMessage && !effectiveIsReadOnly && (
                <Button
                  type="button"
                  variant="secondary"
                  size="md"
                  disabled={!canSend || sending}
                  onClick={handleOpenScheduleModal}
                  data-testid="btn-schedule-send"
                  title="Hẹn giờ gửi tin nhắn"
                >
                  <Icon name="clock" size={16} /> Hẹn giờ gửi
                </Button>
              )}
              <Button
                type="submit"
                variant="primary"
                size="md"
                isLoading={sending}
                disabled={!canSend || sending}
                icon={<Icon name="send" size={16} />}
                data-testid="btn-send-reply"
              >
                Trả lời
              </Button>
            </form>
          </>
        )}
      </div>

      {/* Schedule Message Popover/Modal */}
      {isMessage && !effectiveIsReadOnly && (
        <ScheduleMessagePopover
          isOpen={scheduleModalOpen}
          onClose={() => {
            setScheduleModalOpen(false)
            setEditingScheduleMessage(null)
          }}
          onConfirm={handleConfirmSchedule}
          replyWindowClosesAt={conv?.replyWindowClosesAt || item?.replyWindowClosesAt}
          initialText={editingScheduleMessage ? editingScheduleMessage.text : replyText}
          initialScheduledAt={editingScheduleMessage ? editingScheduleMessage.scheduledAtUtc : null}
          isEdit={Boolean(editingScheduleMessage)}
          isLoading={savingSchedule}
        />
      )}
    </div>
  )
}

export default InboxDetail
