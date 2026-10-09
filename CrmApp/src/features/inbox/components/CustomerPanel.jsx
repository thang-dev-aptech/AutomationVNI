import React, { useState, useEffect, useRef } from 'react'
import { useNavigate, useInRouterContext } from 'react-router-dom'
import { formatVietnamDateTime } from '../../../shared/utils/dateUtils'
import { inboxApi } from '../api/inboxApi'
import { PlatformLogo, PLATFORM_LABELS, platformKeyFromEnum } from './PlatformLogo'
import { opportunityApi } from '../../opportunities/api/opportunityApi'
import './CustomerPanel.css'
import Icon from '../../../shared/components/Icon'

function useSafeNavigate() {
  const inRouter = useInRouterContext()
  const nav = inRouter ? useNavigate() : null
  return nav || ((to) => {
    if (typeof window !== 'undefined') window.location.href = to
  })
}

const MESSAGE_KIND = 1

/**
 * Logo cho một identity: Facebook của nguồn tin nhắn → Messenger (khi xác định được:
 * identity ghi nhận nguồn Message, hoặc đúng người đang nhắn trong hội thoại tin nhắn này);
 * không xác định được thì giữ Facebook.
 */
function identityLogoKey(idnt, item, participant) {
  const key = platformKeyFromEnum(idnt.platform)
  if (key !== 'facebook') return key
  const fromMessage =
    Number(idnt.source) === 1 ||
    (item?.kind === MESSAGE_KIND && participant?.externalId && idnt.externalId === participant.externalId)
  return fromMessage ? 'messenger' : 'facebook'
}

function getStatusLabel(status) {
  const s = Number(status)
  if (s === 1) return 'Mới'
  if (s === 2) return 'Đang xử lý'
  if (s === 3) return 'Đã trả lời'
  if (s === 4) return 'Bỏ qua'
  return `Trạng thái ${status}`
}

function getInitials(name) {
  const str = String(name || '?').trim()
  const parts = str.split(/\s+/).filter(Boolean)
  if (parts.length === 0) return '?'
  if (parts.length === 1) return parts[0].slice(0, 2).toUpperCase()
  return (parts[0][0] + parts[parts.length - 1][0]).toUpperCase()
}

