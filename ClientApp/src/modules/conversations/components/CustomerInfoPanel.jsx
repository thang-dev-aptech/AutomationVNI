import { useEffect, useMemo, useState } from 'react'
import { useQueryClient } from '@tanstack/react-query'
import LoadingState from '@/shared/components/LoadingState'
import ErrorState from '@/shared/components/ErrorState'
import EmptyState from '@/shared/components/EmptyState'
import StatusBadge from '@/shared/components/StatusBadge'
import { formatDateTime, getErrorMessage } from '@/shared/utils/apiHelpers'
import { toast } from '@/shared/stores/toastStore'
import { usePermissions } from '@/shared/hooks/usePermissions'
import { pageMessageApi } from '@/modules/messages/services/pageMessageApi'
import { commentApi } from '@/modules/comments/services/commentApi'
import { useConversationParams } from '../hooks/useConversationParams'
import { useInboxProfile } from '../hooks/useInbox'
import { inboxQueryKeys } from '../services/inboxApi'
import './CustomerInfoPanel.css'

const STORAGE_KEY = 'crm_customer_info_panel_open'

const STATUS_MAP = {
  1: { label: 'Mới', tone: 'info' },
  2: { label: 'Đang xử lý', tone: 'warning' },
  3: { label: 'Đã trả lời', tone: 'success' },
  4: { label: 'Bỏ qua', tone: 'neutral' },
}

function getStatusMeta(value) {
  return STATUS_MAP[value] || { label: `Trạng thái ${value}`, tone: 'neutral' }
}

function getPlatformLabel(platform) {
  const p = Number(platform)
  if (p === 1) return 'Facebook'
  if (p === 2) return 'Instagram'
  if (p === 3) return 'TikTok'
  if (p === 4) return 'Youtube'
  if (p === 5) return 'Threads'
  if (p === 6) return 'Zalo'
  return 'Mạng xã hội'
}

function getActionLabel(actionType) {
  const norm = String(actionType || '').toLowerCase()
  if (norm === 'status' || norm.includes('trạng thái')) return 'Đổi trạng thái'
  if (norm === 'assign' || norm.includes('gán') || norm.includes('giao')) return 'Gán người xử lý'
  if (norm === 'note' || norm.includes('ghi chú')) return 'Ghi chú nội bộ'
  if (norm === 'send' || norm.includes('gửi')) return 'Gửi tin nhắn'
  if (norm === 'reply' || norm.includes('trả lời')) return 'Trả lời bình luận'
  if (norm === 'hide' || norm.includes('ẩn')) return 'Ẩn bình luận'
  if (norm === 'unhide' || norm.includes('hiện')) return 'Hiện bình luận'
  if (norm === 'delete' || norm === 'remove' || norm.includes('xóa')) return 'Xóa bình luận'
  if (norm === 'pending' || norm.includes('chờ duyệt')) return 'Duyệt chờ'
  return actionType || 'Thao tác'
}

function getInitials(name) {
  const val = String(name || '?').trim()
  return val
    .split(/\s+/)
    .slice(0, 2)
    .map((s) => s[0])
    .join('')
    .toUpperCase()
}

