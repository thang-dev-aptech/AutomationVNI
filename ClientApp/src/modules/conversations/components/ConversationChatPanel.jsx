import './ConversationChatPanel.css'

export default function ConversationChatPanel({
  kind,
  id,
  conversation,
  onBack,
  onToggleCustomerInfo,
  isCustomerInfoOpen,
}) {
  if (!id) {
    return (
      <div className="conversation-chat-panel conversation-chat-panel--empty" data-testid="chat-panel-empty">
        <svg xmlns="http://www.w3.org/2000/svg" width="48" height="48" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round" strokeLinejoin="round" className="empty-chat-icon">
          <path d="M21 15a2 2 0 0 1-2 2H7l-4 4V5a2 2 0 0 1 2-2h14a2 2 0 0 1 2 2z" />
        </svg>
        <p>Chọn một hội thoại để bắt đầu xem chi tiết và trả lời</p>
      </div>
    )
  }

  const name = conversation?.participantName || 'Khách hàng'
  const channel = conversation?.channelName || 'Kênh'
  const isMessage = (kind || conversation?.kind) === 'message'

  return (
    <div className="conversation-chat-panel" data-testid="conversation-chat-panel">
      {/* Header */}
      <header className="chat-header">
        <div className="chat-header-left">
          {/* Nút quay lại trên màn hẹp */}
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

          <div className="chat-header-info">
            <h2 className="chat-header-name">{name}</h2>
            <span className="chat-header-channel">{channel}</span>
          </div>
        </div>

        <div className="chat-header-actions">
          {conversation?.permalinkUrl && (
            <a
              href={conversation.permalinkUrl}
              target="_blank"
              rel="noopener noreferrer"
              className="chat-header-link"
              title="Mở bài viết gốc"
            >
              Bài viết gốc ↗
            </a>
          )}

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

      {/* Messages slot / stub */}
      <div className="chat-messages-area" data-testid="chat-messages-stub">
        <div className="chat-stub-placeholder">
          <span>Khung tin nhắn / bình luận (t4 slot)</span>
          <small>{isMessage ? 'Hội thoại tin nhắn' : 'Luồng bình luận'}: {id}</small>
        </div>
      </div>

      {/* Composer slot / stub */}
      <footer className="chat-composer-area" data-testid="chat-composer-stub">
        <div className="chat-composer-tools">
          <button
            type="button"
            className="composer-ai-btn"
            data-testid="ai-suggest-btn"
            title="AI gợi ý trả lời"
          >
            ✨ AI gợi ý
          </button>
        </div>
        <div className="chat-composer-input-row">
          <textarea
            className="chat-composer-input"
            rows="2"
            placeholder="Nhập nội dung trả lời..."
            readOnly
          />
          <button type="button" className="chat-send-btn" disabled>
            Gửi
          </button>
        </div>
      </footer>
    </div>
  )
}