export function CustomerPanel({
  item,
  tags = [],
  isReadOnly = false,
  canCare = true,
  onClose,
  onCustomerLoaded,
}) {
  const navigate = useSafeNavigate()

  const [profile, setProfile] = useState(null)
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState(null)
  const [lightboxUrl, setLightboxUrl] = useState(null)

  // Collapsible sections state
  const [expandedSections, setExpandedSections] = useState({
    info: true,
    channels: true,
    stats: true,
    media: true,
    activities: true,
  })

  const toggleSection = (sectionKey) => {
    setExpandedSections((prev) => ({
      ...prev,
      [sectionKey]: !prev[sectionKey],
    }))
  }

  // Opportunity state
  const [openOpportunity, setOpenOpportunity] = useState(null)
  const [creatingOpportunity, setCreatingOpportunity] = useState(false)

  useEffect(() => {
    let active = true
    if (!item?.id) {
      setOpenOpportunity(null)
      return
    }
    const checkOpp = async () => {
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
      }
    }
    checkOpp()
    return () => {
      active = false
    }
  }, [item?.id, item?.kind])

  const handleCreateOpportunity = async () => {
    if (creatingOpportunity || !item?.id || isReadOnly) return
    setCreatingOpportunity(true)
    try {
      const res = await opportunityApi.fromConversation({
        kind: item.kind === 2 ? 'comment' : 'message',
        id: item.id,
      })
      setOpenOpportunity(res)
    } catch {
      // ignore
    } finally {
      setCreatingOpportunity(false)
    }
  }

  // Ref to track current request id to prevent race conditions on item switch
  const currentReqRef = useRef(0)

  const fetchCustomer = async () => {
    if (!item?.id) return
    const reqId = ++currentReqRef.current
    setLoading(true)
    setError(null)
    setProfile(null)

    try {
      const data = await inboxApi.getCustomer(item.kind, item.id)
      if (currentReqRef.current === reqId) {
        setProfile(data)
        setLoading(false)
        onCustomerLoaded?.(data)
      }
    } catch (err) {
      if (currentReqRef.current === reqId) {
        setError(err?.response?.data?.message || err?.message || 'Không thể tải hồ sơ khách')
        setLoading(false)
      }
    }
  }

  useEffect(() => {
    if (!item?.id) {
      setProfile(null)
      setLoading(false)
      setError(null)
      return
    }

    // Immediately clear previous customer data and set loading
    setProfile(null)
    setLoading(true)
    setError(null)
    const reqId = ++currentReqRef.current

    const run = async () => {
      try {
        const data = await inboxApi.getCustomer(item.kind, item.id)
        if (currentReqRef.current === reqId) {
          setProfile(data)
          setLoading(false)
          onCustomerLoaded?.(data)
        }
      } catch (err) {
        if (currentReqRef.current === reqId) {
          setError(err?.response?.data?.message || err?.message || 'Không thể tải hồ sơ khách')
          setLoading(false)
        }
      }
    }

    run()

    return () => {
      currentReqRef.current++
    }
  }, [item?.id, item?.kind])

  if (!item) {
    return (
      <aside
        className="crm-customer-panel"
        data-testid="customer-panel"
        aria-label="Hồ sơ khách hàng"
      >
        <div className="crm-customer-panel-header">
          <div className="crm-customer-panel-title-wrap">
            <h3 className="crm-customer-panel-title">Hồ sơ khách hàng</h3>
            <span className="crm-customer-panel-badge">SO9</span>
          </div>
          {onClose && (
            <button
              type="button"
              className="crm-customer-toggle-btn crm-customer-toggle-btn--close"
              data-testid="toggle-customer-panel"
              onClick={onClose}
              aria-label="Thu gọn hồ sơ"
              title="Thu gọn hồ sơ"
            >
              ✕ Thu gọn
            </button>
          )}
        </div>
        <div className="crm-customer-empty" data-testid="customer-panel-empty">
          Chọn một hội thoại để xem hồ sơ khách hàng
        </div>
      </aside>
    )
  }

  const linked = profile?.linked === true
  const customer = profile?.customer
  const participant = profile?.participant || {}
  const stats = profile?.stats || {}
  const mediaList = profile?.media || []
  const activitiesList = profile?.activities || []

  const displayName = linked
    ? customer?.displayName || participant?.displayName || 'Khách hàng'
    : participant?.displayName || 'Khách hàng'

  const avatarUrl = participant?.avatarUrl
  const phone = linked ? (customer?.phoneE164 || '—') : '—'
  const email = linked ? (customer?.email || '—') : '—'

  // Match tag objects from tagIds
  const customerTags = linked && customer?.tagIds?.length
    ? tags.filter((t) => customer.tagIds.includes(t.id))
    : []

  // Build identities list
  const identities = linked && customer?.identities?.length
    ? customer.identities
    : participant?.platform
      ? [
          {
            platform: participant.platform,
            channelName: participant.channelName,
            displayName: participant.displayName || participant.externalId,
            externalId: participant.externalId,
          },
        ]
      : []

  return (
    <aside
      className="crm-customer-panel"
      data-testid="customer-panel"
      aria-label="Hồ sơ khách hàng"
    >
      <div className="crm-customer-panel-header">
        <div className="crm-customer-panel-title-wrap">
          <h3 className="crm-customer-panel-title">Hồ sơ khách hàng</h3>
          <span className="crm-customer-panel-badge">SO9</span>
        </div>
        {onClose && (
          <button
            type="button"
            className="crm-customer-toggle-btn crm-customer-toggle-btn--close"
            data-testid="toggle-customer-panel"
            onClick={onClose}
            aria-label="Thu gọn hồ sơ"
            title="Thu gọn hồ sơ"
          >
            ✕ Thu gọn
          </button>
        )}
      </div>

      <div className="crm-customer-panel-body">
        {loading && (
          <div className="crm-customer-skeleton-wrap" data-testid="customer-panel-skeleton">
            <div className="crm-customer-skeleton-header">
              <div className="crm-customer-skeleton-avatar" />
              <div className="crm-customer-skeleton-lines">
                <div className="crm-customer-skeleton-line crm-customer-skeleton-line--title" />
                <div className="crm-customer-skeleton-line crm-customer-skeleton-line--sub" />
              </div>
            </div>
            <div className="crm-customer-skeleton-card" />
            <div className="crm-customer-skeleton-card" />
          </div>
        )}

        {error && !loading && (
          <div className="crm-customer-error-banner" data-testid="customer-panel-error">
            <div><Icon name="alert" size={16} /> {error}</div>
            <button
              type="button"
              className="crm-customer-retry-btn"
              data-testid="btn-retry-customer"
              onClick={fetchCustomer}
            >
              Thử lại
            </button>
          </div>
        )}

        {!loading && !error && profile && (
          <div className="crm-customer-panel-inner" data-testid="customer-panel-content">
            {/* 1. Thông tin khách */}
            <section
              className={`crm-customer-section ${expandedSections.info ? 'crm-customer-section--expanded' : ''}`}
              data-testid="customer-section-info"
            >
              <button
                type="button"
                className="crm-customer-section-header"
                onClick={() => toggleSection('info')}
                aria-expanded={expandedSections.info}
                data-testid="toggle-section-info"
              >
                <div className="crm-customer-section-title-wrap">
                  <h4 className="crm-customer-section-title">Thông tin khách</h4>
                </div>
                <span className="crm-customer-section-chevron">
                  {expandedSections.info ? '▾' : '▸'}
                </span>
              </button>

              {expandedSections.info && (
                <div className="crm-customer-section-body">
                  <div className="crm-customer-profile-hero" data-testid="customer-panel-hero">
                    <div className="crm-customer-avatar">
                      {avatarUrl ? (
                        <img
                          src={avatarUrl}
                          alt={displayName}
                          className="crm-customer-avatar"
                        />
                      ) : (
                        <span>{getInitials(displayName)}</span>
                      )}
                    </div>
                    <div className="crm-customer-hero-meta">
                      <h5 className="crm-customer-name" data-testid="customer-display-name">
                        {displayName}
                      </h5>
                      {!linked && (
                        <span
                          className="crm-customer-unlinked-badge"
                          data-testid="badge-unlinked"
                        >
                          Chưa liên kết hồ sơ
                        </span>
                      )}
                      {!isReadOnly && linked && customer?.id && (
                        <button
                          type="button"
                          className="crm-customer-open-profile-btn"
                          data-testid="btn-open-customer-profile"
                          onClick={() => navigate(`/customers/${customer.id}`)}
                        >
                          Mở hồ sơ
                        </button>
                      )}
                      {openOpportunity ? (
                        <button
                          type="button"
                          className="crm-customer-open-profile-btn"
                          data-testid="btn-view-opportunity-panel"
                          onClick={() => navigate(`/tasks?opportunity=${openOpportunity.id}`)}
                          style={{ marginLeft: '6px' }}
                        >
                          <Icon name="target" size={14} /> Xem cơ hội
                        </button>
                      ) : (
                        !isReadOnly && canCare && (
                          <button
                            type="button"
                            className="crm-customer-open-profile-btn"
                            data-testid="btn-create-opportunity-panel"
                            onClick={handleCreateOpportunity}
                            disabled={creatingOpportunity}
                            style={{ marginLeft: '6px' }}
                          >
                            + Tạo cơ hội
                          </button>
                        )
                      )}
                    </div>
                  </div>

                  <div className="crm-customer-kv-list">
                    <div className="crm-customer-kv-row">
                      <span className="crm-customer-kv-label">SĐT</span>
                      <span className="crm-customer-kv-value" data-testid="customer-phone">
                        {phone}
                      </span>
                    </div>
                    <div className="crm-customer-kv-row">
                      <span className="crm-customer-kv-label">Email</span>
                      <span className="crm-customer-kv-value" data-testid="customer-email">
                        {email}
                      </span>
                    </div>
                    <div className="crm-customer-kv-row">
                      <span className="crm-customer-kv-label">Thẻ nhãn</span>
                      <div className="crm-customer-tags-wrap" data-testid="customer-tags">
                        {customerTags.length > 0 ? (
                          customerTags.map((t) => (
                            <span
                              key={t.id}
                              className="crm-customer-tag-pill"
                              style={t.colorHex ? { backgroundColor: `${t.colorHex}22`, color: t.colorHex } : undefined}
                            >
                              {t.name}
                            </span>
                          ))
                        ) : (
                          <span className="crm-customer-kv-value">—</span>
                        )}
                      </div>
                    </div>
                  </div>
                </div>
              )}
            </section>

            {/* 2. Kênh liên lạc */}
            <section
              className={`crm-customer-section ${expandedSections.channels ? 'crm-customer-section--expanded' : ''}`}
              data-testid="customer-section-channels"
            >
              <button
                type="button"
                className="crm-customer-section-header"
                onClick={() => toggleSection('channels')}
                aria-expanded={expandedSections.channels}
                data-testid="toggle-section-channels"
              >
                <div className="crm-customer-section-title-wrap">
                  <h4 className="crm-customer-section-title">Kênh liên lạc</h4>
                  {identities.length > 0 && (
                    <span className="crm-customer-section-count">{identities.length}</span>
                  )}
                </div>
                <span className="crm-customer-section-chevron">
                  {expandedSections.channels ? '▾' : '▸'}
                </span>
              </button>

              {expandedSections.channels && (
                <div className="crm-customer-section-body">
                  {identities.length === 0 ? (
                    <p className="crm-customer-empty" style={{ padding: '8px 0' }}>
                      Chưa có kênh liên lạc nào.
                    </p>
                  ) : (
                    <div className="crm-customer-identities-list" data-testid="customer-identities-list">
                      {identities.map((idnt, idx) => {
                        const logoKey = identityLogoKey(idnt, item, participant)
                        const platLabel = PLATFORM_LABELS[logoKey]
                        return (
                          <div
                            key={idx}
                            className="crm-customer-identity-card"
                            data-testid={`identity-item-${idx}`}
                          >
                            <span
                              className="crm-customer-identity-platform-icon"
                              title={platLabel}
                              role="img"
                              aria-label={platLabel}
                            >
                              <PlatformLogo name={logoKey} />
                            </span>
                            <div className="crm-customer-identity-info">
                              <span className="crm-customer-identity-channel">
                                {idnt.channelName || platLabel}
                              </span>
                              <span className="crm-customer-identity-name">
                                {idnt.displayName || idnt.externalId || 'Khách'}
                              </span>
                            </div>
                          </div>
                        )
                      })}
                    </div>
                  )}
                </div>
              )}
            </section>

            {/* 3. Thống kê tương tác */}
            <section
              className={`crm-customer-section ${expandedSections.stats ? 'crm-customer-section--expanded' : ''}`}
              data-testid="customer-section-stats"
            >
              <button
                type="button"
                className="crm-customer-section-header"
                onClick={() => toggleSection('stats')}
                aria-expanded={expandedSections.stats}
                data-testid="toggle-section-stats"
              >
                <div className="crm-customer-section-title-wrap">
                  <h4 className="crm-customer-section-title">Thống kê tương tác</h4>
                </div>
                <span className="crm-customer-section-chevron">
                  {expandedSections.stats ? '▾' : '▸'}
                </span>
              </button>

              {expandedSections.stats && (
                <div className="crm-customer-section-body">
                  <div className="crm-customer-stats-grid">
                    <div className="crm-customer-stat-box">
                      <span className="crm-customer-stat-num" data-testid="stat-messages">
                        {stats.messageCount ?? 0}
                      </span>
                      <span className="crm-customer-stat-lbl">Tin nhắn</span>
                    </div>
                    <div className="crm-customer-stat-box">
                      <span className="crm-customer-stat-num" data-testid="stat-comments">
                        {stats.commentCount ?? 0}
                      </span>
                      <span className="crm-customer-stat-lbl">Bình luận</span>
                    </div>
                  </div>

                  <div className="crm-customer-kv-list">
                    <div className="crm-customer-kv-row">
                      <span className="crm-customer-kv-label">Lần đầu</span>
                      <span className="crm-customer-kv-value" data-testid="stat-first-interaction">
                        {stats.firstInteractionAt ? formatVietnamDateTime(stats.firstInteractionAt) : '—'}
                      </span>
                    </div>
                    <div className="crm-customer-kv-row">
                      <span className="crm-customer-kv-label">Lần cuối</span>
                      <span className="crm-customer-kv-value" data-testid="stat-last-interaction">
                        {stats.lastInteractionAt ? formatVietnamDateTime(stats.lastInteractionAt) : '—'}
                      </span>
                    </div>
                    <div className="crm-customer-kv-row">
                      <span className="crm-customer-kv-label">Phụ trách</span>
                      <span className="crm-customer-kv-value" data-testid="stat-assignee">
                        {stats.assignedTo || 'Chưa phân công'}
                      </span>
                    </div>
                    <div className="crm-customer-kv-row">
                      <span className="crm-customer-kv-label">Trạng thái</span>
                      <span className="crm-customer-kv-value" data-testid="stat-inbox-status">
                        {getStatusLabel(stats.inboxStatus ?? item?.status ?? 1)}
                      </span>
                    </div>
                  </div>

                  {/* Cửa sổ 24h đối với tin nhắn */}
                  {(item?.kind === 1 || String(item?.kind).toLowerCase() === 'message') && (
                    <div className="crm-customer-window-block" data-testid="reply-window-block">
                      <div className="crm-customer-window-row">
                        <span style={{ fontWeight: 600, fontSize: '12px' }}>Cửa sổ 24h:</span>
                        <span
                          className={`crm-customer-window-badge ${stats.isReplyWindowOpen ? 'crm-customer-window-badge--open' : 'crm-customer-window-badge--closed'}`}
                          data-testid="badge-reply-window"
                        >
                          {stats.isReplyWindowOpen ? 'Còn hạn 24h' : 'Hết hạn'}
                        </span>
                      </div>
                      {stats.replyWindowClosesAt && (
                        <div className="crm-customer-window-time">
                          Hạn chót: {formatVietnamDateTime(stats.replyWindowClosesAt)}
                        </div>
                      )}
                    </div>
                  )}
                </div>
              )}
            </section>

            {/* 4. Ảnh/Video */}
            <section
              className={`crm-customer-section ${expandedSections.media ? 'crm-customer-section--expanded' : ''}`}
              data-testid="customer-section-media"
            >
              <button
                type="button"
                className="crm-customer-section-header"
                onClick={() => toggleSection('media')}
                aria-expanded={expandedSections.media}
                data-testid="toggle-section-media"
              >
                <div className="crm-customer-section-title-wrap">
                  <h4 className="crm-customer-section-title">Ảnh/Video</h4>
                  {mediaList.length > 0 && (
                    <span className="crm-customer-section-count">{mediaList.length}</span>
                  )}
                </div>
                <span className="crm-customer-section-chevron">
                  {expandedSections.media ? '▾' : '▸'}
                </span>
              </button>

              {expandedSections.media && (
                <div className="crm-customer-section-body">
                  {mediaList.length === 0 ? (
                    <p className="crm-customer-media-empty">Chưa có ảnh/video nào.</p>
                  ) : (
                    <div className="crm-customer-media-grid" data-testid="customer-media-grid">
                      {mediaList.map((m, idx) => (
                        <button
                          key={idx}
                          type="button"
                          className="crm-customer-media-thumb"
                          onClick={() => setLightboxUrl(m.url)}
                          data-testid={`media-thumb-${idx}`}
                          title="Bấm để xem ảnh lớn"
                        >
                          <img src={m.url} alt={`Ảnh ${idx + 1}`} loading="lazy" />
                        </button>
                      ))}
                    </div>
                  )}
                </div>
              )}
            </section>

            {/* 5. Hoạt động */}
            <section
              className={`crm-customer-section ${expandedSections.activities ? 'crm-customer-section--expanded' : ''}`}
              data-testid="customer-section-activities"
            >
              <button
                type="button"
                className="crm-customer-section-header"
                onClick={() => toggleSection('activities')}
                aria-expanded={expandedSections.activities}
                data-testid="toggle-section-activities"
              >
                <div className="crm-customer-section-title-wrap">
                  <h4 className="crm-customer-section-title">Hoạt động</h4>
                  {activitiesList.length > 0 && (
                    <span className="crm-customer-section-count">{activitiesList.length}</span>
                  )}
                </div>
                <span className="crm-customer-section-chevron">
                  {expandedSections.activities ? '▾' : '▸'}
                </span>
              </button>

              {expandedSections.activities && (
                <div className="crm-customer-section-body">
                  {activitiesList.length === 0 ? (
                    <p className="crm-customer-activities-empty">Chưa có hoạt động nào.</p>
                  ) : (
                    <div className="crm-customer-timeline" data-testid="customer-timeline">
                      {activitiesList.map((act, idx) => {
                        const actTime = act.at || act.createdAt
                        const actDesc = act.title || act.description || act.body || act.detail || 'Hoạt động'
                        return (
                          <div
                            key={idx}
                            className="crm-customer-timeline-item"
                            data-testid={`activity-item-${idx}`}
                          >
                            <div className="crm-customer-timeline-bullet" />
                            <div className="crm-customer-timeline-top">
                              <span className="crm-customer-timeline-title">{actDesc}</span>
                              <span className="crm-customer-timeline-time">
                                {formatVietnamDateTime(actTime)}
                              </span>
                            </div>
                            {act.actor && (
                              <span className="crm-customer-timeline-actor">
                                Bởi: {act.actor}
                              </span>
                            )}
                          </div>
                        )
                      })}
                    </div>
                  )}
                </div>
              )}
            </section>
          </div>
        )}
      </div>

      {/* Lightbox Modal */}
      {lightboxUrl && (
        <div
          className="crm-customer-lightbox-backdrop"
          onClick={() => setLightboxUrl(null)}
          data-testid="customer-lightbox"
          role="dialog"
          aria-label="Xem ảnh lớn"
        >
          <div
            className="crm-customer-lightbox-content"
            onClick={(e) => e.stopPropagation()}
          >
            <img src={lightboxUrl} alt="Xem ảnh phóng to" />
            <button
              type="button"
              className="crm-customer-lightbox-close"
              data-testid="btn-close-lightbox"
              onClick={() => setLightboxUrl(null)}
              aria-label="Đóng xem ảnh"
            >
              ✕
            </button>
          </div>
        </div>
      )}
    </aside>
  )
}

export default CustomerPanel
