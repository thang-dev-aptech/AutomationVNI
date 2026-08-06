import { useEffect, useRef, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { formatDateTime } from '@/shared/utils/apiHelpers'
import { useMarkAllRead, useNotificationFeed } from '../hooks/useNotifications'
import './NotificationBell.css'

const KIND_ICON = {
  1: '🔄', // CrawlStarted
  2: '📥', // CrawlFinished
  3: '✅', // ArticleApproved
  4: '🗑', // ArticleRejected
  5: '🚀', // PostPublished
  6: '⚠️', // PostFailed
}

// Nguồn là thứ quan trọng nhất trên mỗi dòng: cả tính năng sinh ra để biết AI ĐÃ LÀM, khỏi
// làm lại. "Đã cào xong" mà không nói ai cào thì vô dụng đúng ở tình huống nó phải giải quyết.
const SOURCE_LABEL = { 1: 'Tự động', 2: 'Web', 3: 'Telegram' }
const SOURCE_CLASS = { 1: 'sys', 2: 'web', 3: 'tele' }

export default function NotificationBell() {
  const [isOpen, setOpen] = useState(false)
  const wrapRef = useRef(null)
  const navigate = useNavigate()
  const { data } = useNotificationFeed()
  const markAllRead = useMarkAllRead()

  const items = data?.items ?? []
  const unread = data?.unread ?? 0

  // Bấm ra ngoài hoặc Esc thì đóng. Không có cái này thì panel dính lại khi chuyển trang.
  useEffect(() => {
    if (!isOpen) return undefined
    const onClick = (e) => {
      if (wrapRef.current && !wrapRef.current.contains(e.target)) setOpen(false)
    }
    const onKey = (e) => { if (e.key === 'Escape') setOpen(false) }
    document.addEventListener('mousedown', onClick)
    document.addEventListener('keydown', onKey)
    return () => {
      document.removeEventListener('mousedown', onClick)
      document.removeEventListener('keydown', onKey)
    }
  }, [isOpen])

  const handleOpen = () => {
    const next = !isOpen
    setOpen(next)
    // Đánh dấu đã đọc khi MỞ, không phải khi đóng: mở ra là đã nhìn thấy rồi.
    if (next && unread > 0) markAllRead.mutate()
  }

  const goTo = (item) => {
    setOpen(false)
    if (item.linkUrl) navigate(item.linkUrl)
  }

  return (
    <div className="notif" ref={wrapRef}>
      <button
        type="button"
        className="notif-btn"
        onClick={handleOpen}
        aria-label={unread > 0 ? `${unread} thông báo chưa đọc` : 'Thông báo'}
        aria-expanded={isOpen}
      >
        <svg xmlns="http://www.w3.org/2000/svg" width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
          <path d="M18 8A6 6 0 0 0 6 8c0 7-3 9-3 9h18s-3-2-3-9" />
          <path d="M13.73 21a2 2 0 0 1-3.46 0" />
        </svg>
        {unread > 0 && <span className="notif-badge">{unread > 99 ? '99+' : unread}</span>}
      </button>

      {isOpen && (
        <div className="notif-panel" role="dialog" aria-label="Thông báo">
          <div className="notif-panel-head">Hoạt động gần đây</div>
          {items.length === 0 && <div className="notif-empty">Chưa có hoạt động nào.</div>}
          <ul className="notif-list">
            {items.map((item) => (
              <li key={item.id}>
                <button
                  type="button"
                  className={`notif-item${item.isRead ? '' : ' notif-item--unread'}`}
                  onClick={() => goTo(item)}
                >
                  <span className="notif-item-icon" aria-hidden="true">{KIND_ICON[item.kind] ?? '•'}</span>
                  <span className="notif-item-body">
                    <span className="notif-item-title">{item.title}</span>
                    {item.message && <span className="notif-item-msg">{item.message}</span>}
                    <span className="notif-item-meta">
                      <span className={`notif-tag notif-tag--${SOURCE_CLASS[item.source] ?? 'sys'}`}>
                        {SOURCE_LABEL[item.source] ?? item.actor}
                      </span>
                      {formatDateTime(item.createdAt)}
                    </span>
                  </span>
                </button>
              </li>
            ))}
          </ul>
        </div>
      )}
    </div>
  )
}
