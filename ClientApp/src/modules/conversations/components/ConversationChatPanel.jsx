import { useEffect, useMemo, useRef, useState } from 'react'
import { Link } from 'react-router-dom'
import LoadingState from '@/shared/components/LoadingState'
import ErrorState from '@/shared/components/ErrorState'
import EmptyState from '@/shared/components/EmptyState'
import StatusBadge from '@/shared/components/StatusBadge'
import { formatDateTime, getErrorMessage } from '@/shared/utils/apiHelpers'
import { toast } from '@/shared/stores/toastStore'
import { usePermissions } from '@/shared/hooks/usePermissions'
import {
  usePageConversation,
  usePageMessageWorkflow,
  useSendPageMessage,
} from '@/modules/messages/hooks/usePageMessages'
import {
  useAssignComment,
  useCommentModeration,
  useCommentNote,
  useCommentThread,
  useReplyComment,
  useSetCommentStatus,
} from '@/modules/comments/hooks/useComments'
import { useSuggestReply } from '../hooks/useInbox'
import './ConversationChatPanel.css'

const STATUS_MAP = {
  1: { label: 'Mới', tone: 'info' },
  2: { label: 'Đang xử lý', tone: 'warning' },
  3: { label: 'Đã trả lời', tone: 'success' },
  4: { label: 'Bỏ qua', tone: 'neutral' },
}

function statusMeta(value) {
  return STATUS_MAP[value] || { label: `Status ${value}`, tone: 'neutral' }
}

function initials(name) {
  const value = String(name || '?').trim()
  return value
    .split(/\s+/)
    .slice(0, 2)
    .map((part) => part[0])
    .join('')
    .toUpperCase()
}

function parseAttachments(raw) {
  if (!raw) return []
  try {
    const parsed = JSON.parse(raw)
    const data = Array.isArray(parsed?.data) ? parsed.data : Array.isArray(parsed) ? parsed : []
    return data
      .map((item) => ({
        type: item?.mime_type || item?.type || 'file',
        url: item?.image_data?.url || item?.file_url || item?.video_data?.url || item?.url,
        name: item?.name || item?.type || 'Tệp đính kèm',
      }))
      .filter((item) => item.url)
  } catch {
    return []
  }
}

function MessageBubble({ message }) {
  const attachments = parseAttachments(message.attachmentsJson)
  return (
    <div className={`chat-message-row${message.isFromPage ? ' is-page' : ' is-customer'}`}>
      <div className="chat-message-bubble">
        {message.text && <p className="chat-message-text">{message.text}</p>}
        {attachments.map((attachment) => (
          <a
            key={attachment.url}
            href={attachment.url}
            target="_blank"
            rel="noreferrer"
            className="chat-message-attachment"
          >
            {attachment.type?.startsWith('image') ? (
              <img src={attachment.url} alt={attachment.name} />
            ) : (
              <span>📎 {attachment.name}</span>
            )}
          </a>
        ))}
        <div className="chat-message-time">
          {formatDateTime(message.sentAt)}
          {message.isFromPage && message.isRead ? ' · Đã xem' : ''}
          {message.isFromPage && !message.isRead && message.isDelivered ? ' · Đã nhận' : ''}
        </div>
      </div>
    </div>
  )
}

function CommentNode({ comment, depth = 0 }) {
  return (
    <div className={`comment-tree-node depth-${Math.min(depth, 4)}`}>
      <div className="comment-node-header">
        <span className="comment-node-author">
          {comment.authorName || comment.authorUsername || 'Ẩn danh'}
        </span>
        <span className="comment-node-time">
          {formatDateTime(comment.commentedAt || comment.createdAt)}
        </span>
        {comment.isFromPage && <StatusBadge label="Page" tone="info" />}
        {comment.isHidden && <StatusBadge label="Đã ẩn" tone="warning" />}
        {comment.isPending && <StatusBadge label="Pending" tone="warning" />}
      </div>
      <p className="comment-node-body">{comment.message || '(không có nội dung)'}</p>
      {comment.replies?.length > 0 && (
        <div className="comment-node-children">
          {comment.replies.map((child) => (
            <CommentNode key={child.id} comment={child} depth={depth + 1} />
          ))}
        </div>
      )}
    </div>
  )
}

