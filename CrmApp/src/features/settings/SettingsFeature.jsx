import React, { useState } from 'react'
import { useAuth } from '../../auth/useAuth'
import Badge from '../../shared/components/Badge'
import Icon from '../../shared/components/Icon'
import TagsSettings from './components/TagsSettings'
import AutoAssignSettings from './components/AutoAssignSettings'
import './SettingsFeature.css'

export const SettingsFeature = () => {
  const { user, roles, canManage } = useAuth()
  const [activeTab, setActiveTab] = useState('profile')

  return (
    <div>
      <div className="crm-page-header">
        <div>
          <h2 style={{ margin: 0, fontSize: '20px', fontWeight: '800' }}>Cài đặt hệ thống</h2>
          <p style={{ margin: '4px 0 0', fontSize: '14px', color: 'var(--crm-text-muted)' }}>
            Quản trị tài khoản, cấu hình thẻ phân loại và quy tắc phân chia hội thoại
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
            Cấu hình Tag {!canManage && <Icon name="lock" size={14} />}
          </button>

          <button
            type="button"
            className={`crm-settings-tab ${activeTab === 'routing' ? 'active' : ''}`}
            onClick={() => setActiveTab('routing')}
            data-testid="tab-routing"
          >
            Phân chia tự động {!canManage && <Icon name="lock" size={14} />}
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
                  <label className="crm-form-label">Tên hiển thị:</label>
                  <input type="text" className="crm-form-input" disabled value={user?.userName || user?.displayName || ''} />
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
              <TagsSettings canManage={canManage} />
            </div>
          )}

          {activeTab === 'routing' && (
            <div data-testid="settings-routing-pane">
              <AutoAssignSettings canManage={canManage} />
            </div>
          )}
        </div>
      </div>
    </div>
  )
}

export default SettingsFeature