export default function CustomerInfoPanel({
  kind,
  id,
  conversation,
  onClose,
  onSelectConversation,
}) {
  const queryClient = useQueryClient()
  const permissions = usePermissions()
  const canEdit = permissions.hasRole(['Admin', 'ContentManager', 'Reviewer'])
  const { setSelectedConversation } = useConversationParams()

  const normalizedKind = useMemo(() => {
    const raw = String(kind || conversation?.kind || 'message').toLowerCase()
    return raw === 'comment' || raw === '2' ? 'comment' : 'message'
  }, [kind, conversation])

  // Fetch profile via api/Inbox/{kind}/{id}/profile
  const {
    data: profile,
    isLoading,
    isError,
    error,
    refetch,
  } = useInboxProfile(normalizedKind, id)

  // Local state for editing assignee and internal note
  const [assignedTo, setAssignedTo] = useState('')
  const [internalNote, setInternalNote] = useState('')
  const [isSavingAssign, setIsSavingAssign] = useState(false)
  const [isSavingNote, setIsSavingNote] = useState(false)

  // Lightbox preview for media
  const [lightboxUrl, setLightboxUrl] = useState(null)

  useEffect(() => {
    if (profile) {
      setAssignedTo(profile.assignedTo || '')
      setInternalNote(profile.internalNote || '')
    }
  }, [profile])

  const handleClose = () => {
    try {
      localStorage.setItem(STORAGE_KEY, JSON.stringify(false))
    } catch {
      // Ignore localStorage exceptions
    }
    if (onClose) onClose()
  }

  // Save assignee
  const handleSaveAssignee = async () => {
    if (!id) return
    setIsSavingAssign(true)
    try {
      if (normalizedKind === 'message') {
        await pageMessageApi.assign(id, assignedTo)
      } else {
        await commentApi.assign(id, assignedTo)
      }
      await queryClient.invalidateQueries({
        queryKey: inboxQueryKeys.profile(normalizedKind, id),
      })
      toast.success('Đã cập nhật người xử lý')
    } catch (err) {
      toast.error(getErrorMessage(err) || 'Không thể lưu người xử lý')
    } finally {
      setIsSavingAssign(false)
    }
  }

  // Save internal note
  const handleSaveNote = async () => {
    if (!id) return
    setIsSavingNote(true)
    try {
      if (normalizedKind === 'message') {
        await pageMessageApi.note(id, internalNote)
      } else {
        await commentApi.note(id, internalNote)
      }
      await queryClient.invalidateQueries({
        queryKey: inboxQueryKeys.profile(normalizedKind, id),
      })
      toast.success('Đã lưu ghi chú nội bộ')
    } catch (err) {
      toast.error(getErrorMessage(err) || 'Không thể lưu ghi chú')
    } finally {
      setIsSavingNote(false)
    }
  }

  // Open related conversation on same page
  const handleOpenRelated = (item) => {
    const itemKind =
      String(item.kind) === '1' || String(item.kind).toLowerCase() === 'message'
        ? 'message'
        : 'comment'
    if (onSelectConversation) {
      onSelectConversation(item.id, itemKind)
    } else {
      setSelectedConversation(item.id, itemKind)
    }
  }

  if (!id) {
    return (
      <div className="customer-info-panel customer-info-panel--empty" data-testid="customer-info-panel">
        <EmptyState message="Chưa chọn hội thoại để xem thông tin khách." />
      </div>
    )
  }

  const name =
    profile?.participantName ||
    profile?.participantExternalId ||
    conversation?.participantName ||
    'Khách hàng'
  const isMsg = normalizedKind === 'message'
  const statusInfo = getStatusMeta(profile?.inboxStatus ?? conversation?.inboxStatus ?? 1)
  const stats = profile?.stats || {}
  const mediaList = profile?.media || []
  const activitiesList = profile?.activities || []
  const otherList = profile?.otherConversations || []

  return (
    <aside className="customer-info-panel" data-testid="customer-info-panel">
      {/* Panel Header */}
      <header className="info-header">
        <h3 className="info-title">Thông tin khách hàng</h3>
        <button
          type="button"
          className="info-close-btn"
          onClick={handleClose}
          aria-label="Đóng thông tin khách hàng"
          data-testid="close-info-btn"
        >
          ×
        </button>
      </header>

      {/* Loading & Error States */}
      {isLoading && (
        <div className="info-loading-wrap" data-testid="info-loading">
          <LoadingState message="Đang tải thông tin khách hàng..." />
        </div>
      )}

      {isError && (
        <div className="info-error-wrap" data-testid="info-error">
          <ErrorState message={getErrorMessage(error)} onRetry={refetch} />
        </div>
      )}

      {!isLoading && !isError && profile && (
        <div className="info-scroll-body" data-testid="info-scroll-body">
          {/* 1. Thông tin khách: avatar, tên, page, loại, trạng thái */}
          <section className="info-card info-customer-card" data-testid="info-section-customer">
            <div className="info-avatar-row">
              <div className="info-avatar">
                {profile.participantAvatarUrl ? (
                  <img src={profile.participantAvatarUrl} alt={name} />
                ) : (
                  <span>{getInitials(name)}</span>
                )}
              </div>
              <div className="info-customer-details">
                <h4 className="info-customer-name" data-testid="customer-name">
                  {name}
                </h4>
                <div className="info-customer-meta">
                  <span className="info-platform-chip">
                    {getPlatformLabel(profile.platform)}
                  </span>
                  <span className="info-channel-name">{profile.channelName || 'Kênh'}</span>
                </div>
                <div className="info-type-row">
                  <span className="info-kind-badge">
                    {isMsg ? 'Tin nhắn' : 'Bình luận'}
                  </span>
                  <StatusBadge label={statusInfo.label} tone={statusInfo.tone} />
                </div>
              </div>
            </div>
          </section>

          {/* 2. Nhân viên phụ trách + Ghi chú nội bộ */}
          <section className="info-card" data-testid="info-section-workflow">
            <h5 className="info-section-title">Phụ trách & Ghi chú</h5>

            {/* Nhân viên phụ trách */}
            <div className="info-field-group">
              <label htmlFor="info-assignee-input" className="info-field-label">
                Nhân viên phụ trách
              </label>
              {canEdit ? (
                <div className="info-inline-edit">
                  <input
                    id="info-assignee-input"
                    type="text"
                    className="info-input"
                    value={assignedTo}
                    onChange={(e) => setAssignedTo(e.target.value)}
                    placeholder="Tên hoặc email..."
                    data-testid="info-assignee-input"
                  />
                  <button
                    type="button"
                    className="btn btn-secondary btn-sm"
                    onClick={handleSaveAssignee}
                    disabled={isSavingAssign}
                    data-testid="info-assign-save-btn"
                  >
                    {isSavingAssign ? 'Đang lưu...' : 'Gán'}
                  </button>
                </div>
              ) : (
                <p className="info-readonly-text" data-testid="info-readonly-assignee">
                  {profile.assignedTo || 'Chưa gán người xử lý'}
                </p>
              )}
            </div>

            {/* Ghi chú nội bộ */}
            <div className="info-field-group">
              <label htmlFor="info-note-input" className="info-field-label">
                Ghi chú nội bộ
              </label>
              {canEdit ? (
                <div className="info-note-edit">
                  <textarea
                    id="info-note-input"
                    rows={2}
                    className="info-textarea"
                    value={internalNote}
                    onChange={(e) => setInternalNote(e.target.value)}
                    placeholder="Ghi chú thêm về khách..."
                    data-testid="info-note-input"
                  />
                  <button
                    type="button"
                    className="btn btn-secondary btn-sm"
                    onClick={handleSaveNote}
                    disabled={isSavingNote}
                    data-testid="info-note-save-btn"
                  >
                    {isSavingNote ? 'Đang lưu...' : 'Lưu ghi chú'}
                  </button>
                </div>
              ) : (
                <p className="info-readonly-text" data-testid="info-readonly-note">
                  {profile.internalNote || 'Chưa có ghi chú'}
                </p>
              )}
            </div>
          </section>

          {/* 3. Thống kê tương tác & Cửa sổ 24h */}
          <section className="info-card" data-testid="info-section-stats">
            <h5 className="info-section-title">Thống kê tương tác</h5>
            <div className="info-stats-grid">
              <div className="info-stat-item">
                <span className="info-stat-num">{stats.customerCount ?? 0}</span>
                <span className="info-stat-lbl">Khách gửi</span>
              </div>
              <div className="info-stat-item">
                <span className="info-stat-num">{stats.pageCount ?? 0}</span>
                <span className="info-stat-lbl">Page gửi</span>
              </div>
            </div>

            <div className="info-dates-list">
              <div className="info-date-row">
                <span>Liên hệ đầu:</span>
                <strong>{stats.firstAt ? formatDateTime(stats.firstAt) : '—'}</strong>
              </div>
              <div className="info-date-row">
                <span>Hoạt động cuối:</span>
                <strong>{stats.lastAt ? formatDateTime(stats.lastAt) : '—'}</strong>
              </div>
            </div>

            {/* Cửa sổ 24h đối với tin nhắn */}
            {isMsg && (
              <div className="info-window-block" data-testid="info-window-block">
                <div className="info-window-header">
                  <span>Cửa sổ 24 giờ:</span>
                  <StatusBadge
                    label={profile.isReplyWindowOpen ? 'Còn hạn 24 giờ' : 'Đã hết hạn 24 giờ'}
                    tone={profile.isReplyWindowOpen ? 'success' : 'danger'}
                  />
                </div>
                {profile.replyWindowClosesAt && (
                  <small className="info-window-closes">
                    Đóng: {formatDateTime(profile.replyWindowClosesAt)}
                  </small>
                )}
              </div>
            )}
          </section>

          {/* 4. Ảnh/Video đã trao đổi */}
          <section className="info-card" data-testid="info-section-media">
            <div className="info-section-header">
              <h5 className="info-section-title">Ảnh / Video ({mediaList.length})</h5>
            </div>
            {mediaList.length === 0 ? (
              <p className="info-empty-note">Chưa có ảnh/video nào được chia sẻ.</p>
            ) : (
              <div className="info-media-grid" data-testid="info-media-grid">
                {mediaList.map((item, idx) => (
                  <button
                    key={idx}
                    type="button"
                    className="info-media-thumb"
                    onClick={() => setLightboxUrl(item.url)}
                    data-testid={`media-item-${idx}`}
                    title="Bấm để xem ảnh lớn"
                  >
                    <img src={item.url} alt={`Media ${idx + 1}`} loading="lazy" />
                  </button>
                ))}
              </div>
            )}
          </section>

          {/* 5. Hoạt động (Timeline) */}
          <section className="info-card" data-testid="info-section-activities">
            <h5 className="info-section-title">Lịch sử hoạt động ({activitiesList.length})</h5>
            {activitiesList.length === 0 ? (
              <p className="info-empty-note">Chưa có lịch sử hoạt động.</p>
            ) : (
              <div className="info-timeline" data-testid="info-timeline">
                {activitiesList.map((act, idx) => (
                  <div key={idx} className="info-timeline-item" data-testid={`activity-item-${idx}`}>
                    <div className="info-timeline-bullet" />
                    <div className="info-timeline-content">
                      <div className="info-timeline-top">
                        <strong className="info-timeline-action">
                          {getActionLabel(act.actionType)}
                        </strong>
                        <span className="info-timeline-time">
                          {formatDateTime(act.createdAt)}
                        </span>
                      </div>
                      <div className="info-timeline-actor">
                        {act.actorUserName ? `Bởi: ${act.actorUserName}` : 'Hệ thống'}
                        {act.detail && ` · ${act.detail}`}
                      </div>
                    </div>
                  </div>
                ))}
              </div>
            )}
          </section>

          {/* 6. Hội thoại khác trên cùng page */}
          <section className="info-card" data-testid="info-section-other">
            <h5 className="info-section-title">
              Hội thoại khác trên kênh này ({otherList.length})
            </h5>
            {otherList.length === 0 ? (
              <p className="info-empty-note">Không có hội thoại khác trên cùng page.</p>
            ) : (
              <ul className="info-other-list" data-testid="info-other-list">
                {otherList.map((other) => {
                  const otherStatus = getStatusMeta(other.inboxStatus)
                  const otherIsMsg =
                    String(other.kind) === '1' || String(other.kind).toLowerCase() === 'message'
                  return (
                    <li key={other.id} className="info-other-item">
                      <button
                        type="button"
                        className="info-other-btn"
                        onClick={() => handleOpenRelated(other)}
                        data-testid={`other-conversation-${other.id}`}
                      >
                        <div className="info-other-top">
                          <span className="info-other-kind">
                            {otherIsMsg ? '💬 Tin nhắn' : '📝 Bình luận'}
                          </span>
                          <StatusBadge label={otherStatus.label} tone={otherStatus.tone} />
                        </div>
                        <p className="info-other-snippet">
                          {other.snippet || '(Không có nội dung)'}
                        </p>
                        <span className="info-other-time">
                          {formatDateTime(other.lastActivityAt)}
                        </span>
                      </button>
                    </li>
                  )
                })}
              </ul>
            )}
          </section>
        </div>
      )}

      {/* Lightbox Preview Modal */}
      {lightboxUrl && (
        <div
          className="info-lightbox-backdrop"
          onClick={() => setLightboxUrl(null)}
          data-testid="info-lightbox"
          role="dialog"
          aria-label="Xem ảnh lớn"
        >
          <div className="info-lightbox-content" onClick={(e) => e.stopPropagation()}>
            <img src={lightboxUrl} alt="Xem phóng to" />
            <button
              type="button"
              className="info-lightbox-close"
              onClick={() => setLightboxUrl(null)}
              aria-label="Đóng xem ảnh"
            >
              ×
            </button>
          </div>
        </div>
      )}
    </aside>
  )
}
