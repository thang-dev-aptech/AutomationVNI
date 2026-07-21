import { useEffect, useMemo, useRef, useState } from 'react'
import PageHeader from '@/shared/components/PageHeader'
import LoadingState from '@/shared/components/LoadingState'
import ErrorState from '@/shared/components/ErrorState'
import EmptyState from '@/shared/components/EmptyState'
import StatusBadge from '@/shared/components/StatusBadge'
import { formatDateTime, getErrorMessage } from '@/shared/utils/apiHelpers'
import { toast } from '@/shared/stores/toastStore'
import { usePermissions } from '@/shared/hooks/usePermissions'
import { useSocialChannelAll } from '@/modules/social-channels/hooks/useSocialChannels'
import {
  usePageConversation,
  usePageMessageList,
  usePageMessageSummary,
  usePageMessageWorkflow,
  useSendPageMessage,
  useSubscribePageMessages,
  useSyncPageMessages,
} from '../hooks/usePageMessages'
import './PageMessagesInboxPage.css'

const STATUS = {
  1: { label: 'Mới', tone: 'info' },
  2: { label: 'Đang xử lý', tone: 'warning' },
  3: { label: 'Đã trả lời', tone: 'success' },
  4: { label: 'Bỏ qua', tone: 'neutral' },
}

function statusMeta(value) {
  return STATUS[value] || { label: `Status ${value}`, tone: 'neutral' }
}

function initials(name) {
  const value = String(name || '?').trim()
  return value.split(/\s+/).slice(0, 2).map((part) => part[0]).join('').toUpperCase()
}

function attachmentUrls(raw) {
  if (!raw) return []
  try {
    const parsed = JSON.parse(raw)
    const data = Array.isArray(parsed?.data) ? parsed.data : Array.isArray(parsed) ? parsed : []
    return data.map((item) => ({
      type: item?.mime_type || item?.type || 'file',
      url: item?.image_data?.url || item?.file_url || item?.video_data?.url || item?.url,
      name: item?.name || item?.type || 'Tệp đính kèm',
    })).filter((item) => item.url)
  } catch {
    return []
  }
}

function MessageBubble({ message }) {
  const attachments = attachmentUrls(message.attachmentsJson)
  return (
    <div className={`page-message-row${message.isFromPage ? ' is-page' : ''}`}>
      <div className="page-message-bubble">
        {message.text && <p>{message.text}</p>}
        {attachments.map((attachment) => (
          <a
            key={attachment.url}
            href={attachment.url}
            target="_blank"
            rel="noreferrer"
            className="page-message-attachment"
          >
            {attachment.type?.startsWith('image') ? (
              <img src={attachment.url} alt={attachment.name} />
            ) : (
              attachment.name
            )}
          </a>
        ))}
        <div className="page-message-time">
          {formatDateTime(message.sentAt)}
          {message.isFromPage && message.isRead ? ' · Đã xem' : ''}
          {message.isFromPage && !message.isRead && message.isDelivered ? ' · Đã nhận' : ''}
        </div>
      </div>
    </div>
  )
}

