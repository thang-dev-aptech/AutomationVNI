import React, { useState, useEffect } from 'react'
import { reminderApi } from '../api/reminderApi'
import { customerApi } from '../../customers/api/customerApi'
import { opportunityApi } from '../../opportunities/api/opportunityApi'
import { useAuth } from '../../../auth/useAuth'
import Button from '../../../shared/components/Button'
import Badge from '../../../shared/components/Badge'
import Icon from '../../../shared/components/Icon'
import { formatVietnamDateTime } from '../../../shared/utils/dateUtils'

export const TasksBoard = ({ onNavigateCustomer }) => {
  const { canCare } = useAuth()
  const [buckets, setBuckets] = useState({ today: [], overdue: [], upcoming: [] })
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState(null)
  const [viewAll, setViewAll] = useState(false)
  const [activeTab, setActiveTab] = useState('today') // 'today' | 'overdue' | 'upcoming'

  // Create reminder modal state
  const [createOpen, setCreateOpen] = useState(false)
  const [newTitle, setNewTitle] = useState('')
  const [newDue, setNewDue] = useState('')
  const [customerSearch, setCustomerSearch] = useState('')
  const [customerSearching, setCustomerSearching] = useState(false)
  const [selectedCustomerId, setSelectedCustomerId] = useState('')
  const [customers, setCustomers] = useState([])
  const [customerOpportunities, setCustomerOpportunities] = useState([])
  const [selectedOpportunityId, setSelectedOpportunityId] = useState('')
  const [saving, setSaving] = useState(false)

  useEffect(() => {
    loadBuckets()
  }, [viewAll])

  const loadBuckets = async () => {
    setLoading(true)
    setError(null)
    try {
      const data = await reminderApi.getBuckets({ all: viewAll })
      setBuckets({
        today: data?.today || [],
        overdue: data?.overdue || [],
        upcoming: data?.upcoming || [],
      })
    } catch (err) {
      setError(err?.response?.data?.message || err?.message || 'Không thể tải danh sách nhắc việc')
      setBuckets({ today: [], overdue: [], upcoming: [] })
    } finally {
      setLoading(false)
    }
  }

  const loadCustomers = async (kw = '') => {
    setCustomerSearching(true)
    try {
      const data = await customerApi.filter({
        keyword: kw?.trim() || null,
        index: 1,
        size: 20,
      })
      setCustomers(data?.items || data || [])
    } catch {
      setCustomers([])
    } finally {
      setCustomerSearching(false)
    }
  }

  useEffect(() => {
    if (!createOpen) return
    const timer = setTimeout(() => {
      loadCustomers(customerSearch)
    }, 200)
    return () => clearTimeout(timer)
  }, [customerSearch, createOpen])

  useEffect(() => {
    if (!selectedCustomerId) {
      setCustomerOpportunities([])
      setSelectedOpportunityId('')
      return
    }
    const loadOpps = async () => {
      try {
        const res = await opportunityApi.filter({
          customerId: selectedCustomerId,
          crmCustomerId: selectedCustomerId,
          size: 50,
        })
        setCustomerOpportunities(res?.items || [])
      } catch {
        setCustomerOpportunities([])
      }
    }
    loadOpps()
  }, [selectedCustomerId])

  const handleOpenCreate = () => {
    setCustomerSearch('')
    setSelectedCustomerId('')
    setSelectedOpportunityId('')
    setNewTitle('')
    setNewDue('')
    setCreateOpen(true)
    loadCustomers('')
  }

  const handleCreateReminder = async (e) => {
    e.preventDefault()
    if (!newTitle.trim() || !newDue || !selectedCustomerId || !canCare) return
    setSaving(true)
    try {
      await reminderApi.create({
        crmCustomerId: selectedCustomerId,
        crmOpportunityId: selectedOpportunityId || null,
        title: newTitle.trim(),
        dueAtUtc: new Date(newDue).toISOString(),
      })
      setNewTitle('')
      setNewDue('')
      setSelectedCustomerId('')
      setSelectedOpportunityId('')
      setCustomerSearch('')
      setCreateOpen(false)
      loadBuckets()
    } catch (err) {
      alert('Lỗi tạo nhắc việc: ' + (err?.response?.data?.message || err?.message))
    } finally {
      setSaving(false)
    }
  }

  const handleComplete = async (id) => {
    if (!canCare) return
    try {
      await reminderApi.complete(id)
      loadBuckets()
    } catch (err) {
      alert('Lỗi hoàn thành nhắc việc: ' + (err?.message || ''))
    }
  }

  const handleDelete = async (id) => {
    if (!canCare) return
    if (!window.confirm('Bạn có chắc muốn xoá nhắc việc này?')) return
    try {
      await reminderApi.delete(id)
      loadBuckets()
    } catch (err) {
      alert('Lỗi xoá nhắc việc: ' + (err?.message || ''))
    }
  }

  const currentList = buckets[activeTab] || []

  return (
    <div data-testid="tasks-board-page">
      {/* Page Header */}
      <div className="crm-page-header">
        <div>
          <h2 style={{ margin: 0, fontSize: '20px', fontWeight: '800' }}>Việc của tôi</h2>
          <p style={{ margin: '4px 0 0', fontSize: '14px', color: 'var(--crm-text-muted)' }}>
            Quản lý các mốc nhắc hẹn chăm sóc khách hàng (Hôm nay / Quá hạn / Sắp tới) theo giờ Việt Nam
          </p>
        </div>

        <div className="crm-header-actions">
          {canCare && (
            <Button
              variant="primary"
              icon={<Icon name="plus" size={16} />}
              onClick={handleOpenCreate}
              data-testid="btn-create-task"
            >
              Tạo nhắc việc
            </Button>
          )}
        </div>
      </div>

      {/* Filter Bar */}
      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '16px', flexWrap: 'wrap', gap: '12px' }}>
        {/* Bucket Tabs */}
        <div style={{ display: 'flex', gap: '8px' }} data-testid="reminder-bucket-tabs">
          <button
            type="button"
            className={`crm-filter-chip ${activeTab === 'today' ? 'active' : ''}`}
            onClick={() => setActiveTab('today')}
            data-testid="tab-bucket-today"
          >
            📅 Hôm nay ({buckets.today?.length || 0})
          </button>
          <button
            type="button"
            className={`crm-filter-chip ${activeTab === 'overdue' ? 'active' : ''}`}
            onClick={() => setActiveTab('overdue')}
            style={{ color: buckets.overdue?.length > 0 ? 'var(--crm-danger)' : undefined }}
            data-testid="tab-bucket-overdue"
          >
            ⚠️ Quá hạn ({buckets.overdue?.length || 0})
          </button>
          <button
            type="button"
            className={`crm-filter-chip ${activeTab === 'upcoming' ? 'active' : ''}`}
            onClick={() => setActiveTab('upcoming')}
            data-testid="tab-bucket-upcoming"
          >
            ⏰ Sắp tới ({buckets.upcoming?.length || 0})
          </button>
        </div>

        {/* View All Toggle */}
        <div style={{ display: 'flex', alignItems: 'center', gap: '8px' }}>
          <label style={{ fontSize: '13px', fontWeight: '600', color: 'var(--crm-text-muted)', cursor: 'pointer', display: 'flex', alignItems: 'center', gap: '6px' }}>
            <input
              type="checkbox"
              checked={viewAll}
              onChange={(e) => setViewAll(e.target.checked)}
              data-testid="checkbox-view-all-reminders"
            />
            Xem toàn bộ nhân viên
          </label>
        </div>
      </div>

      {/* Error state */}
      {error && (
        <div style={{ padding: '12px', background: 'var(--crm-danger-light)', color: 'var(--crm-danger-text)', borderRadius: 'var(--crm-radius-md)', marginBottom: '16px' }}>
          {error}
        </div>
      )}

      {/* Tasks List */}
      <div className="crm-tasks-list" data-testid="bucket-tasks-list">
        {loading && currentList.length === 0 ? (
          <div style={{ textAlign: 'center', padding: '32px', color: 'var(--crm-text-muted)' }} data-testid="tasks-loading">
            Đang tải danh sách việc cần làm...
          </div>
        ) : currentList.length === 0 ? (
          <div style={{ textAlign: 'center', padding: '40px', background: 'var(--crm-surface)', border: '1px solid var(--crm-border)', borderRadius: 'var(--crm-radius-lg)', color: 'var(--crm-text-muted)' }} data-testid="tasks-empty">
            🎉 Không có việc nào trong mục này!
          </div>
        ) : (
          currentList.map((task) => (
            <div
              key={task.id}
              className={`crm-task-card ${task.isCompleted ? 'completed' : ''}`}
              data-testid={`reminder-card-${task.id}`}
            >
              <div className="crm-task-left">
                <div>
                  <h4
                    className="crm-task-title"
                    style={{ textDecoration: task.isCompleted ? 'line-through' : 'none', fontSize: '15px' }}
                    data-testid={`reminder-title-${task.id}`}
                  >
                    {task.title}
                  </h4>
                  <div className="crm-task-meta" style={{ marginTop: '6px', fontSize: '13px' }}>
                    <span>
                      Khách hàng:{' '}
                      <strong
                        style={{ cursor: onNavigateCustomer ? 'pointer' : 'default', color: onNavigateCustomer ? 'var(--crm-primary)' : 'inherit' }}
                        onClick={() => onNavigateCustomer && onNavigateCustomer(task.crmCustomerId)}
                        data-testid={`reminder-customer-${task.id}`}
                      >
                        {task.customerName || 'Khách hàng'}
                      </strong>
                    </span>
                    <span>•</span>
                    <span>
                      Hạn chót: <strong>{formatVietnamDateTime(task.dueAtUtc)}</strong>
                    </span>
                    {task.opportunityTitle && (
                      <>
                        <span>•</span>
                        <span
                          className="crm-reminder-opp-badge"
                          data-testid={`reminder-opportunity-${task.id}`}
                          style={{
                            display: 'inline-flex',
                            alignItems: 'center',
                            gap: '4px',
                            color: 'var(--crm-primary)',
                            fontWeight: '600',
                            fontSize: '12px',
                          }}
                        >
                          🎯 {task.opportunityTitle}
                        </span>
                      </>
                    )}
                    {task.isCompleted && (
                      <Badge variant="success" size="sm">Đã xong</Badge>
                    )}
                    {activeTab === 'overdue' && !task.isCompleted && (
                      <Badge variant="danger" size="sm">Quá hạn</Badge>
                    )}
                  </div>
                </div>
              </div>

              {canCare && (
                <div style={{ display: 'flex', gap: '8px', alignItems: 'center' }}>
                  <Button
                    variant={task.isCompleted ? 'secondary' : 'primary'}
                    size="sm"
                    onClick={() => handleComplete(task.id)}
                    data-testid={task.id === 't-1' ? 'btn-toggle-task-t-1' : `btn-complete-task-${task.id}`}
                  >
                    {task.isCompleted ? 'Đã xong ✓' : 'Hoàn thành'}
                  </Button>
                  <Button
                    variant="ghost"
                    size="sm"
                    style={{ color: 'var(--crm-danger)' }}
                    onClick={() => handleDelete(task.id)}
                    data-testid={`btn-delete-task-${task.id}`}
                  >
                    Xoá
                  </Button>
                </div>
              )}
            </div>
          ))
        )}
      </div>

      {/* Create Reminder Modal */}
      {createOpen && (
        <div
          style={{
            position: 'fixed',
            inset: 0,
            backgroundColor: 'rgba(0,0,0,0.5)',
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'center',
            zIndex: 1000,
            padding: '16px',
          }}
          data-testid="create-reminder-modal"
        >
          <div style={{ background: '#ffffff', borderRadius: 'var(--crm-radius-lg)', padding: '24px', maxWidth: '440px', width: '100%' }}>
            <h3 style={{ margin: '0 0 16px', fontSize: '18px', fontWeight: '700' }}>Tạo việc cần làm / nhắc hẹn mới</h3>
            <form onSubmit={handleCreateReminder}>
              <div style={{ marginBottom: '12px' }}>
                <label style={{ display: 'block', fontSize: '13px', fontWeight: '600', marginBottom: '4px' }}>
                  Khách hàng <span style={{ color: 'var(--crm-danger)' }}>*</span>
                </label>
                <input
                  type="text"
                  className="crm-form-input"
                  placeholder="Tìm khách theo tên, SĐT..."
                  value={customerSearch}
                  onChange={(e) => setCustomerSearch(e.target.value)}
                  data-testid="input-customer-search"
                  style={{ marginBottom: '6px' }}
                />
                <select
                  className="crm-form-input"
                  required
                  value={selectedCustomerId}
                  onChange={(e) => setSelectedCustomerId(e.target.value)}
                  data-testid="select-reminder-customer"
                >
                  <option value="">-- Chọn khách hàng --</option>
                  {customers.map((c) => (
                    <option key={c.id} value={c.id}>
                      {c.displayName || c.name} {c.phoneE164 ? `(${c.phoneE164})` : ''}
                    </option>
                  ))}
                </select>
                {customerSearching && (
                  <small style={{ color: 'var(--crm-text-muted)', fontSize: '11px' }}>Đang tìm khách hàng...</small>
                )}
              </div>

              {/* Gắn cơ hội (tuỳ chọn) */}
              <div style={{ marginBottom: '12px' }}>
                <label style={{ display: 'block', fontSize: '13px', fontWeight: '600', marginBottom: '4px' }}>
                  Gắn vào cơ hội (tuỳ chọn)
                </label>
                <select
                  className="crm-form-input"
                  value={selectedOpportunityId}
                  onChange={(e) => setSelectedOpportunityId(e.target.value)}
                  data-testid="select-reminder-opportunity"
                  disabled={!selectedCustomerId || customerOpportunities.length === 0}
                >
                  <option value="">
                    {customerOpportunities.length === 0
                      ? selectedCustomerId ? '-- Không có cơ hội nào --' : '-- Chọn khách hàng trước --'
                      : '-- Không gắn cơ hội --'}
                  </option>
                  {customerOpportunities.map((opp) => (
                    <option key={opp.id} value={opp.id}>
                      {opp.title}
                    </option>
                  ))}
                </select>
              </div>

              <div style={{ marginBottom: '12px' }}>
                <label style={{ display: 'block', fontSize: '13px', fontWeight: '600', marginBottom: '4px' }}>Tiêu đề công việc</label>
                <input
                  type="text"
                  className="crm-form-input"
                  required
                  placeholder="Ví dụ: Gọi điện chốt học phí..."
                  value={newTitle}
                  onChange={(e) => setNewTitle(e.target.value)}
                  data-testid="input-new-reminder-title"
                />
              </div>

              <div style={{ marginBottom: '20px' }}>
                <label style={{ display: 'block', fontSize: '13px', fontWeight: '600', marginBottom: '4px' }}>Hạn chót (Giờ & ngày)</label>
                <input
                  type="datetime-local"
                  className="crm-form-input"
                  required
                  value={newDue}
                  onChange={(e) => setNewDue(e.target.value)}
                  data-testid="input-new-reminder-due"
                />
              </div>

              <div style={{ display: 'flex', justifyContent: 'flex-end', gap: '8px' }}>
                <Button variant="ghost" onClick={() => setCreateOpen(false)}>Huỷ</Button>
                <Button type="submit" variant="primary" isLoading={saving} data-testid="btn-save-reminder">
                  Lưu nhắc việc
                </Button>
              </div>
            </form>
          </div>
        </div>
      )}
    </div>
  )
}

export default TasksBoard
