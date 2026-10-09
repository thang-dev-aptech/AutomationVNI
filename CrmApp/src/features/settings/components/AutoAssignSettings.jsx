import React, { useState, useEffect, useCallback } from 'react'
import { autoAssignApi, usersApi } from '../api/settingsApi'
import Button from '../../../shared/components/Button'
import Badge from '../../../shared/components/Badge'
import Icon from '../../../shared/components/Icon'

export const AutoAssignSettings = ({ canManage = false }) => {
  const [loading, setLoading] = useState(() => Boolean(canManage))
  const [submitting, setSubmitting] = useState(false)
  const [error, setError] = useState(null)
  const [successMessage, setSuccessMessage] = useState(null)

  const [isEnabled, setIsEnabled] = useState(false)
  const [assigneeUserIds, setAssigneeUserIds] = useState([])
  const [nextIndex, setNextIndex] = useState(0)
  const [users, setUsers] = useState([])
  const [searchUser, setSearchUser] = useState('')

  const loadData = useCallback(async () => {
    try {
      setLoading(true)
      setError(null)
      const [settingsRes, usersRes] = await Promise.all([
        autoAssignApi.get(),
        usersApi.list(),
      ])

      if (settingsRes) {
        setIsEnabled(Boolean(settingsRes.isEnabled))
        setAssigneeUserIds(Array.isArray(settingsRes.assigneeUserIds) ? settingsRes.assigneeUserIds : [])
        setNextIndex(settingsRes.nextIndex ?? 0)
      }

      if (Array.isArray(usersRes)) {
        setUsers(usersRes)
      }
    } catch (err) {
      if (process.env.NODE_ENV !== 'test') {
        console.error('Failed to load auto-assign settings', err)
      }
      setError(err?.response?.data?.message || 'Không thể tải cấu hình tự chia hoặc danh sách người dùng')
    } finally {
      setLoading(false)
    }
  }, [])

  useEffect(() => {
    if (canManage) {
      loadData()
    }
  }, [canManage, loadData])

  const handleToggleUser = (userId) => {
    setAssigneeUserIds((prev) => {
      if (prev.includes(userId)) {
        return prev.filter((id) => id !== userId)
      } else {
        return [...prev, userId]
      }
    })
  }

  const handleSelectAll = () => {
    setAssigneeUserIds(users.map((u) => u.id))
  }

  const handleDeselectAll = () => {
    setAssigneeUserIds([])
  }

  const handleSave = async () => {
    if (!canManage || submitting) return

    try {
      setSubmitting(true)
      setError(null)
      setSuccessMessage(null)

      const payload = {
        isEnabled,
        assigneeUserIds,
      }

      const res = await autoAssignApi.update(payload)
      if (res) {
        if (typeof res.nextIndex === 'number') {
          setNextIndex(res.nextIndex)
        }
      }

      setSuccessMessage('Đã lưu cấu hình tự chia thành công!')
      setTimeout(() => setSuccessMessage(null), 4000)
    } catch (err) {
      console.error('Failed to update auto-assign settings', err)
      setError(err?.response?.data?.message || 'Lỗi khi lưu cấu hình tự chia')
    } finally {
      setSubmitting(false)
    }
  }

  if (!canManage) {
    return (
      <div
        style={{
          padding: '16px',
          borderRadius: 'var(--crm-radius-md)',
          background: 'var(--crm-warning-light)',
          color: 'var(--crm-warning-text)',
          fontSize: '14px',
        }}
        data-testid="routing-permission-denied"
      >
        <Icon name="lock" size={16} /> Bạn không có quyền cấu hình Tự chia khách hàng. Tính năng này chỉ dành cho Admin và ContentManager.
      </div>
    )
  }

  const filteredUsers = users.filter((u) => {
    const q = searchUser.trim().toLowerCase()
    if (!q) return true
    return (
      u.displayName?.toLowerCase().includes(q) ||
      u.userName?.toLowerCase().includes(q) ||
      u.roles?.some((r) => r.toLowerCase().includes(q))
    )
  })

  return (
    <div>
      <h3 style={{ margin: '0 0 8px', fontSize: '17px', fontWeight: '700' }}>Quy tắc tự chia khách hàng & hội thoại</h3>
      <p style={{ margin: '0 0 16px', fontSize: '13px', color: 'var(--crm-text-muted)' }}>
        Tự động phân phối hội thoại mới cho đội ngũ trực ca theo cơ chế xoay vòng đều (Round Robin).
      </p>

      {successMessage && (
        <div
          style={{
            padding: '12px 16px',
            marginBottom: '16px',
            borderRadius: 'var(--crm-radius-md)',
            background: 'var(--crm-success-light, #ecfdf5)',
            color: 'var(--crm-success-text, #065f46)',
            fontSize: '13px',
            fontWeight: '600',
            display: 'flex',
            alignItems: 'center',
            gap: '8px',
          }}
          data-testid="auto-assign-success"
        >
          <Icon name="check" size={14} /> {successMessage}
        </div>
      )}

      {error && (
        <div
          style={{
            padding: '12px 16px',
            marginBottom: '16px',
            borderRadius: 'var(--crm-radius-md)',
            background: 'var(--crm-danger-light, #fef2f2)',
            color: 'var(--crm-danger, #ef4444)',
            fontSize: '13px',
            display: 'flex',
            alignItems: 'center',
            gap: '8px',
          }}
          data-testid="auto-assign-error"
        >
          <Icon name="alert" size={14} /> {error}
        </div>
      )}

      {loading && (
        <div style={{ padding: '8px 12px', marginBottom: '12px', color: 'var(--crm-text-muted)', fontSize: '13px' }}>
          Đang đồng bộ cấu hình tự chia...
        </div>
      )}

      <div style={{ display: 'flex', flexDirection: 'column', gap: '20px', maxWidth: '640px' }}>
          {/* Card Bật/Tắt tự chia */}
          <div
            style={{
              padding: '16px',
              border: '1px solid var(--crm-border)',
              borderRadius: 'var(--crm-radius-md)',
              background: 'var(--crm-surface)',
              display: 'flex',
              alignItems: 'flex-start',
              gap: '12px',
            }}
          >
            <input
              type="checkbox"
              id="toggle-auto-assign"
              checked={isEnabled}
              onChange={(e) => setIsEnabled(e.target.checked)}
              data-testid="toggle-auto-assign"
              style={{
                width: '18px',
                height: '18px',
                marginTop: '3px',
                cursor: 'pointer',
                accentColor: 'var(--crm-primary)',
              }}
            />
            <label htmlFor="toggle-auto-assign" style={{ cursor: 'pointer', flex: 1 }}>
              <div style={{ fontWeight: '700', fontSize: '15px', color: 'var(--crm-text)' }}>
                Bật tự động phân chia hội thoại (Round Robin)
              </div>
              <div style={{ fontSize: '13px', color: 'var(--crm-text-muted)', marginTop: '4px', lineHeight: 1.5 }}>
                Khi bật, mọi hội thoại tin nhắn hoặc bình luận mới chưa có người phụ trách sẽ tự động được chia đều luân
                phiên cho các nhân sự được chọn bên dưới.
              </div>
            </label>
          </div>

          {/* Quy tắc và trạng thái con trỏ */}
          <div
            style={{
              padding: '14px',
              borderRadius: 'var(--crm-radius-md)',
              background: 'var(--crm-surface-subtle)',
              border: '1px solid var(--crm-border)',
              fontSize: '13px',
              color: 'var(--crm-text)',
            }}
          >
            <div style={{ fontWeight: '600', marginBottom: '4px', display: 'flex', alignItems: 'center', gap: '6px' }}>
              <Icon name="info" size={16} /> Nguyên tắc hoạt động:
            </div>
            <ul style={{ margin: 0, paddingLeft: '20px', color: 'var(--crm-text-muted)' }}>
              <li>Chỉ gán hội thoại MỚI chưa có người phụ trách.</li>
              <li>Hội thoại đã có người phụ trách sẽ KHÔNG bị ghi đè.</li>
              <li>Tự động bỏ qua người dùng bị vô hiệu hóa hoặc không còn hoạt động.</li>
              <li>Mỗi lần phân chia đều ghi log kiểm toán hành động của hệ thống.</li>
            </ul>
            {assigneeUserIds.length > 0 && (
              <div style={{ marginTop: '8px', fontSize: '12px', color: 'var(--crm-primary)', fontWeight: '600' }}>
                Lượt phân công tiếp theo: Vị trí #{((nextIndex || 0) % assigneeUserIds.length) + 1} / {assigneeUserIds.length}
              </div>
            )}
          </div>

          {/* Danh sách nhân sự tham gia nhận hội thoại */}
          <div>
            <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', marginBottom: '8px' }}>
              <h4 style={{ margin: 0, fontSize: '14px', fontWeight: '700' }}>
                Danh sách người nhận phân công ({assigneeUserIds.length}/{users.length})
              </h4>
              <div style={{ display: 'flex', gap: '8px' }}>
                <button
                  type="button"
                  onClick={handleSelectAll}
                  style={{
                    background: 'none',
                    border: 'none',
                    color: 'var(--crm-primary)',
                    fontSize: '12px',
                    fontWeight: '600',
                    cursor: 'pointer',
                    padding: 0,
                  }}
                  data-testid="btn-select-all-users"
                >
                  Chọn tất cả
                </button>
                <span style={{ color: 'var(--crm-border)' }}>|</span>
                <button
                  type="button"
                  onClick={handleDeselectAll}
                  style={{
                    background: 'none',
                    border: 'none',
                    color: 'var(--crm-text-muted)',
                    fontSize: '12px',
                    cursor: 'pointer',
                    padding: 0,
                  }}
                  data-testid="btn-deselect-all-users"
                >
                  Bỏ chọn
                </button>
              </div>
            </div>

            {users.length > 5 && (
              <input
                type="text"
                placeholder="Tìm nhân sự theo tên hoặc vai trò..."
                value={searchUser}
                onChange={(e) => setSearchUser(e.target.value)}
                className="crm-form-input"
                style={{ marginBottom: '10px', fontSize: '13px' }}
                data-testid="input-search-user"
              />
            )}

            <div
              style={{
                maxHeight: '300px',
                overflowY: 'auto',
                border: '1px solid var(--crm-border)',
                borderRadius: 'var(--crm-radius-md)',
                background: 'var(--crm-surface)',
              }}
              data-testid="assignees-checklist"
            >
              {filteredUsers.length === 0 ? (
                <div style={{ padding: '20px', textAlign: 'center', color: 'var(--crm-text-muted)', fontSize: '13px' }}>
                  {users.length === 0 ? 'Chưa có người dùng hoạt động trong hệ thống.' : 'Không tìm thấy người dùng phù hợp.'}
                </div>
              ) : (
                filteredUsers.map((u) => {
                  const isChecked = assigneeUserIds.includes(u.id)
                  return (
                    <label
                      key={u.id}
                      style={{
                        display: 'flex',
                        alignItems: 'center',
                        justifyContent: 'space-between',
                        padding: '10px 14px',
                        borderBottom: '1px solid var(--crm-border)',
                        cursor: 'pointer',
                        background: isChecked ? 'var(--crm-surface-subtle)' : 'transparent',
                        transition: 'background var(--crm-transition-fast)',
                      }}
                      data-testid={`user-item-${u.id}`}
                    >
                      <div style={{ display: 'flex', alignItems: 'center', gap: '10px' }}>
                        <input
                          type="checkbox"
                          checked={isChecked}
                          onChange={() => handleToggleUser(u.id)}
                          data-testid={`user-checkbox-${u.id}`}
                          style={{
                            width: '16px',
                            height: '16px',
                            cursor: 'pointer',
                            accentColor: 'var(--crm-primary)',
                          }}
                        />
                        <div>
                          <div style={{ fontSize: '13px', fontWeight: '600', color: 'var(--crm-text)' }}>
                            {u.displayName || u.userName}
                          </div>
                          {u.userName && u.displayName && u.userName !== u.displayName && (
                            <div style={{ fontSize: '11px', color: 'var(--crm-text-muted)' }}>
                              @{u.userName}
                            </div>
                          )}
                        </div>
                      </div>

                      <div style={{ display: 'flex', gap: '6px' }}>
                        {u.roles?.map((r) => (
                          <Badge key={r} variant={r === 'Admin' ? 'primary' : 'secondary'} size="sm">
                            {r}
                          </Badge>
                        ))}
                      </div>
                    </label>
                  )
                })
              )}
            </div>
          </div>

          {/* Nút lưu cấu hình */}
          <div style={{ paddingTop: '8px' }}>
            <Button
              type="button"
              variant="primary"
              onClick={handleSave}
              disabled={submitting}
              isLoading={submitting}
              data-testid="btn-save-routing"
            >
              Lưu cấu hình
            </Button>
          </div>
        </div>
    </div>
  )
}

export default AutoAssignSettings
