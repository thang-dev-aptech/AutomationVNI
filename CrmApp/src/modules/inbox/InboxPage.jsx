import React, { useState } from 'react'
import { useAuth } from '../../auth/useAuth'
import Icon from '../../shared/components/Icon'
import Badge from '../../shared/components/Badge'
import Button from '../../shared/components/Button'
import './InboxPage.css'

const initialConversations = [
  {
    id: 'c1',
    name: 'Nguyễn Văn An',
    channel: 'Facebook',
    preview: 'Chào shop, em muốn hỏi thông tin khóa học kế toán thực hành...',
    status: 'Mới',
    time: '10:15',
    messages: [
      { id: 'm1', sender: 'customer', text: 'Chào shop, em muốn hỏi thông tin khóa học kế toán thực hành...', time: '10:15' },
    ],
  },
  {
    id: 'c2',
    name: 'Trần Thị Bích',
    channel: 'Zalo',
    preview: 'Mình đã thanh toán học phí qua ngân hàng, check giúp mình nhé.',
    status: 'Đang xử lý',
    time: '09:40',
    messages: [
      { id: 'm2', sender: 'customer', text: 'Mình đã thanh toán học phí qua ngân hàng, check giúp mình nhé.', time: '09:40' },
      { id: 'm3', sender: 'agent', text: 'Dạ shop đã nhận được giao dịch của chị ạ!', time: '09:42' },
    ],
  },
  {
    id: 'c3',
    name: 'Lê Hoàng Long',
    channel: 'Website',
    preview: 'Tư vấn giúp mình lịch khai giảng tháng tới tại cơ sở Hà Nội.',
    status: 'Chờ phản hồi',
    time: 'Hôm qua',
    messages: [
      { id: 'm4', sender: 'customer', text: 'Tư vấn giúp mình lịch khai giảng tháng tới tại cơ sở Hà Nội.', time: '15:20' },
    ],
  },
]

