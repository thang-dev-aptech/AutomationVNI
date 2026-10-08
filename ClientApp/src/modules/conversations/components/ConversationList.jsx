import './ConversationList.css'

function formatActivityTime(dateStr) {
  if (!dateStr) return ''
  const d = new Date(dateStr)
  if (Number.isNaN(d.getTime())) return ''

  const now = new Date()
  const isSameDay =
    d.getDate() === now.getDate() &&
    d.getMonth() === now.getMonth() &&
    d.getFullYear() === now.getFullYear()

  if (isSameDay) {
    return d.toLocaleTimeString('vi-VN', { hour: '2-digit', minute: '2-digit' })
  }

  const yesterday = new Date(now)
  yesterday.setDate(now.getDate() - 1)
  const isYesterday =
    d.getDate() === yesterday.getDate() &&
    d.getMonth() === yesterday.getMonth() &&
    d.getFullYear() === yesterday.getFullYear()

  if (isYesterday) {
    return 'Hôm qua'
  }

  return d.toLocaleDateString('vi-VN', { day: '2-digit', month: '2-digit' })
}

function getInitials(name) {
  if (!name) return 'KH'
  const parts = name.trim().split(/\s+/)
  if (parts.length === 1) return parts[0].slice(0, 2).toUpperCase()
  return (parts[0][0] + parts[parts.length - 1][0]).toUpperCase()
}

function PlatformBadge({ platform }) {
  // SocialPlatform: 1: Facebook, 2: Instagram, 3: TikTok, 4: YouTube, 5: Threads
  const isFacebook = platform === 1 || platform === 'Facebook'
  const isThreads = platform === 5 || platform === 'Threads'

  let label = 'Facebook'
  let bg = '#1877f2'
  let char = 'f'

  if (isThreads) {
    label = 'Threads'
    bg = '#000000'
    char = '@'
  }

  return (
    <span
      className="conversation-platform-badge"
      data-testid="platform-badge"
      title={`Nền tảng: ${label}`}
      style={{ backgroundColor: bg }}
    >
      {char}
    </span>
  )
}

function KindIcon({ kind }) {
  const isMessage = kind === 'message'
  return (
    <span
      className="conversation-kind-badge"
      data-testid="kind-icon"
      title={isMessage ? 'Tin nhắn' : 'Bình luận'}
    >
      {isMessage ? (
        <svg xmlns="http://www.w3.org/2000/svg" width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.5" strokeLinecap="round" strokeLinejoin="round">
          <path d="M21 15a2 2 0 0 1-2 2H7l-4 4V5a2 2 0 0 1 2-2h14a2 2 0 0 1 2 2z" />
        </svg>
      ) : (
        <svg xmlns="http://www.w3.org/2000/svg" width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.5" strokeLinecap="round" strokeLinejoin="round">
          <path d="M7.9 20A9 9 0 1 0 4 16.1L2 22Z" />
        </svg>
      )}
    </span>
  )
}

export default function ConversationList({
  items = [],
  total = 0,
  page = 1,
  size = 20,
  isLoading = false,
  selectedId = null,
  onSelectConversation,
  onPageChange,
}) {
  const totalPages = Math.ceil(total / size) || 1

  if (isLoading && items.length === 0) {
    return (
      <div className="conversation-list-loading" data-testid="conversation-list-loading">
        <div className="conversation-list-spinner" />
        <span>Đang tải danh sách...</span>
      </div>
    )
  }

  if (!isLoading && items.length === 0) {
    return (
      <div className="conversation-list-empty" data-testid="conversation-list-empty">
        <svg xmlns="http://www.w3.org/2000/svg" width="40" height="40" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round" strokeLinejoin="round" className="empty-icon">
          <path d="M21 15a2 2 0 0 1-2 2H7l-4 4V5a2 2 0 0 1 2-2h14a2 2 0 0 1 2 2z" />
        </svg>
        <p>Không có hội thoại nào</p>
      </div>
    )
  }

  return (
    <div className="conversation-list-container" data-testid="conversation-list">
      <div className="conversation-items-scroll">
        {items.map((item) => {
          const isSelected = item.id === selectedId
          const hasUnread = item.unreadCount > 0

          return (
            <div
              key={`${item.kind}-${item.id}`}
              className={`conversation-item${isSelected ? ' is-selected' : ''}${hasUnread ? ' is-unread' : ''}`}
              onClick={() => onSelectConversation(item.id, item.kind, item)}
              role="button"
              tabIndex={0}
              onKeyDown={(e) => {
                if (e.key === 'Enter' || e.key === ' ') {
                  onSelectConversation(item.id, item.kind, item)
                }
              }}
              data-testid={`conversation-item-${item.id}`}
            >
              {/* Avatar + Badge Nền tảng + Icon Loại */}
              <div className="conversation-avatar-wrap">
                {item.participantAvatarUrl ? (
                  <img
                    src={item.participantAvatarUrl}
                    alt={item.participantName || ''}
                    className="conversation-avatar"
                  />
                ) : (
                  <div className="conversation-avatar conversation-avatar--placeholder">
                    {getInitials(item.participantName)}
                  </div>
                )}
                <PlatformBadge platform={item.platform} />
                <KindIcon kind={item.kind} />
              </div>

              {/* Thông tin chính */}
              <div className="conversation-body">
                <div className="conversation-header-row">
                  <span className="conversation-name" title={item.participantName || 'Khách hàng'}>
                    {item.participantName || 'Khách hàng'}
                  </span>
                  <span className="conversation-time">
                    {formatActivityTime(item.lastActivityAt)}
                  </span>
                </div>

                <div className="conversation-snippet" title={item.snippet || ''}>
                  {item.snippet || '(Không có nội dung)'}
                </div>

                <div className="conversation-footer-row">
                  <span className="conversation-page" title={item.channelName || ''}>
                    {item.channelName || 'Kênh'}
                  </span>
                  {item.assignedTo && (
                    <span className="conversation-assignee" title={`Phụ trách: ${item.assignedTo}`}>
                      {item.assignedTo}
                    </span>
                  )}
                  {hasUnread && (
                    <span
                      className="conversation-unread-dot"
                      data-testid="unread-dot"
                      title="Chưa đọc"
                    />
                  )}
                </div>
              </div>
            </div>
          )
        })}
      </div>

      {/* Phân trang */}
      {total > size && (
        <div className="conversation-pagination" data-testid="conversation-pagination">
          <button
            type="button"
            className="pagination-btn"
            disabled={page <= 1}
            onClick={() => onPageChange(page - 1)}
            aria-label="Trang trước"
          >
            ‹
          </button>
          <span className="pagination-info">
            Trang {page} / {totalPages} ({total})
          </span>
          <button
            type="button"
            className="pagination-btn"
            disabled={page >= totalPages}
            onClick={() => onPageChange(page + 1)}
            aria-label="Trang sau"
          >
            ›
          </button>
        </div>
      )}
    </div>
  )
}
