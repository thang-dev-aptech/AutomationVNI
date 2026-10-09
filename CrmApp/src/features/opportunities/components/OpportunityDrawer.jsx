import React, { useState, useEffect } from 'react'
import { Link } from 'react-router-dom'
import { opportunityApi } from '../api/opportunityApi'
import { formatVietnamDateTime } from '../../../shared/utils/dateUtils'
import { formatCurrencyVnd } from './OpportunityStatsBar'
import { SourceBadge } from '../../inbox/components/SourceBadge'
import { reminderApi } from '../../tasks/api/reminderApi'

export const OpportunityDrawer = ({
  opportunityId,
  isOpen = false,
  onClose,
  onEdit,
  onOpportunityUpdated,
  isReadOnly = false,
  stages = [],
  users = [],
}) => {
  const [data, setData] = useState(null)
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState(null)

  const [addingWatcher, setAddingWatcher] = useState(false)
  const [selectedWatcherId, setSelectedWatcherId] = useState('')

  // Reminders state
  const [reminders, setReminders] = useState([])
  const [loadingReminders, setLoadingReminders] = useState(false)
  const [addingReminder, setAddingReminder] = useState(false)
  const [reminderTitle, setReminderTitle] = useState('')
  const [reminderDue, setReminderDue] = useState('')
  const [savingReminder, setSavingReminder] = useState(false)

  const loadReminders = async (oppId) => {
    if (!oppId) return
    setLoadingReminders(true)
    try {
      const res = await reminderApi.listForOpportunity(oppId)
      setReminders(Array.isArray(res) ? res : res?.items || [])
    } catch {
      setReminders([])
    } finally {
      setLoadingReminders(false)
    }
  }

  useEffect(() => {
    if (!isOpen || !opportunityId) {
      setData(null)
      setError(null)
      setReminders([])
      return
    }

    const loadDetail = async () => {
      setLoading(true)
      setError(null)
      try {
        const detail = await opportunityApi.get(opportunityId)
        setData(detail)
        loadReminders(opportunityId)
      } catch (err) {
        setError(err?.response?.data?.message || err?.message || 'Không thể tải chi tiết cơ hội')
      } finally {
        setLoading(false)
      }
    }

    loadDetail()
  }, [isOpen, opportunityId])

  const handleCreateReminder = async (e) => {
    e.preventDefault()
    if (!reminderTitle.trim() || !reminderDue || !opportunityId) return
    setSavingReminder(true)
    try {
      await reminderApi.create({
        crmCustomerId: data?.crmCustomerId,
        crmOpportunityId: opportunityId,
        title: reminderTitle.trim(),
        dueAtUtc: new Date(reminderDue).toISOString(),
      })
      setReminderTitle('')
      setReminderDue('')
      setAddingReminder(false)
      await loadReminders(opportunityId)
      onOpportunityUpdated?.()
    } catch (err) {
      alert('Lỗi tạo nhắc việc: ' + (err?.response?.data?.message || err?.message))
    } finally {
      setSavingReminder(false)
    }
  }

  const handleCompleteReminder = async (reminderId) => {
    try {
      await reminderApi.complete(reminderId)
      await loadReminders(opportunityId)
      onOpportunityUpdated?.()
    } catch (err) {
      alert('Lỗi hoàn thành nhắc việc: ' + (err?.message || ''))
    }
  }

  if (!isOpen) return null

  const handleAddWatcher = async () => {
    if (!selectedWatcherId || !opportunityId) return
    try {
      await opportunityApi.addWatcher(opportunityId, selectedWatcherId)
      const updated = await opportunityApi.get(opportunityId)
      setData(updated)
      setSelectedWatcherId('')
      setAddingWatcher(false)
      onOpportunityUpdated?.()
    } catch (err) {
      alert('Lỗi thêm người theo dõi: ' + (err?.response?.data?.message || err?.message))
    }
  }

  const handleRemoveWatcher = async (userId) => {
    if (!opportunityId) return
    try {
      await opportunityApi.removeWatcher(opportunityId, userId)
      const updated = await opportunityApi.get(opportunityId)
      setData(updated)
      onOpportunityUpdated?.()
    } catch (err) {
      alert('Lỗi xoá người theo dõi: ' + (err?.response?.data?.message || err?.message))
    }
  }

  const getStatusBadge = (status, isArchived) => {
    if (isArchived) return <span className="crm-opp-badge crm-opp-badge--archived">Đã lưu trữ</span>
    if (status === 2) return <span className="crm-opp-badge crm-opp-badge--won">Thành công (Won)</span>
    if (status === 3) return <span className="crm-opp-badge crm-opp-badge--lost">Thất bại (Lost)</span>
    return <span className="crm-opp-badge crm-opp-badge--open">Đang mở</span>
  }

  return (
    <div className="crm-opp-drawer-backdrop" onClick={onClose} data-testid="opportunity-drawer-backdrop">
      <div
        className="crm-opp-drawer"
        onClick={(e) => e.stopPropagation()}
        data-testid="opportunity-drawer"
        role="dialog"
        aria-label="Chi tiết cơ hội"
      >
        <div className="crm-opp-drawer-header">
          <div className="crm-opp-drawer-title-wrap">
            <span className="crm-opp-drawer-badge">Chi tiết cơ hội</span>
            <h3 className="crm-opp-drawer-title" data-testid="drawer-opp-title">
              {loading ? 'Đang tải...' : data?.title || 'Cơ hội'}
            </h3>
          </div>
          <div className="crm-opp-drawer-actions">
            {!isReadOnly && data && (
              <button
                type="button"
                className="crm-opp-btn crm-opp-btn--secondary crm-opp-btn--sm"
                onClick={() => onEdit?.(data)}
                data-testid="drawer-btn-edit"
              >
                ✏️ Chỉnh sửa
              </button>
            )}
            <button
              type="button"
              className="crm-opp-drawer-close"
              onClick={onClose}
              aria-label="Đóng chi tiết"
              data-testid="btn-close-drawer"
            >
              ✕
            </button>
          </div>
        </div>

        <div className="crm-opp-drawer-body">
          {loading && (
            <div className="crm-opp-drawer-loading" data-testid="drawer-loading">
              Đang tải dữ liệu cơ hội...
            </div>
          )}

          {error && (
            <div className="crm-opp-drawer-error" data-testid="drawer-error">
              ⚠️ {error}
            </div>
          )}

          {!loading && !error && data && (
            <div className="crm-opp-drawer-content" data-testid="drawer-content">
              {/* Summary Hero Card */}
              <div className="crm-opp-drawer-card crm-opp-drawer-hero">
                <div className="crm-opp-drawer-hero-val">
                  <span className="crm-opp-drawer-hero-lbl">Giá trị dự kiến</span>
                  <strong className="crm-opp-drawer-hero-num" data-testid="drawer-opp-value">
                    {formatCurrencyVnd(data.expectedValue)}
                  </strong>
                </div>
                <div className="crm-opp-drawer-hero-status">
                  {getStatusBadge(data.status, data.isArchived)}
                </div>
              </div>

              {/* Thông tin chính */}
              <div className="crm-opp-drawer-card">
                <h4 className="crm-opp-drawer-section-title">Thông tin chung</h4>
                <div className="crm-opp-drawer-kv-list">
                  <div className="crm-opp-drawer-kv-row">
                    <span className="crm-opp-drawer-kv-lbl">Khách hàng</span>
                    <span className="crm-opp-drawer-kv-val">
                      <Link
                        to={`/customers/${data.crmCustomerId}`}
                        className="crm-opp-link"
                        title="Xem hồ sơ khách"
                        data-testid="drawer-customer-link"
                      >
                        👤 <strong>{data.customerName || 'Khách hàng'}</strong> →
                      </Link>
                    </span>
                  </div>

                  <div className="crm-opp-drawer-kv-row">
                    <span className="crm-opp-drawer-kv-lbl">Số điện thoại</span>
                    <span className="crm-opp-drawer-kv-val" data-testid="drawer-customer-phone">
                      {data.customerPhoneE164 || '—'}
                    </span>
                  </div>

                  <div className="crm-opp-drawer-kv-row">
                    <span className="crm-opp-drawer-kv-lbl">Giai đoạn</span>
                    <span className="crm-opp-drawer-kv-val" data-testid="drawer-opp-stage">
                      <span
                        className="crm-opp-stage-pill"
                        style={{
                          backgroundColor: data.stageColor ? `${data.stageColor}22` : '#e0e7ff',
                          color: data.stageColor || '#4338ca',
                        }}
                      >
                        {data.stageName || 'Giai đoạn'}
                      </span>
                    </span>
                  </div>

                  <div className="crm-opp-drawer-kv-row">
                    <span className="crm-opp-drawer-kv-lbl">Người phụ trách</span>
                    <span className="crm-opp-drawer-kv-val" data-testid="drawer-opp-assignee">
                      {data.assignedTo || 'Chưa phân công'}
                    </span>
                  </div>

                  <div className="crm-opp-drawer-kv-row">
                    <span className="crm-opp-drawer-kv-lbl">Kênh nguồn</span>
                    <span className="crm-opp-drawer-kv-val" data-testid="drawer-opp-source">
                      {data.channelPlatform ? (
                        <span className="crm-opp-source-flex">
                          <SourceBadge
                            item={{
                              platform: data.channelPlatform,
                              kind: data.source === 2 ? 1 : 2,
                              id: data.id,
                            }}
                          />
                          <span>{data.channelName || 'Mạng xã hội'}</span>
                          {(data.pageConversationId || data.socialCommentId) && (
                            <Link
                              to={`/inbox?kind=${data.source === 2 ? 'message' : 'comment'}&id=${data.pageConversationId || data.socialCommentId}`}
                              className="crm-opp-link crm-opp-link--sm"
                              title="Mở hội thoại"
                            >
                              (Mở chat)
                            </Link>
                          )}
                        </span>
                      ) : (
                        <span>✍️ Thủ công</span>
                      )}
                    </span>
                  </div>

                  {data.sourceSnippet && (
                    <div className="crm-opp-drawer-kv-row">
                      <span className="crm-opp-drawer-kv-lbl">Trích đoạn</span>
                      <span className="crm-opp-drawer-kv-val crm-opp-drawer-snippet">
                        "{data.sourceSnippet}"
                      </span>
                    </div>
                  )}

                  <div className="crm-opp-drawer-kv-row">
                    <span className="crm-opp-drawer-kv-lbl">Ngày tạo</span>
                    <span className="crm-opp-drawer-kv-val">
                      {formatVietnamDateTime(data.createdAt)}
                    </span>
                  </div>

                  <div className="crm-opp-drawer-kv-row">
                    <span className="crm-opp-drawer-kv-lbl">Hoạt động cuối</span>
                    <span className="crm-opp-drawer-kv-val">
                      {formatVietnamDateTime(data.lastActivityAtUtc || data.createdAt)}
                    </span>
                  </div>

                  {data.closedAtUtc && (
                    <div className="crm-opp-drawer-kv-row">
                      <span className="crm-opp-drawer-kv-lbl">Ngày đóng</span>
                      <span className="crm-opp-drawer-kv-val">
                        {formatVietnamDateTime(data.closedAtUtc)}
                      </span>
                    </div>
                  )}

                  {data.lostReason && (
                    <div className="crm-opp-drawer-kv-row">
                      <span className="crm-opp-drawer-kv-lbl">Lý do thất bại</span>
                      <span className="crm-opp-drawer-kv-val crm-opp-lost-text">
                        {data.lostReason}
                      </span>
                    </div>
                  )}
                </div>
              </div>

              {/* Người theo dõi (Watchers) */}
              <div className="crm-opp-drawer-card">
                <div className="crm-opp-drawer-card-header">
                  <h4 className="crm-opp-drawer-section-title">
                    Người theo dõi ({data.watcherUserIds?.length || 0})
                  </h4>
                  {!isReadOnly && !addingWatcher && (
                    <button
                      type="button"
                      className="crm-opp-btn-link"
                      onClick={() => setAddingWatcher(true)}
                      data-testid="btn-add-watcher-toggle"
                    >
                      + Thêm người
                    </button>
                  )}
                </div>

                {!isReadOnly && addingWatcher && (
                  <div className="crm-opp-add-watcher-box">
                    <select
                      className="crm-opp-select crm-opp-select--sm"
                      value={selectedWatcherId}
                      onChange={(e) => setSelectedWatcherId(e.target.value)}
                      data-testid="select-new-watcher"
                    >
                      <option value="">Chọn nhân viên...</option>
                      {users
                        .filter((u) => !data.watcherUserIds?.includes(u.id))
                        .map((u) => (
                          <option key={u.id} value={u.id}>
                            {u.displayName || u.userName}
                          </option>
                        ))}
                    </select>
                    <button
                      type="button"
                      className="crm-opp-btn crm-opp-btn--primary crm-opp-btn--sm"
                      onClick={handleAddWatcher}
                      disabled={!selectedWatcherId}
                      data-testid="btn-confirm-add-watcher"
                    >
                      Lưu
                    </button>
                    <button
                      type="button"
                      className="crm-opp-btn crm-opp-btn--ghost crm-opp-btn--sm"
                      onClick={() => {
                        setAddingWatcher(false)
                        setSelectedWatcherId('')
                      }}
                    >
                      Huỷ
                    </button>
                  </div>
                )}

                <div className="crm-opp-watchers-list" data-testid="drawer-watchers-list">
                  {(!data.watcherUserIds || data.watcherUserIds.length === 0) ? (
                    <p className="crm-opp-empty-hint">Chưa có người theo dõi nào.</p>
                  ) : (
                    data.watcherUserIds.map((wid) => {
                      const u = users.find((user) => user.id === wid)
                      const name = u?.displayName || u?.userName || wid
                      return (
                        <div key={wid} className="crm-opp-watcher-tag" data-testid={`watcher-${wid}`}>
                          <span>👤 {name}</span>
                          {!isReadOnly && (
                            <button
                              type="button"
                              className="crm-opp-watcher-remove"
                              onClick={() => handleRemoveWatcher(wid)}
                              title="Bỏ theo dõi"
                              data-testid={`btn-remove-watcher-${wid}`}
                            >
                              ✕
                            </button>
                          )}
                        </div>
                      )
                    })
                  )}
                </div>
              </div>

              {/* Nhắc việc của cơ hội (Reminders) */}
              <div className="crm-opp-drawer-card" data-testid="drawer-reminders-card">
                <div className="crm-opp-drawer-card-header">
                  <h4 className="crm-opp-drawer-section-title">
                    Nhắc việc ({reminders.length})
                  </h4>
                  {!isReadOnly && !addingReminder && (
                    <button
                      type="button"
                      className="crm-opp-btn-link"
                      onClick={() => setAddingReminder(true)}
                      data-testid="btn-add-opp-reminder-toggle"
                    >
                      + Thêm nhắc việc
                    </button>
                  )}
                </div>

                {!isReadOnly && addingReminder && (
                  <form onSubmit={handleCreateReminder} style={{ marginBottom: '12px' }} data-testid="form-add-opp-reminder">
                    <div style={{ marginBottom: '8px' }}>
                      <input
                        type="text"
                        className="crm-opp-form-control crm-opp-form-control--sm"
                        placeholder="Nội dung việc cần làm..."
                        value={reminderTitle}
                        onChange={(e) => setReminderTitle(e.target.value)}
                        required
                        data-testid="input-drawer-reminder-title"
                      />
                    </div>
                    <div style={{ marginBottom: '8px' }}>
                      <input
                        type="datetime-local"
                        className="crm-opp-form-control crm-opp-form-control--sm"
                        value={reminderDue}
                        onChange={(e) => setReminderDue(e.target.value)}
                        required
                        data-testid="input-drawer-reminder-due"
                      />
                    </div>
                    <div style={{ display: 'flex', gap: '6px', justifyContent: 'flex-end' }}>
                      <button
                        type="button"
                        className="crm-opp-btn crm-opp-btn--ghost crm-opp-btn--sm"
                        onClick={() => {
                          setAddingReminder(false)
                          setReminderTitle('')
                          setReminderDue('')
                        }}
                      >
                        Huỷ
                      </button>
                      <button
                        type="submit"
                        className="crm-opp-btn crm-opp-btn--primary crm-opp-btn--sm"
                        disabled={savingReminder}
                        data-testid="btn-save-drawer-reminder"
                      >
                        {savingReminder ? 'Đang lưu...' : 'Lưu nhắc việc'}
                      </button>
                    </div>
                  </form>
                )}

                <div className="crm-opp-reminders-list" data-testid="drawer-reminders-list">
                  {loadingReminders ? (
                    <p className="crm-opp-empty-hint">Đang tải nhắc việc...</p>
                  ) : reminders.length === 0 ? (
                    <p className="crm-opp-empty-hint">Chưa có nhắc việc nào cho cơ hội này.</p>
                  ) : (
                    reminders.map((r) => (
                      <div
                        key={r.id}
                        className="crm-opp-reminder-item"
                        style={{
                          display: 'flex',
                          justifyContent: 'space-between',
                          alignItems: 'center',
                          padding: '8px 0',
                          borderBottom: '1px solid var(--crm-border)',
                        }}
                        data-testid={`drawer-reminder-${r.id}`}
                      >
                        <div>
                          <strong style={{ fontSize: '13px', textDecoration: r.isCompleted ? 'line-through' : 'none' }}>
                            {r.title}
                          </strong>
                          <div style={{ fontSize: '11px', color: 'var(--crm-text-muted)' }}>
                            Hạn: {formatVietnamDateTime(r.dueAtUtc)}
                            {r.isCompleted && <span style={{ marginLeft: '6px', color: 'var(--crm-success)' }}>✓ Đã xong</span>}
                          </div>
                        </div>
                        {!isReadOnly && !r.isCompleted && (
                          <button
                            type="button"
                            className="crm-opp-btn crm-opp-btn--secondary crm-opp-btn--sm"
                            onClick={() => handleCompleteReminder(r.id)}
                            data-testid={`btn-complete-drawer-reminder-${r.id}`}
                            style={{ fontSize: '11px', padding: '3px 8px' }}
                          >
                            Hoàn thành
                          </button>
                        )}
                      </div>
                    ))
                  )}
                </div>
              </div>
            </div>
          )}
        </div>
      </div>
    </div>
  )
}

export default OpportunityDrawer
