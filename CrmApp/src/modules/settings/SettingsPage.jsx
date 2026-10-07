import React, { useState } from 'react'
import { useAuth } from '../../auth/useAuth'
import Button from '../../shared/components/Button'
import Badge from '../../shared/components/Badge'
import './SettingsPage.css'

export const SettingsPage = () => {
  const { user, roles, canManage } = useAuth()
  const [activeTab, setActiveTab] = useState('profile')
  const [tags, setTags] = useState(['Quan tâm kế toán', 'Khóa K45', 'Hà Nội', 'VIP', 'Chờ gọi lại'])
  const [newTag, setNewTag] = useState('')

  const handleAddTag = (e) => {
    e.preventDefault()
    if (!newTag.trim() || !canManage) return
    setTags([...tags, newTag.trim()])
    setNewTag('')
  }

  const handleRemoveTag = (tag) => {
    if (!canManage) return
    setTags(tags.filter((t) => t !== tag))
  }

  return (
    <div>
      <div className="crm-page-header">
        <div>
          <h2 style={{ margin: 0, fontSize: '20px', fontWeight: '800' }}>Cài đặt hệ thống</h2>
          <p style={{ margin: '4px 0 0', fontSize: '14px', color: 'var(--crm-text-muted)' }}>
            Quản trị tài khoản, cấu hình nhãn và quy tắc phân chia khách hàng
          </p>
        </div>
      </div>

      <div className="crm-settings-grid">
        {/* Navigation tabs */}
        <div className="crm-settings-menu">
          <button
            type="button"
            className={`crm-settings-tab ${activeTab === 'profile' ? 'active' : ''}`}
            onClick={() => setActiveTab('profile')}
            data-testid="tab-profile"
          >
            Thông tin tài khoản
          </button>

          <button
            type="button"
            className={`crm-settings-tab ${activeTab === 'tags' ? 'active' : ''}`}
            onClick={() => setActiveTab('tags')}
            data-testid="tab-tags"
          >
            Cấu hình Tag {canManage ? '' : '🔒'}
          </button>

          <button
            type="button"
            className={`crm-settings-tab ${activeTab === 'routing' ? 'active' : ''}`}
            onClick={() => setActiveTab('routing')}
            data-testid="tab-routing"
          >
            Phân chia tự động {canManage ? '' : '🔒'}
          </button>
        </div>

        {/* Content */}
        <div className="crm-settings-card">
          {activeTab === 'profile' && (
            <div data-testid="settings-profile-pane">
              <h3 style={{ margin: '0 0 16px', fontSize: '17px', fontWeight: '700' }}>Hồ sơ nhân sự CRM</h3>
              <div style={{ display: 'flex', flexDirection: 'column', gap: '14px', maxWidth: '480px' }}>
                <div>
                  <label className="crm-form-label">Email tài khoản:</label>
                  <input type="text" className="crm-form-input" disabled value={user?.email || ''} />
                </div>
                <div>
                  <label className="crm-form-label">Vai trò trong hệ thống:</label>
                  <div style={{ display: 'flex', gap: '8px', marginTop: '6px' }}>
                    {roles.map((r) => (
                      <Badge key={r} variant="primary" size="md">
                        {r}
                      </Badge>
                    ))}
                  </div>
                </div>
                <div>
                  <label className="crm-form-label">Quyền hạn:</label>
                  <p style={{ fontSize: '13px', color: 'var(--crm-text-muted)', margin: '4px 0 0' }}>
                    {canManage
                      ? 'Toàn quyền quản trị: Quản lý khách hàng, cấu hình tag, tự chia, nhập CSV & xuất dữ liệu.'
                      : roles.includes('Reviewer')
                        ? 'Chăm sóc khách hàng: Trả lời, ghi chú, trạng thái, nhắc việc, tạo khách tay, gộp hồ sơ.'
                        : 'Chỉ đọc (Viewer): Xem danh sách khách hàng và hội thoại.'}
                  </p>
                </div>
              </div>
            </div>
          )}

          {activeTab === 'tags' && (
            <div data-testid="settings-tags-pane">
              <h3 style={{ margin: '0 0 8px', fontSize: '17px', fontWeight: '700' }}>Cấu hình danh mục Tag</h3>
              <p style={{ margin: '0 0 16px', fontSize: '13px', color: 'var(--crm-text-muted)' }}>
                Các nhãn phân loại khách hàng phục vụ chiến dịch và tự động gắn kênh.
              </p>

              {canManage ? (
                <>
                  <form onSubmit={handleAddTag} style={{ display: 'flex', gap: '8px', marginBottom: '20px', maxWidth: '420px' }}>
                    <input
                      type="text"
                      className="crm-form-input"
                      placeholder="Thêm nhãn tag mới..."
                      value={newTag}
                      onChange={(e) => setNewTag(e.target.value)}
                      data-testid="input-new-tag"
                    />
                    <Button type="submit" variant="primary" data-testid="btn-add-tag">
                      Thêm
                    </Button>
                  </form>

                  <div style={{ display: 'flex', flexWrap: 'wrap', gap: '8px' }} data-testid="tags-list">
                    {tags.map((t) => (
                      <span
                        key={t}
                        style={{
                          display: 'inline-flex',
                          alignItems: 'center',
                          gap: '6px',
                          padding: '6px 12px',
                          borderRadius: 'var(--crm-radius-full)',
                          background: 'var(--crm-surface-subtle)',
                          border: '1px solid var(--crm-border)',
                          fontSize: '13px',
                          fontWeight: '600',
                        }}
                      >
                        {t}
                        <button
                          type="button"
                          onClick={() => handleRemoveTag(t)}
                          style={{
                            background: 'none',
                            border: 'none',
                            cursor: 'pointer',
                            color: 'var(--crm-danger)',
                            fontSize: '14px',
                            padding: '0 2px',
                          }}
                          data-testid={`btn-remove-tag-${t}`}
                        >
                          ×
                        </button>
                      </span>
                    ))}
                  </div>
                </>
              ) : (
                <div
                  style={{
                    padding: '16px',
                    borderRadius: 'var(--crm-radius-md)',
                    background: 'var(--crm-warning-light)',
                    color: 'var(--crm-warning-text)',
                    fontSize: '14px',
                  }}
                  data-testid="tags-permission-denied"
                >
                  🔒 Bạn không có quyền cấu hình Tag. Tính năng này chỉ dành cho Admin và ContentManager.
                </div>
              )}
            </div>
          )}

          {activeTab === 'routing' && (
            <div data-testid="settings-routing-pane">
              <h3 style={{ margin: '0 0 8px', fontSize: '17px', fontWeight: '700' }}>Quy tắc tự chia khách hàng</h3>
              <p style={{ margin: '0 0 16px', fontSize: '13px', color: 'var(--crm-text-muted)' }}>
                Tự động gán khách hàng mới về nhân viên theo kênh hoặc cơ sở.
              </p>

              {canManage ? (
                <div style={{ display: 'flex', flexDirection: 'column', gap: '12px', maxWidth: '500px' }}>
                  <div style={{ padding: '14px', border: '1px solid var(--crm-border)', borderRadius: 'var(--crm-radius-md)' }}>
                    <div style={{ fontWeight: '700', fontSize: '14px' }}>Chia xoay vòng (Round Robin)</div>
                    <div style={{ fontSize: '13px', color: 'var(--crm-text-muted)', marginTop: '4px' }}>
                      Chia đều cho các nhân sự có ca trực đang online.
                    </div>
                  </div>
                  <Button variant="primary" style={{ alignSelf: 'flex-start' }} data-testid="btn-save-routing">
                    Lưu cấu hình
                  </Button>
                </div>
              ) : (
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
                  🔒 Bạn không có quyền cấu hình Tự chia khách hàng. Tính năng này chỉ dành cho Admin và ContentManager.
                </div>
              )}
            </div>
          )}
        </div>
      </div>
    </div>
  )
}

export default SettingsPage