export default function PageMessagesInboxPage() {
  const permissions = usePermissions()
  const canManage = permissions.hasRole(['Admin', 'ContentManager', 'Reviewer'])
  const canSync = permissions.hasRole(['Admin', 'ContentManager'])
  const isAdmin = permissions.hasRole(['Admin'])
  const [channelId, setChannelId] = useState('')
  const [status, setStatus] = useState('')
  const [unreadOnly, setUnreadOnly] = useState(false)
  const [openWindowOnly, setOpenWindowOnly] = useState(false)
  const [keyword, setKeyword] = useState('')
  const [selectedId, setSelectedId] = useState(null)
  const [text, setText] = useState('')
  const [assignedTo, setAssignedTo] = useState('')
  const [note, setNote] = useState('')

  const params = useMemo(() => ({
    index: 1,
    size: 100,
    socialChannelId: channelId || undefined,
    inboxStatus: status ? Number(status) : undefined,
    unreadOnly: unreadOnly || undefined,
    openWindowOnly: openWindowOnly || undefined,
    keyword: keyword || undefined,
  }), [channelId, status, unreadOnly, openWindowOnly, keyword])

  const { data: channels = [] } = useSocialChannelAll()
  const facebookPages = channels.filter((channel) => channel.platform === 1 && channel.channelType === 1)
  const { data: summary } = usePageMessageSummary()
  const { data, isLoading, isError, error, refetch } = usePageMessageList(params)
  const { data: conversation, isLoading: detailLoading } = usePageConversation(selectedId)
  const syncMutation = useSyncPageMessages()
  const subscribeMutation = useSubscribePageMessages()
  const sendMutation = useSendPageMessage()
  const workflowMutation = usePageMessageWorkflow()

  const threadRef = useRef(null)
  const detailRef = useRef(null)
  const lastScrolledId = useRef(null)

  useEffect(() => {
    if (!conversation) return
    setAssignedTo(conversation.assignedTo || '')
    setNote(conversation.internalNote || '')
    requestAnimationFrame(() => {
      // Thread cuộn xuống tin mới nhất để ô trả lời luôn nằm ngay dưới.
      if (threadRef.current) threadRef.current.scrollTop = threadRef.current.scrollHeight
      // Lần đầu mở hội thoại thì đưa panel chi tiết vào giữa màn hình,
      // tránh trường hợp ô trả lời nằm dưới fold khiến người dùng tưởng không có.
      if (detailRef.current && lastScrolledId.current !== conversation.id) {
        lastScrolledId.current = conversation.id
        detailRef.current.scrollIntoView({ behavior: 'smooth', block: 'start' })
      }
    })
  }, [conversation])

  const items = data?.items ?? []

  const handleSync = async (full) => {
    try {
      const result = await syncMutation.mutateAsync({
        socialChannelId: channelId || null,
        full,
      })
      toast.success(
        `Đã đồng bộ ${result.conversationsUpserted} hội thoại, ${result.messagesUpserted} tin nhắn`
        + (result.errors?.length ? ` (${result.errors.length} lỗi)` : ''),
      )
    } catch (syncError) {
      toast.error(getErrorMessage(syncError))
    }
  }

  const handleSend = async () => {
    if (!selectedId || !text.trim()) return
    try {
      await sendMutation.mutateAsync({ id: selectedId, text: text.trim() })
      setText('')
      toast.success('Đã gửi tin nhắn')
    } catch (sendError) {
      toast.error(getErrorMessage(sendError))
    }
  }

  const handleSubscribe = async () => {
    try {
      await subscribeMutation.mutateAsync()
      toast.success('Đã đăng ký webhook Messenger cho các Facebook Page')
    } catch (subscribeError) {
      toast.error(getErrorMessage(subscribeError))
    }
  }

  const workflow = async (action, value, successMessage) => {
    if (!selectedId) return
    try {
      await workflowMutation.mutateAsync({ id: selectedId, action, value })
      toast.success(successMessage)
    } catch (workflowError) {
      toast.error(getErrorMessage(workflowError))
    }
  }

  return (
    <section className="page-messages-page">
      <PageHeader
        title="Tin nhắn Page"
        description="Hộp thư Messenger hợp nhất cho các Facebook Page đã kết nối"
        actions={(
          <div className="page-message-header-actions">
            <button type="button" className="btn btn-secondary btn-sm" onClick={() => refetch()}>
              Làm mới
            </button>
            {canSync && (
              <>
                <button
                  type="button"
                  className="btn btn-secondary btn-sm"
                  disabled={syncMutation.isPending}
                  onClick={() => handleSync(false)}
                >
                  Đồng bộ gần đây
                </button>
                <button
                  type="button"
                  className="btn btn-primary btn-sm"
                  disabled={syncMutation.isPending}
                  onClick={() => handleSync(true)}
                >
                  {syncMutation.isPending ? 'Đang đồng bộ...' : 'Đồng bộ đầy đủ'}
                </button>
              </>
            )}
            {isAdmin && (
              <button
                type="button"
                className="btn btn-secondary btn-sm"
                disabled={subscribeMutation.isPending}
                onClick={handleSubscribe}
              >
                Đăng ký webhook
              </button>
            )}
          </div>
        )}
      />

      <div className="page-message-summary">
        <span>Tổng: {summary?.total ?? '—'}</span>
        <span className="is-alert">Chưa đọc: {summary?.unread ?? '—'}</span>
        <span>Mới: {summary?.newCount ?? '—'}</span>
        <span>Đang xử lý: {summary?.inProgress ?? '—'}</span>
        <span>Còn hạn trả lời: {summary?.replyWindowOpen ?? '—'}</span>
      </div>

      <div className="card card-body page-message-filters">
        <div className="form-group">
          <label htmlFor="message-page">Facebook Page</label>
          <select id="message-page" value={channelId} onChange={(event) => setChannelId(event.target.value)}>
            <option value="">Tất cả Page</option>
            {facebookPages.map((channel) => (
              <option key={channel.id} value={channel.id}>{channel.pageName}</option>
            ))}
          </select>
        </div>
        <div className="form-group">
          <label htmlFor="message-status">Trạng thái</label>
          <select id="message-status" value={status} onChange={(event) => setStatus(event.target.value)}>
            <option value="">Tất cả</option>
            <option value="1">Mới</option>
            <option value="2">Đang xử lý</option>
            <option value="3">Đã trả lời</option>
            <option value="4">Bỏ qua</option>
          </select>
        </div>
        <div className="form-group">
          <label htmlFor="message-keyword">Tìm kiếm</label>
          <input
            id="message-keyword"
            value={keyword}
            onChange={(event) => setKeyword(event.target.value)}
            placeholder="Tên hoặc nội dung..."
          />
        </div>
        <label className="page-message-check">
          <input type="checkbox" checked={unreadOnly} onChange={(event) => setUnreadOnly(event.target.checked)} />
          Chỉ chưa đọc
        </label>
        <label className="page-message-check">
          <input
            type="checkbox"
            checked={openWindowOnly}
            onChange={(event) => setOpenWindowOnly(event.target.checked)}
          />
          Còn hạn 24 giờ
        </label>
      </div>

      <div className="page-message-layout">
        <div className="card page-message-list-panel">
          {isLoading && <LoadingState message="Đang tải hội thoại..." />}
          {isError && <ErrorState message={getErrorMessage(error)} onRetry={refetch} />}
          {!isLoading && !isError && items.length === 0 && (
            <EmptyState message="Chưa có hội thoại. Hãy cấp pages_messaging rồi bấm Đồng bộ đầy đủ." />
          )}
          {!isLoading && !isError && items.length > 0 && (
            <ul className="page-message-list">
              {items.map((item) => {
                const meta = statusMeta(item.inboxStatus)
                return (
                  <li key={item.id}>
                    <button
                      type="button"
                      className={`page-message-list-item${selectedId === item.id ? ' is-active' : ''}`}
                      onClick={() => {
                        setSelectedId(item.id)
                        setText('')
                      }}
                    >
                      <span className="page-message-avatar">
                        {item.participantAvatarUrl
                          ? <img src={item.participantAvatarUrl} alt="" />
                          : initials(item.participantName)}
                      </span>
                      <span className="page-message-list-content">
                        <span className="page-message-list-top">
                          <strong>{item.participantName || item.participantExternalId}</strong>
                          <StatusBadge label={meta.label} tone={meta.tone} />
                        </span>
                        <span className="page-message-snippet">{item.snippet || 'Tin nhắn'}</span>
                        <span className="page-message-list-meta">
                          {item.channelName || 'Facebook Page'} · {formatDateTime(item.lastMessageAt)}
                          {item.unreadCount > 0 ? ` · ${item.unreadCount} chưa đọc` : ''}
                        </span>
                      </span>
                    </button>
                  </li>
                )
              })}
            </ul>
          )}
        </div>

        <div className="card page-message-detail-panel" ref={detailRef}>
          {!selectedId && <EmptyState message="Chọn một hội thoại để xem và trả lời" />}
          {selectedId && detailLoading && <LoadingState message="Đang tải tin nhắn..." />}
          {selectedId && !detailLoading && conversation && (
            <>
              <div className="page-message-detail-header">
                <div className="page-message-person">
                  <span className="page-message-avatar is-large">
                    {conversation.participantAvatarUrl
                      ? <img src={conversation.participantAvatarUrl} alt="" />
                      : initials(conversation.participantName)}
                  </span>
                  <div>
                    <h2>{conversation.participantName || conversation.participantExternalId}</h2>
                    <p>{conversation.channelName} · {conversation.messageCount} tin nhắn</p>
                  </div>
                </div>
                <div className="page-message-window">
                  <StatusBadge
                    label={conversation.isReplyWindowOpen ? 'Còn hạn 24 giờ' : 'Đã hết hạn 24 giờ'}
                    tone={conversation.isReplyWindowOpen ? 'success' : 'danger'}
                  />
                  {conversation.replyWindowClosesAt && (
                    <small>Đóng lúc {formatDateTime(conversation.replyWindowClosesAt)}</small>
                  )}
                </div>
              </div>

              <div className="page-message-thread" ref={threadRef}>
                {conversation.messages?.length > 0
                  ? conversation.messages.map((message) => <MessageBubble key={message.id} message={message} />)
                  : <EmptyState message="Hội thoại chưa có nội dung được đồng bộ" />}
              </div>

              {canManage && (
                <div className="page-message-composer">
                  {!conversation.isReplyWindowOpen && (
                    <div className="alert alert-warning">
                      Cửa sổ 24 giờ đã đóng. Hệ thống khóa gửi để tránh vi phạm chính sách Meta.
                    </div>
                  )}
                  <textarea
                    rows={3}
                    value={text}
                    onChange={(event) => setText(event.target.value)}
                    placeholder="Nhập tin nhắn trả lời..."
                    disabled={!conversation.isReplyWindowOpen}
                  />
                  <div className="page-message-composer-actions">
                    <span>{text.length} ký tự</span>
                    <button
                      type="button"
                      className="btn btn-primary"
                      disabled={!conversation.isReplyWindowOpen || !text.trim() || sendMutation.isPending}
                      onClick={handleSend}
                    >
                      {sendMutation.isPending ? 'Đang gửi...' : 'Gửi tin nhắn'}
                    </button>
                  </div>
                </div>
              )}

              {canManage && (
                <div className="page-message-workflow">
                  <div className="page-message-workflow-actions">
                    <button type="button" className="btn btn-ghost btn-sm" onClick={() => workflow('status', 2, 'Đã chuyển sang đang xử lý')}>
                      Đang xử lý
                    </button>
                    <button type="button" className="btn btn-ghost btn-sm" onClick={() => workflow('status', 4, 'Đã bỏ qua')}>
                      Bỏ qua
                    </button>
                  </div>
                  <div className="form-group">
                    <label htmlFor="message-assignee">Gán người xử lý</label>
                    <div className="page-message-inline-form">
                      <input
                        id="message-assignee"
                        value={assignedTo}
                        onChange={(event) => setAssignedTo(event.target.value)}
                        placeholder="Tên hoặc email..."
                      />
                      <button type="button" className="btn btn-secondary btn-sm" onClick={() => workflow('assign', assignedTo, 'Đã gán người xử lý')}>
                        Gán
                      </button>
                    </div>
                  </div>
                  <div className="form-group">
                    <label htmlFor="message-note">Ghi chú nội bộ</label>
                    <textarea id="message-note" rows={2} value={note} onChange={(event) => setNote(event.target.value)} />
                    <button type="button" className="btn btn-secondary btn-sm" onClick={() => workflow('note', note, 'Đã lưu ghi chú')}>
                      Lưu ghi chú
                    </button>
                  </div>
                </div>
              )}
            </>
          )}
        </div>
      </div>
    </section>
  )
}