export const InboxPage = () => {
  const { canCare, isReadOnly } = useAuth()
  const [conversations, setConversations] = useState(initialConversations)
  const [selectedId, setSelectedId] = useState('c1')
  const [selectedChannel, setSelectedChannel] = useState('all')
  const [replyText, setReplyText] = useState('')
  const [noteOpen, setNoteOpen] = useState(false)
  const [noteText, setNoteText] = useState('')

  const activeConv = conversations.find((c) => c.id === selectedId) || conversations[0]

  const filteredConversations = conversations.filter((c) => {
    if (selectedChannel === 'all') return true
    return c.channel.toLowerCase() === selectedChannel.toLowerCase()
  })

  const handleSendReply = (e) => {
    e.preventDefault()
    if (!replyText.trim() || isReadOnly) return

    const newMsg = {
      id: `m_${Date.now()}`,
      sender: 'agent',
      text: replyText.trim(),
      time: 'Vừa xong',
    }

    setConversations((prev) =>
      prev.map((c) =>
        c.id === selectedId
          ? { ...c, messages: [...c.messages, newMsg], preview: newMsg.text, status: 'Đã trả lời' }
          : c,
      ),
    )
    setReplyText('')
  }

  const handleAddNote = () => {
    if (!noteText.trim()) return
    alert(`Đã lưu ghi chú: "${noteText}"`)
    setNoteText('')
    setNoteOpen(false)
  }

  return (
    <div className="crm-inbox-grid">
      {/* Left Column: Conversation list */}
      <div className="crm-inbox-sidebar">
        <div className="crm-inbox-search">
          <div className="crm-channel-filters">
            {['all', 'Facebook', 'Zalo', 'Website'].map((ch) => (
              <button
                key={ch}
                type="button"
                className={`crm-filter-chip ${selectedChannel === ch ? 'active' : ''}`}
                onClick={() => setSelectedChannel(ch)}
              >
                {ch === 'all' ? 'Tất cả kênh' : ch}
              </button>
            ))}
          </div>
        </div>

        <div className="crm-conv-list" data-testid="conversation-list">
          {filteredConversations.map((conv) => (
            <div
              key={conv.id}
              className={`crm-conv-item ${conv.id === activeConv?.id ? 'active' : ''}`}
              onClick={() => setSelectedId(conv.id)}
              data-testid={`conv-item-${conv.id}`}
            >
              <div className="crm-conv-avatar">{conv.name[0]}</div>
              <div className="crm-conv-body">
                <div className="crm-conv-header">
                  <span className="crm-conv-name">{conv.name}</span>
                  <span className="crm-conv-time">{conv.time}</span>
                </div>
                <div className="crm-conv-preview">{conv.preview}</div>
                <div style={{ marginTop: '6px' }}>
                  <Badge variant="primary" size="sm">
                    {conv.channel}
                  </Badge>{' '}
                  <Badge variant={conv.status === 'Mới' ? 'danger' : 'default'} size="sm">
                    {conv.status}
                  </Badge>
                </div>
              </div>
            </div>
          ))}
        </div>
      </div>

      {/* Right Column: Chat Thread */}
      <div className="crm-chat-pane">
        {activeConv && (
          <>
            <div className="crm-chat-header">
              <div>
                <h3 style={{ margin: 0, fontSize: '16px', fontWeight: '700' }}>{activeConv.name}</h3>
                <span style={{ fontSize: '13px', color: 'var(--crm-text-muted)' }}>
                  Kênh: {activeConv.channel} • Trạng thái: {activeConv.status}
                </span>
              </div>

              {/* Care Actions: Visible for Reviewer & Manager, Hidden for Viewer */}
              {canCare && (
                <div className="crm-chat-actions" data-testid="care-actions">
                  <Button
                    variant="secondary"
                    size="sm"
                    onClick={() => setNoteOpen(!noteOpen)}
                    data-testid="btn-add-note"
                  >
                    Ghi chú
                  </Button>
                  <Button
                    variant="outline"
                    size="sm"
                    onClick={() => alert('Đã tạo việc nhắc xử lý')}
                    data-testid="btn-create-reminder"
                  >
                    Nhắc việc
                  </Button>
                  <Button
                    variant="secondary"
                    size="sm"
                    onClick={() => alert('Đã cập nhật trạng thái')}
                    data-testid="btn-change-status"
                  >
                    Đổi trạng thái
                  </Button>
                </div>
              )}
            </div>

            {/* Note Panel */}
            {noteOpen && canCare && (
              <div style={{ padding: '12px 20px', background: 'var(--crm-warning-light)', borderBottom: '1px solid var(--crm-border)' }}>
                <div style={{ display: 'flex', gap: '8px' }}>
                  <input
                    type="text"
                    className="crm-chat-input"
                    placeholder="Nhập ghi chú nội bộ khách hàng..."
                    value={noteText}
                    onChange={(e) => setNoteText(e.target.value)}
                  />
                  <Button variant="primary" size="sm" onClick={handleAddNote}>
                    Lưu
                  </Button>
                </div>
              </div>
            )}

            {/* Messages */}
            <div className="crm-chat-messages" data-testid="chat-messages">
              {activeConv.messages.map((m) => (
                <div
                  key={m.id}
                  className={`crm-msg-bubble ${m.sender === 'agent' ? 'crm-msg-outgoing' : 'crm-msg-incoming'}`}
                >
                  <p style={{ margin: 0 }}>{m.text}</p>
                  <span style={{ display: 'block', fontSize: '11px', marginTop: '4px', opacity: 0.8, textAlign: 'right' }}>
                    {m.time}
                  </span>
                </div>
              ))}
            </div>

            {/* Footer / Reply Area */}
            <div className="crm-chat-footer">
              {canCare ? (
                <form onSubmit={handleSendReply} className="crm-chat-input-box" data-testid="reply-form">
                  <input
                    type="text"
                    className="crm-chat-input"
                    placeholder="Nhập nội dung phản hồi khách hàng..."
                    value={replyText}
                    onChange={(e) => setReplyText(e.target.value)}
                    data-testid="reply-input"
                  />
                  <Button
                    type="submit"
                    variant="primary"
                    size="md"
                    icon={<Icon name="send" size={16} />}
                    data-testid="btn-send-reply"
                  >
                    Trả lời
                  </Button>
                </form>
              ) : (
                <div className="crm-readonly-notice" data-testid="viewer-readonly-notice">
                  🔒 Chế độ Chỉ đọc (Viewer) — Thao tác trả lời và chỉnh sửa bị vô hiệu hoá
                </div>
              )}
            </div>
          </>
        )}
      </div>
    </div>
  )
}

export default InboxPage