export default function ConversationChatPanel({
  kind,
  id,
  conversation,
  onBack,
  onToggleCustomerInfo,
  isCustomerInfoOpen,
}) {
  const permissions = usePermissions()
  const canAct = permissions.hasRole(['Admin', 'ContentManager', 'Reviewer'])

  // Message queries & mutations
  const isMessage = (kind || conversation?.kind) === 'message'
  const messageDetailQuery = usePageConversation(isMessage ? id : null)
  const sendMsgMutation = useSendPageMessage()
  const msgWorkflowMutation = usePageMessageWorkflow()

  // Comment queries & mutations
  const commentThreadQuery = useCommentThread(!isMessage ? id : null)
  const replyCmtMutation = useReplyComment()
  const cmtModerationMutation = useCommentModeration()
  const cmtStatusMutation = useSetCommentStatus()
  const cmtAssignMutation = useAssignComment()
  const cmtNoteMutation = useCommentNote()

  // AI suggest mutation
  const suggestReplyMutation = useSuggestReply()

  // Local state
  const [text, setText] = useState('')
  const [assignedTo, setAssignedTo] = useState('')
  const [note, setNote] = useState('')
  const [activeWorkflowModal, setActiveWorkflowModal] = useState(null) // 'assign' | 'note' | null

  const threadRef = useRef(null)

  // Sync state when detail changes
  const messageData = messageDetailQuery.data
  const commentData = commentThreadQuery.data

  useEffect(() => {
    setText('')
    setActiveWorkflowModal(null)
  }, [id, kind])

  useEffect(() => {
    if (isMessage && messageData) {
      setAssignedTo(messageData.assignedTo || '')
      setNote(messageData.internalNote || '')
    } else if (!isMessage && commentData) {
      setAssignedTo(commentData.assignedTo || '')
      setNote(commentData.internalNote || '')
    }
  }, [isMessage, messageData, commentData])

  useEffect(() => {
    if (threadRef.current) {
      threadRef.current.scrollTop = threadRef.current.scrollHeight
    }
  }, [messageData?.messages, commentData])

  if (!id) {
    return (
      <div className="conversation-chat-panel conversation-chat-panel--empty" data-testid="chat-panel-empty">
        <svg
          xmlns="http://www.w3.org/2000/svg"
          width="48"
          height="48"
          viewBox="0 0 24 24"
          fill="none"
          stroke="currentColor"
          strokeWidth="1.5"
          strokeLinecap="round"
          strokeLinejoin="round"
          className="empty-chat-icon"
        >
          <path d="M21 15a2 2 0 0 1-2 2H7l-4 4V5a2 2 0 0 1 2-2h14a2 2 0 0 1 2 2z" />
        </svg>
        <p>Chọn một hội thoại để bắt đầu xem chi tiết và trả lời</p>
      </div>
    )
  }

  const isLoading = isMessage ? messageDetailQuery.isLoading : commentThreadQuery.isLoading
  const isError = isMessage ? messageDetailQuery.isError : commentThreadQuery.isError
  const error = isMessage ? messageDetailQuery.error : commentThreadQuery.error
  const refetch = isMessage ? messageDetailQuery.refetch : commentThreadQuery.refetch

  const name = isMessage
    ? messageData?.participantName || messageData?.participantExternalId || conversation?.participantName || 'Khách hàng'
    : commentData?.authorName || commentData?.authorUsername || conversation?.participantName || 'Bình luận viên'

  const channelName = isMessage
    ? messageData?.channelName || conversation?.channelName || 'Facebook Page'
    : commentData?.channelName || conversation?.channelName || 'Kênh'

  const currentStatus = isMessage
    ? messageData?.inboxStatus ?? conversation?.inboxStatus ?? 1
    : commentData?.inboxStatus ?? conversation?.inboxStatus ?? 1
  const meta = statusMeta(currentStatus)

  const isReplyWindowOpen = isMessage ? messageData?.isReplyWindowOpen ?? true : true
  const replyWindowClosesAt = isMessage ? messageData?.replyWindowClosesAt : null
  const caps = commentData?.capabilities || {}

  // Handlers for sending messages / comments
  const handleSend = async () => {
    if (!text.trim()) return
    if (isMessage) {
      if (!isReplyWindowOpen) return
      try {
        await sendMsgMutation.mutateAsync({ id, text: text.trim() })
        setText('')
        toast.success('Đã gửi tin nhắn')
      } catch (err) {
        toast.error(getErrorMessage(err))
      }
    } else {
      try {
        await replyCmtMutation.mutateAsync({ id, message: text.trim() })
        setText('')
        toast.success('Đã gửi trả lời')
      } catch (err) {
        toast.error(getErrorMessage(err))
      }
    }
  }

  // Handler for AI suggest reply
  const handleAiSuggest = async () => {
    try {
      const res = await suggestReplyMutation.mutateAsync({
        kind: isMessage ? 'message' : 'comment',
        id,
      })
      const draft = res?.draft || res?.data?.draft || (typeof res === 'string' ? res : '')
      if (draft) {
        setText(draft)
        toast.success('Đã tạo bản nháp gợi ý từ AI')
      } else {
        toast.info('AI chưa có gợi ý phù hợp')
      }
    } catch (err) {
      toast.error(getErrorMessage(err) || 'Không thể tạo gợi ý từ AI')
      // Giữ nguyên text đang gõ, không xóa
    }
  }

  // Handler for status workflow
  const handleSetStatus = async (statusCode, label) => {
    try {
      if (isMessage) {
        await msgWorkflowMutation.mutateAsync({ id, action: 'status', value: statusCode })
      } else {
        await cmtStatusMutation.mutateAsync({ id, status: statusCode })
      }
      toast.success(`Đã chuyển trạng thái sang "${label}"`)
    } catch (err) {
      toast.error(getErrorMessage(err))
    }
  }

  // Handler for assigning user
  const handleAssign = async () => {
    try {
      if (isMessage) {
        await msgWorkflowMutation.mutateAsync({ id, action: 'assign', value: assignedTo })
      } else {
        await cmtAssignMutation.mutateAsync({ id, assignedTo })
      }
      setActiveWorkflowModal(null)
      toast.success('Đã gán người xử lý')
    } catch (err) {
      toast.error(getErrorMessage(err))
    }
  }

  // Handler for saving internal note
  const handleSaveNote = async () => {
    try {
      if (isMessage) {
        await msgWorkflowMutation.mutateAsync({ id, action: 'note', value: note })
      } else {
        await cmtNoteMutation.mutateAsync({ id, note })
      }
      setActiveWorkflowModal(null)
      toast.success('Đã lưu ghi chú nội bộ')
    } catch (err) {
      toast.error(getErrorMessage(err))
    }
  }

  // Moderation handlers for comment
  const handleModeration = async (action, approve) => {
    if (action === 'delete') {
      if (!window.confirm('Xóa comment này trên Facebook?')) return
    }
    try {
      await cmtModerationMutation.mutateAsync({ id, action, approve })
      toast.success('Đã cập nhật kiểm duyệt')
    } catch (err) {
      toast.error(getErrorMessage(err))
    }
  }

  return (
    <div className="conversation-chat-panel" data-testid="conversation-chat-panel">
      {/* Header */}
      <header className="chat-header">
        <div className="chat-header-left">
          <button
            type="button"
            className="chat-back-btn"
            onClick={onBack}
            aria-label="Quay lại"
            data-testid="chat-back-btn"
          >
            <svg xmlns="http://www.w3.org/2000/svg" width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
              <path d="m15 18-6-6 6-6" />
            </svg>
            <span>Quay lại</span>
          </button>

          <div className="chat-header-avatar">
            {conversation?.participantAvatarUrl ? (
              <img src={conversation.participantAvatarUrl} alt="" />
            ) : (
              <span>{initials(name)}</span>
            )}
          </div>

          <div className="chat-header-info">
            <div className="chat-header-title-row">
              <h2 className="chat-header-name">{name}</h2>
              <StatusBadge label={meta.label} tone={meta.tone} />
              {isMessage && (
                <StatusBadge
                  label={isReplyWindowOpen ? 'Còn hạn 24 giờ' : 'Đã hết hạn 24 giờ'}
                  tone={isReplyWindowOpen ? 'success' : 'danger'}
                />
              )}
            </div>
            <div className="chat-header-meta">
              <span className="chat-header-channel">{channelName}</span>
              {isMessage && messageData?.messageCount !== undefined && (
                <span> · {messageData.messageCount} tin nhắn</span>
              )}
              {isMessage && replyWindowClosesAt && (
                <small title="Thời điểm đóng cửa sổ phản hồi">
                  {' '}· Đóng: {formatDateTime(replyWindowClosesAt)}
                </small>
              )}
              {!isMessage && commentData?.postPermalinkUrl && (
                <>
                  {' · '}
                  <a
                    href={commentData.postPermalinkUrl}
                    target="_blank"
                    rel="noopener noreferrer"
                    className="chat-header-link"
                  >
                    Xem bài gốc ↗
                  </a>
                </>
              )}
              {!isMessage && commentData?.localPostId && (
                <>
                  {' · '}
                  <Link to={`/posts/${commentData.localPostId}`} className="chat-header-link">
                    Bài nội bộ
                  </Link>
                </>
              )}
            </div>
          </div>
        </div>

        <div className="chat-header-actions">
          {/* Workflow & Moderation buttons (Viewer không thấy) */}
          {canAct && (
            <div className="chat-action-buttons">
              {/* Quick Status Buttons */}
              <button
                type="button"
                className="btn btn-ghost btn-sm"
                onClick={() => handleSetStatus(2, 'Đang xử lý')}
                data-testid="status-in-progress-btn"
              >
                Đang xử lý
              </button>
              <button
                type="button"
                className="btn btn-ghost btn-sm"
                onClick={() => handleSetStatus(4, 'Bỏ qua')}
                data-testid="status-ignore-btn"
              >
                Bỏ qua
              </button>

              {/* Comment moderation buttons according to capabilities */}
              {!isMessage && (
                <>
                  {caps.canHide && !commentData?.isHidden && (
                    <button
                      type="button"
                      className="btn btn-secondary btn-sm"
                      onClick={() => handleModeration('hide')}
                      data-testid="comment-hide-btn"
                    >
                      Ẩn
                    </button>
                  )}
                  {caps.canUnhide && commentData?.isHidden && (
                    <button
                      type="button"
                      className="btn btn-secondary btn-sm"
                      onClick={() => handleModeration('unhide')}
                      data-testid="comment-unhide-btn"
                    >
                      Hiện
                    </button>
                  )}
                  {caps.canDelete && (
                    <button
                      type="button"
                      className="btn btn-danger btn-sm"
                      onClick={() => handleModeration('delete')}
                      data-testid="comment-delete-btn"
                    >
                      Xóa
                    </button>
                  )}
                  {caps.canManagePending && commentData?.isPending && (
                    <>
                      <button
                        type="button"
                        className="btn btn-secondary btn-sm"
                        onClick={() => handleModeration('pending', true)}
                        data-testid="comment-approve-pending-btn"
                      >
                        Duyệt pending
                      </button>
                      <button
                        type="button"
                        className="btn btn-ghost btn-sm"
                        onClick={() => handleModeration('pending', false)}
                        data-testid="comment-reject-pending-btn"
                      >
                        Bỏ qua pending
                      </button>
                    </>
                  )}
                </>
              )}

              {/* Gán & Ghi chú toggles */}
              <button
                type="button"
                className={`btn btn-secondary btn-sm${activeWorkflowModal === 'assign' ? ' is-active' : ''}`}
                onClick={() => setActiveWorkflowModal((v) => (v === 'assign' ? null : 'assign'))}
                data-testid="toggle-assign-btn"
              >
                Gán
              </button>
              <button
                type="button"
                className={`btn btn-secondary btn-sm${activeWorkflowModal === 'note' ? ' is-active' : ''}`}
                onClick={() => setActiveWorkflowModal((v) => (v === 'note' ? null : 'note'))}
                data-testid="toggle-note-btn"
              >
                Ghi chú
              </button>
            </div>
          )}

          {/* Toggle Customer Info Panel */}
          <button
            type="button"
            className={`chat-header-btn${isCustomerInfoOpen ? ' is-active' : ''}`}
            onClick={onToggleCustomerInfo}
            aria-label="Thông tin khách hàng"
            title="Thông tin khách hàng"
            data-testid="toggle-info-btn"
          >
            <svg xmlns="http://www.w3.org/2000/svg" width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
              <circle cx="12" cy="12" r="10" />
              <line x1="12" y1="16" x2="12" y2="12" />
              <line x1="12" y1="8" x2="12.01" y2="8" />
            </svg>
          </button>
        </div>
      </header>

      {/* Popovers for Assignee and Note */}
      {canAct && activeWorkflowModal === 'assign' && (
        <div className="chat-workflow-popover" role="dialog" aria-label="Khung gán người xử lý">
          <label htmlFor="workflow-assignee-input">Gán người xử lý</label>
          <div className="chat-workflow-input-row">
            <input
              id="workflow-assignee-input"
              data-testid="workflow-assignee-input"
              type="text"
              className="chat-workflow-input"
              value={assignedTo}
              onChange={(e) => setAssignedTo(e.target.value)}
              placeholder="Tên hoặc email..."
            />
            <button
              type="button"
              className="btn btn-secondary btn-sm"
              onClick={handleAssign}
              data-testid="assign-submit-btn"
            >
              Gán
            </button>
          </div>
        </div>
      )}

      {canAct && activeWorkflowModal === 'note' && (
        <div className="chat-workflow-popover" role="dialog" aria-label="Khung ghi chú nội bộ">
          <label htmlFor="workflow-note-input">Ghi chú nội bộ</label>
          <textarea
            id="workflow-note-input"
            data-testid="workflow-note-input"
            rows={2}
            className="chat-workflow-textarea"
            value={note}
            onChange={(e) => setNote(e.target.value)}
            placeholder="Nội dung ghi chú..."
          />
          <div className="chat-workflow-actions">
            <button
              type="button"
              className="btn btn-secondary btn-sm"
              onClick={handleSaveNote}
              data-testid="note-submit-btn"
            >
              Lưu ghi chú
            </button>
          </div>
        </div>
      )}

      {/* Messages / Comments Stream Area */}
      <div className="chat-messages-area" ref={threadRef} data-testid="chat-messages-area">
        {isLoading && <LoadingState message="Đang tải nội dung hội thoại..." />}
        {isError && <ErrorState message={getErrorMessage(error)} onRetry={refetch} />}

        {!isLoading && !isError && isMessage && (
          <div className="chat-message-thread">
            {messageData?.messages?.length > 0 ? (
              messageData.messages.map((message) => (
                <MessageBubble key={message.id} message={message} />
              ))
            ) : (
              <EmptyState message="Chưa có tin nhắn trong hội thoại này." />
            )}
          </div>
        )}

        {!isLoading && !isError && !isMessage && commentData && (
          <div className="chat-comment-thread">
            {commentData.postMessage && (
              <div className="chat-post-preview">
                <span className="chat-post-label">Bài viết</span>
                <p>{commentData.postMessage}</p>
              </div>
            )}
            <CommentNode comment={commentData} />
          </div>
        )}
      </div>

      {/* Composer Area */}
      {canAct ? (
        <footer className="chat-composer-area" data-testid="chat-composer-area">
          {/* Cảnh báo cửa sổ 24h Messenger */}
          {isMessage && !isReplyWindowOpen && (
            <div className="alert alert-warning chat-window-banner" role="alert" data-testid="window-24h-alert">
              Cửa sổ 24 giờ đã đóng. Hệ thống khóa gửi để tránh vi phạm chính sách Meta.
            </div>
          )}

          {(!isMessage && caps.canReply === false) ? (
            <div className="chat-readonly-note">Bình luận này không hỗ trợ trả lời trực tiếp.</div>
          ) : (
            <>
              <div className="chat-composer-tools">
                <button
                  type="button"
                  className="composer-ai-btn"
                  onClick={handleAiSuggest}
                  disabled={suggestReplyMutation.isPending}
                  data-testid="ai-suggest-btn"
                  title="AI gợi ý trả lời"
                >
                  {suggestReplyMutation.isPending ? '✨ Đang tạo gợi ý...' : '✨ AI gợi ý'}
                </button>
                <span className="chat-char-count">{text.length} ký tự</span>
              </div>

              <div className="chat-composer-input-row">
                <textarea
                  className="chat-composer-input"
                  rows={2}
                  value={text}
                  onChange={(e) => setText(e.target.value)}
                  placeholder={isMessage ? 'Nhập tin nhắn trả lời...' : 'Nhập nội dung trả lời bình luận...'}
                  disabled={isMessage && !isReplyWindowOpen}
                  data-testid="chat-composer-textarea"
                />
                <button
                  type="button"
                  className="btn btn-primary chat-send-btn"
                  onClick={handleSend}
                  disabled={
                    (isMessage && !isReplyWindowOpen)
                    || !text.trim()
                    || (isMessage ? sendMsgMutation.isPending : replyCmtMutation.isPending)
                  }
                  data-testid="chat-send-btn"
                >
                  {isMessage
                    ? (sendMsgMutation.isPending ? 'Đang gửi...' : 'Gửi tin nhắn')
                    : (replyCmtMutation.isPending ? 'Đang gửi...' : 'Gửi trả lời')}
                </button>
              </div>
            </>
          )}
        </footer>
      ) : (
        <footer className="chat-composer-area chat-composer-readonly" data-testid="chat-composer-readonly">
          <p className="chat-readonly-note">Bạn có quyền xem, không có quyền trả lời hội thoại này.</p>
        </footer>
      )}
    </div>
  )
}
