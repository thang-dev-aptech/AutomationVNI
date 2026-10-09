import React, { useState, useEffect } from 'react'
import { customerApi } from '../api/customerApi'
import { useAuth } from '../../../auth/useAuth'
import Button from '../../../shared/components/Button'
import Badge from '../../../shared/components/Badge'
import Icon from '../../../shared/components/Icon'
import { formatVietnamDateTime } from '../../../shared/utils/dateUtils'
import MergeModal from './MergeModal'

export const CustomerProfile = ({ customerId, onBack, onCustomerUpdated }) => {
  const { canCare, canManage, isAdmin } = useAuth()
  const [customer, setCustomer] = useState(null)
  const [timeline, setTimeline] = useState([])
  const [notes, setNotes] = useState([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState(null)

  // Active sub-tab
  const [activeTab, setActiveTab] = useState('timeline') // 'timeline' | 'notes' | 'identities'

  // Edit info modal
  const [editOpen, setEditOpen] = useState(false)
  const [editName, setEditName] = useState('')
  const [editPhone, setEditPhone] = useState('')

  // Add note state
  const [newNoteBody, setNewNoteBody] = useState('')
  const [savingNote, setSavingNote] = useState(false)

  // Merge modal state
  const [mergeModalOpen, setMergeModalOpen] = useState(false)

  useEffect(() => {
    if (customerId) {
      loadCustomerData()
    }
  }, [customerId])

  const loadCustomerData = async () => {
    setLoading(true)
    setError(null)
    try {
      const [detailData, timelineData, notesData] = await Promise.all([
        customerApi.get(customerId),
        customerApi.getTimeline(customerId),
        customerApi.listNotes(customerId),
      ])

      setCustomer(detailData)
      setEditName(detailData.displayName || '')
      setEditPhone(detailData.phoneE164 || '')
      setTimeline(timelineData || [])
      setNotes(notesData || [])
    } catch (err) {
      const status = err?.response?.status
      setError(status === 404 || status === 400
        ? 'Không tìm thấy khách'
        : err?.response?.data?.message || err?.message || 'Không thể tải thông tin khách hàng')
    } finally {
      setLoading(false)
    }
  }

  // Confirm Phone Suggestion
  const handleConfirmPhone = async (suggestion) => {
    if (!canCare) return
    try {
      await customerApi.confirmPhone(customerId, {
        suggestionId: suggestion.id,
        phoneE164: suggestion.phoneE164,
      })
      alert(`Đã xác nhận số điện thoại: ${suggestion.phoneE164}`)
      loadCustomerData()
      if (onCustomerUpdated) onCustomerUpdated()
    } catch (err) {
      alert('Lỗi xác nhận số: ' + (err?.response?.data?.message || err?.message))
    }
  }

  // Update Customer Info
  const handleUpdateCustomer = async (e) => {
    e.preventDefault()
    if (!canCare) return
    try {
      await customerApi.update(customerId, {
        displayName: editName.trim(),
        phoneE164: editPhone.trim() || null,
      })
      setEditOpen(false)
      loadCustomerData()
      if (onCustomerUpdated) onCustomerUpdated()
    } catch (err) {
      alert('Lỗi cập nhật: ' + (err?.response?.data?.message || err?.message))
    }
  }

  // Soft Delete
  const handleSoftDelete = async () => {
    if (!canManage) return
    if (!window.confirm('Bạn có chắc chắn muốn xoá mềm khách hàng này khỏi danh sách?')) return
    try {
      await customerApi.softDelete(customerId)
      alert('Đã xoá mềm khách hàng')
      if (onBack) onBack()
      if (onCustomerUpdated) onCustomerUpdated()
    } catch (err) {
      alert('Lỗi xoá mềm: ' + (err?.response?.data?.message || err?.message))
    }
  }

  // Hard Delete (Admin Only)
  const handleHardDelete = async () => {
    if (!isAdmin) return
    if (!window.confirm('CẢNH BÁO: Bạn đang thực hiện xoá hẳn dữ liệu cá nhân của khách hàng (bao gồm mọi danh tính, ghi chú và dữ liệu chăm sóc liên quan). Hành động này không thể hoàn tác! Bạn có chắc chắn?')) return
    try {
      await customerApi.hardDelete(customerId)
      alert('Đã xoá vĩnh viễn dữ liệu khách hàng')
      if (onBack) onBack()
      if (onCustomerUpdated) onCustomerUpdated()
    } catch (err) {
      alert('Lỗi xoá hẳn: ' + (err?.response?.data?.message || err?.message))
    }
  }

  // Add Note
  const handleAddNote = async (e) => {
    e.preventDefault()
    if (!newNoteBody.trim() || !canCare) return
    setSavingNote(true)
    try {
      await customerApi.addNote(customerId, { body: newNoteBody.trim() })
      setNewNoteBody('')
      const updatedNotes = await customerApi.listNotes(customerId)
      setNotes(updatedNotes || [])
      // Refresh timeline too
      const updatedTimeline = await customerApi.getTimeline(customerId)
      setTimeline(updatedTimeline || [])
    } catch (err) {
      alert('Lỗi thêm ghi chú: ' + (err?.message || ''))
    } finally {
      setSavingNote(false)
    }
  }

  // Delete Note
  const handleDeleteNote = async (noteId) => {
    if (!canCare) return
    if (!window.confirm('Bạn có chắc muốn xoá ghi chú này?')) return
    try {
      await customerApi.deleteNote(customerId, noteId)
      setNotes(notes.filter((n) => n.id !== noteId))
    } catch (err) {
      alert('Lỗi xoá ghi chú: ' + (err?.message || ''))
    }
  }

  if (loading) {
    return <div style={{ padding: '32px', textAlign: 'center' }}>Đang tải hồ sơ khách hàng...</div>
  }

  if (error || !customer) {
    return (
      <div style={{ padding: '24px' }}>
        <Button variant="ghost" onClick={onBack} icon={<Icon name="inbox" size={16} />} data-testid="btn-profile-back-list">
          Quay lại danh sách
        </Button>
        <div style={{ marginTop: '16px', color: 'var(--crm-danger)', fontWeight: '600' }} data-testid="customer-profile-error">
          {error || 'Không tìm thấy khách'}
        </div>
      </div>
    )
  }

  return (
    <div data-testid="customer-profile-page">
      {/* Header Bar */}
      <div style={{ marginBottom: '20px' }}>
        <Button
          variant="ghost"
          size="sm"
          onClick={onBack}
          style={{ marginBottom: '12px' }}
          data-testid="btn-profile-back"
        >
          ← Quay lại danh sách
        </Button>

        <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-start', flexWrap: 'wrap', gap: '16px' }}>
          <div>
            <div style={{ display: 'flex', alignItems: 'center', gap: '12px' }}>
              <h2 style={{ margin: 0, fontSize: '24px', fontWeight: '800' }} data-testid="profile-display-name">
                {customer.displayName}
              </h2>
              <Badge variant={customer.phoneE164 ? 'success' : 'default'} size="md">
                {customer.phoneE164 ? customer.phoneE164 : 'Chưa có SĐT'}
              </Badge>
            </div>
            <p style={{ margin: '6px 0 0', fontSize: '13px', color: 'var(--crm-text-muted)' }}>
              Mã khách hàng: <code>{customer.id}</code> • Ngày tạo: {formatVietnamDateTime(customer.createdAt)}
            </p>
          </div>

          <div style={{ display: 'flex', gap: '8px', flexWrap: 'wrap' }} data-testid="profile-action-buttons">
            {canCare && (
              <>
                <Button
                  variant="secondary"
                  size="sm"
                  onClick={() => setEditOpen(true)}
                  data-testid="btn-edit-profile"
                >
                  Sửa thông tin
                </Button>
                <Button
                  variant="outline"
                  size="sm"
                  onClick={() => setMergeModalOpen(true)}
                  data-testid="btn-open-merge-modal"
                >
                  Gộp hồ sơ
                </Button>
              </>
            )}

            {canManage && (
              <Button
                variant="danger"
                size="sm"
                onClick={handleSoftDelete}
                data-testid="btn-soft-delete-profile"
              >
                Xoá mềm
              </Button>
            )}

            {isAdmin && (
              <Button
                variant="danger"
                size="sm"
                onClick={handleHardDelete}
                style={{ backgroundColor: '#991b1b' }}
                data-testid="btn-hard-delete-profile"
              >
                Xoá hẳn dữ liệu
              </Button>
            )}
          </div>
        </div>
      </div>

      {/* Phone Suggestions Alert (if any) */}
      {customer.phoneSuggestions && customer.phoneSuggestions.length > 0 && (
        <div
          style={{
            background: 'var(--crm-warning-light)',
            border: '1px solid rgba(245, 158, 11, 0.3)',
            borderRadius: 'var(--crm-radius-md)',
            padding: '16px',
            marginBottom: '20px',
          }}
          data-testid="phone-suggestions-box"
        >
          <h4 style={{ margin: '0 0 8px', fontSize: '14px', fontWeight: '700', color: 'var(--crm-warning-text)' }}>
            <Icon name="lightbulb" size={16} /> Phát hiện gợi ý số điện thoại trong hội thoại/bình luận:
          </h4>
          <div style={{ display: 'flex', flexDirection: 'column', gap: '8px' }}>
            {customer.phoneSuggestions.map((sug) => (
              <div
                key={sug.id}
                style={{
                  display: 'flex',
                  alignItems: 'center',
                  justifyContent: 'space-between',
                  background: '#ffffff',
                  padding: '8px 12px',
                  borderRadius: 'var(--crm-radius-sm)',
                }}
                data-testid={`phone-suggestion-${sug.id}`}
              >
                <div>
                  <strong style={{ fontSize: '14px' }}>{sug.phoneE164}</strong>
                  {sug.rawMatched && <span style={{ fontSize: '12px', color: 'var(--crm-text-muted)', marginLeft: '8px' }}>(Trích xuất từ "{sug.rawMatched}")</span>}
                </div>
                {canCare && (
                  <Button
                    variant="primary"
                    size="sm"
                    onClick={() => handleConfirmPhone(sug)}
                    data-testid={`btn-confirm-phone-${sug.id}`}
                  >
                    Xác nhận số này
                  </Button>
                )}
              </div>
            ))}
          </div>
        </div>
      )}

      {/* Tabs Menu */}
      <div style={{ display: 'flex', borderBottom: '1px solid var(--crm-border)', marginBottom: '20px', gap: '4px' }}>
        {[
          { key: 'timeline', label: `Dòng thời gian (${timeline.length})` },
          { key: 'notes', label: `Ghi chú (${notes.length})` },
          { key: 'identities', label: `Danh tính liên kết (${customer.identities?.length || 0})` },
        ].map((tab) => (
          <button
            key={tab.key}
            type="button"
            className={`crm-settings-tab ${activeTab === tab.key ? 'active' : ''}`}
            onClick={() => setActiveTab(tab.key)}
            style={{ borderRadius: 'var(--crm-radius-sm) var(--crm-radius-sm) 0 0', borderBottom: activeTab === tab.key ? '2px solid var(--crm-primary)' : 'none' }}
            data-testid={`tab-${tab.key}`}
          >
            {tab.label}
          </button>
        ))}
      </div>

      {/* Tab 1: Dòng thời gian (Timeline) */}
      {activeTab === 'timeline' && (
        <div data-testid="timeline-pane">
          {timeline.length === 0 ? (
            <div style={{ textAlign: 'center', padding: '32px', color: 'var(--crm-text-muted)' }}>Chưa có sự kiện nào trong dòng thời gian.</div>
          ) : (
            <div style={{ display: 'flex', flexDirection: 'column', gap: '12px' }} data-testid="timeline-list">
              {timeline.map((item, idx) => (
                <div
                  key={idx}
                  style={{
                    background: 'var(--crm-surface)',
                    border: '1px solid var(--crm-border)',
                    borderRadius: 'var(--crm-radius-md)',
                    padding: '14px 18px',
                    display: 'flex',
                    flexDirection: 'column',
                    gap: '4px',
                  }}
                  data-testid={`timeline-item-${idx}`}
                >
                  <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
                    <div style={{ display: 'flex', alignItems: 'center', gap: '8px' }}>
                      <Badge variant={item.kind === 'message' ? 'primary' : item.kind === 'note' ? 'warning' : 'default'} size="sm">
                        {item.kind === 'message' ? 'Tin nhắn' : item.kind === 'note' ? 'Ghi chú' : item.kind}
                      </Badge>
                      <strong style={{ fontSize: '14px' }}>{item.title}</strong>
                    </div>
                    <span style={{ fontSize: '12px', color: 'var(--crm-text-subtle)' }}>
                      {formatVietnamDateTime(item.at)}
                    </span>
                  </div>
                  {item.body && <p style={{ margin: '4px 0 0', fontSize: '13px', color: 'var(--crm-text)' }}>{item.body}</p>}
                  <div style={{ fontSize: '12px', color: 'var(--crm-text-muted)', marginTop: '4px' }}>
                    {item.actor && <span>Thực hiện: {item.actor}</span>}
                    {item.channelName && <span> • Kênh: {item.channelName}</span>}
                  </div>
                </div>
              ))}
            </div>
          )}
        </div>
      )}

      {/* Tab 2: Ghi chú (Notes) */}
      {activeTab === 'notes' && (
        <div data-testid="notes-pane">
          {canCare && (
            <form onSubmit={handleAddNote} style={{ marginBottom: '20px' }} data-testid="add-note-form">
              <label style={{ display: 'block', fontSize: '13px', fontWeight: '600', marginBottom: '6px' }}>
                Thêm ghi chú chăm sóc khách hàng:
              </label>
              <textarea
                className="crm-form-input"
                rows={3}
                placeholder="Nhập nội dung ghi chú trao đổi, sở thích, phản hồi..."
                value={newNoteBody}
                onChange={(e) => setNewNoteBody(e.target.value)}
                required
                data-testid="input-note-body"
              />
              <div style={{ marginTop: '8px', display: 'flex', justifyContent: 'flex-end' }}>
                <Button type="submit" variant="primary" size="sm" isLoading={savingNote} data-testid="btn-submit-note">
                  Lưu ghi chú
                </Button>
              </div>
            </form>
          )}

          <div style={{ display: 'flex', flexDirection: 'column', gap: '10px' }} data-testid="notes-list">
            {notes.map((note) => (
              <div
                key={note.id}
                style={{
                  background: 'var(--crm-surface)',
                  border: '1px solid var(--crm-border)',
                  borderRadius: 'var(--crm-radius-md)',
                  padding: '14px',
                  display: 'flex',
                  justifyContent: 'space-between',
                  alignItems: 'flex-start',
                }}
                data-testid={`note-item-${note.id}`}
              >
                <div>
                  <p style={{ margin: '0 0 6px', fontSize: '14px' }}>{note.body}</p>
                  <div style={{ fontSize: '12px', color: 'var(--crm-text-muted)' }}>
                    Bởi: {note.createdBy || 'Hệ thống'} • {formatVietnamDateTime(note.createdAt)}
                  </div>
                </div>
                {canCare && (
                  <Button
                    variant="ghost"
                    size="sm"
                    style={{ color: 'var(--crm-danger)' }}
                    onClick={() => handleDeleteNote(note.id)}
                    data-testid={`btn-delete-note-${note.id}`}
                  >
                    Xoá
                  </Button>
                )}
              </div>
            ))}
          </div>
        </div>
      )}

      {/* Tab 3: Danh tính liên kết (Identities) */}
      {activeTab === 'identities' && (
        <div data-testid="identities-pane">
          <p style={{ margin: '0 0 16px', fontSize: '13px', color: 'var(--crm-text-muted)' }}>
            Mỗi khách hàng có thể sở hữu nhiều danh tính từ các kênh (Facebook Fanpage, Zalo OA...) sau khi được gắn tự động hoặc người dùng xác nhận gộp.
          </p>

          <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fill, minmax(280px, 1fr))', gap: '12px' }} data-testid="identities-list">
            {customer.identities?.map((idnt) => (
              <div
                key={idnt.id}
                style={{
                  background: 'var(--crm-surface)',
                  border: '1px solid var(--crm-border)',
                  borderRadius: 'var(--crm-radius-md)',
                  padding: '16px',
                  display: 'flex',
                  gap: '12px',
                  alignItems: 'center',
                }}
                data-testid={`identity-card-${idnt.id}`}
              >
                <div
                  style={{
                    width: '42px',
                    height: '42px',
                    borderRadius: '50%',
                    background: 'var(--crm-primary-light)',
                    color: 'var(--crm-primary)',
                    display: 'flex',
                    alignItems: 'center',
                    justifyContent: 'center',
                    fontWeight: '800',
                  }}
                >
                  {idnt.displayName?.[0] || 'ID'}
                </div>
                <div>
                  <div style={{ fontWeight: '700', fontSize: '14px' }}>{idnt.displayName || 'Khách'}</div>
                  <div style={{ fontSize: '12px', color: 'var(--crm-text-muted)' }}>
                    Kênh: <Badge variant="primary">{idnt.channelName || 'Mặc định'}</Badge>
                  </div>
                  <div style={{ fontSize: '11px', color: 'var(--crm-text-subtle)', marginTop: '2px' }}>
                    ExternalId: <code>{idnt.externalId}</code>
                  </div>
                </div>
              </div>
            ))}
          </div>
        </div>
      )}

      {/* Edit Customer Info Modal */}
      {editOpen && (
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
          data-testid="edit-customer-modal"
        >
          <div style={{ background: '#ffffff', borderRadius: 'var(--crm-radius-lg)', padding: '24px', maxWidth: '420px', width: '100%' }}>
            <h3 style={{ margin: '0 0 16px', fontSize: '18px', fontWeight: '700' }}>Cập nhật thông tin khách hàng</h3>
            <form onSubmit={handleUpdateCustomer}>
              <div style={{ marginBottom: '12px' }}>
                <label style={{ display: 'block', fontSize: '13px', fontWeight: '600', marginBottom: '4px' }}>Họ và tên</label>
                <input
                  type="text"
                  className="crm-form-input"
                  required
                  value={editName}
                  onChange={(e) => setEditName(e.target.value)}
                  data-testid="input-edit-name"
                />
              </div>
              <div style={{ marginBottom: '20px' }}>
                <label style={{ display: 'block', fontSize: '13px', fontWeight: '600', marginBottom: '4px' }}>Số điện thoại (+84...)</label>
                <input
                  type="text"
                  className="crm-form-input"
                  value={editPhone}
                  onChange={(e) => setEditPhone(e.target.value)}
                  placeholder="Ví dụ: +84987654321"
                  data-testid="input-edit-phone"
                />
              </div>
              <div style={{ display: 'flex', justifyContent: 'flex-end', gap: '8px' }}>
                <Button variant="ghost" onClick={() => setEditOpen(false)}>Huỷ</Button>
                <Button type="submit" variant="primary" data-testid="btn-save-edit-profile">Lưu thay đổi</Button>
              </div>
            </form>
          </div>
        </div>
      )}

      {/* Merge Modal */}
      <MergeModal
        isOpen={mergeModalOpen}
        onClose={() => setMergeModalOpen(false)}
        initialCustomerId={customerId}
        onSuccess={() => {
          setMergeModalOpen(false)
          loadCustomerData()
          if (onCustomerUpdated) onCustomerUpdated()
        }}
      />
    </div>
  )
}

export default CustomerProfile
