import React, { useState, useEffect } from 'react'
import { customerApi } from '../api/customerApi'
import Button from '../../../shared/components/Button'
import Badge from '../../../shared/components/Badge'
import Icon from '../../../shared/components/Icon'

export const MergeModal = ({ isOpen, onClose, onSuccess, initialCustomerId = null }) => {
  const [suggestions, setSuggestions] = useState([])
  const [loading, setLoading] = useState(false)
  const [merging, setMerging] = useState(false)
  const [error, setError] = useState(null)
  const [message, setMessage] = useState(null)

  // Manual merge state
  const [manualKeptId, setManualKeptId] = useState(initialCustomerId || '')
  const [manualSourceId, setManualSourceId] = useState('')
  const [splitRecordId, setSplitRecordId] = useState('')

  useEffect(() => {
    if (isOpen) {
      loadSuggestions()
      if (initialCustomerId) setManualKeptId(initialCustomerId)
    }
  }, [isOpen, initialCustomerId])

  const loadSuggestions = async () => {
    setLoading(true)
    setError(null)
    try {
      const data = await customerApi.listMergeSuggestions()
      setSuggestions(data || [])
    } catch (err) {
      setError('Không thể tải gợi ý gộp: ' + (err?.message || ''))
    } finally {
      setLoading(false)
    }
  }

  const handleMerge = async (keptId, sourceId) => {
    if (!keptId || !sourceId || keptId === sourceId) {
      setError('Vui lòng chọn 2 khách hàng khác nhau để gộp')
      return
    }
    setMerging(true)
    setError(null)
    try {
      const result = await customerApi.merge(keptId, sourceId)
      setMessage(`Đã gộp thành công! Mã bản ghi gộp: ${result.mergeRecordId}`)
      loadSuggestions()
      if (onSuccess) onSuccess()
    } catch (err) {
      setError(err?.response?.data?.message || err?.message || 'Lỗi khi gộp khách hàng')
    } finally {
      setMerging(false)
    }
  }

  const handleSplit = async () => {
    if (!splitRecordId.trim()) return
    setMerging(true)
    setError(null)
    try {
      await customerApi.split(splitRecordId.trim())
      setMessage('Đã tách hồ sơ khách hàng thành công!')
      setSplitRecordId('')
      loadSuggestions()
      if (onSuccess) onSuccess()
    } catch (err) {
      setError(err?.response?.data?.message || err?.message || 'Lỗi khi tách khách hàng')
    } finally {
      setMerging(false)
    }
  }

  if (!isOpen) return null

  return (
    <div
      style={{
        position: 'fixed',
        inset: 0,
        backgroundColor: 'rgba(15, 23, 42, 0.6)',
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'center',
        zIndex: 1000,
        padding: '16px',
      }}
      data-testid="merge-modal"
    >
      <div
        style={{
          background: '#ffffff',
          borderRadius: 'var(--crm-radius-lg)',
          maxWidth: '680px',
          width: '100%',
          maxHeight: '90vh',
          display: 'flex',
          flexDirection: 'column',
          overflow: 'hidden',
          boxShadow: 'var(--crm-shadow-lg)',
        }}
      >
        <div style={{ padding: '20px 24px', borderBottom: '1px solid var(--crm-border)', display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
          <div>
            <h3 style={{ margin: 0, fontSize: '18px', fontWeight: '800' }}>Gợi ý & Gộp / Tách hồ sơ khách hàng</h3>
            <p style={{ margin: '4px 0 0', fontSize: '13px', color: 'var(--crm-text-muted)' }}>
              Chuyển toàn bộ danh tính, ghi chú sang khách giữ lại và có thể tách lại
            </p>
          </div>
          <button
            type="button"
            onClick={onClose}
            style={{ background: 'none', border: 'none', fontSize: '20px', cursor: 'pointer', color: 'var(--crm-text-muted)' }}
          >
            ×
          </button>
        </div>

        <div style={{ padding: '24px', overflowY: 'auto', flex: 1 }}>
          {error && (
            <div style={{ padding: '12px', background: 'var(--crm-danger-light)', color: 'var(--crm-danger-text)', borderRadius: 'var(--crm-radius-md)', marginBottom: '16px', fontSize: '13px' }} data-testid="merge-error-alert">
              {error}
            </div>
          )}
          {message && (
            <div style={{ padding: '12px', background: 'var(--crm-success-light)', color: 'var(--crm-success-text)', borderRadius: 'var(--crm-radius-md)', marginBottom: '16px', fontSize: '13px' }} data-testid="merge-success-alert">
              {message}
            </div>
          )}

          {/* Section: Suggestions */}
          <div style={{ marginBottom: '24px' }}>
            <h4 style={{ margin: '0 0 12px', fontSize: '15px', fontWeight: '700' }}>Danh sách gợi ý gộp tự động</h4>
            {loading ? (
              <div style={{ textAlign: 'center', padding: '16px', color: 'var(--crm-text-muted)' }}>Đang tìm kiếm gợi ý...</div>
            ) : suggestions.length === 0 ? (
              <div style={{ padding: '16px', background: 'var(--crm-surface-subtle)', borderRadius: 'var(--crm-radius-md)', fontSize: '13px', color: 'var(--crm-text-muted)', textAlign: 'center' }}>
                Hiện không có cặp khách hàng nào trùng lặp số điện thoại hoặc tên.
              </div>
            ) : (
              <div style={{ display: 'flex', flexDirection: 'column', gap: '10px' }} data-testid="merge-suggestions-list">
                {suggestions.map((s, index) => (
                  <div
                    key={index}
                    style={{
                      border: '1px solid var(--crm-border)',
                      borderRadius: 'var(--crm-radius-md)',
                      padding: '14px',
                      display: 'flex',
                      alignItems: 'center',
                      justifyContent: 'space-between',
                      gap: '12px',
                    }}
                    data-testid={`merge-suggestion-item-${index}`}
                  >
                    <div>
                      <div style={{ fontSize: '14px', fontWeight: '700' }}>
                        {s.customerAName} <span style={{ color: 'var(--crm-text-muted)', fontWeight: '400' }}><Icon name="arrow-left-right" size={14} aria-label="và" /></span> {s.customerBName}
                      </div>
                      <div style={{ fontSize: '12px', color: 'var(--crm-text-muted)', marginTop: '4px' }}>
                        Lý do: <Badge variant="warning">{s.reason}</Badge>{' '}
                        {s.sharedPhoneE164 && <span>(SĐT: {s.sharedPhoneE164})</span>}
                      </div>
                    </div>
                    <Button
                      variant="primary"
                      size="sm"
                      disabled={merging}
                      onClick={() => handleMerge(s.customerAId, s.customerBId)}
                      data-testid={`btn-merge-suggestion-${index}`}
                    >
                      Gộp vào {s.customerAName}
                    </Button>
                  </div>
                ))}
              </div>
            )}
          </div>

          {/* Section: Manual Merge */}
          <div style={{ borderTop: '1px solid var(--crm-border)', paddingTop: '16px', marginBottom: '20px' }}>
            <h4 style={{ margin: '0 0 12px', fontSize: '15px', fontWeight: '700' }}>Gộp thủ công theo ID</h4>
            <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: '12px', marginBottom: '12px' }}>
              <div>
                <label style={{ display: 'block', fontSize: '12px', fontWeight: '600', marginBottom: '4px' }}>ID Khách hàng giữ lại (Kept):</label>
                <input
                  type="text"
                  className="crm-form-input"
                  placeholder="Guid khách giữ lại..."
                  value={manualKeptId}
                  onChange={(e) => setManualKeptId(e.target.value)}
                  data-testid="input-kept-id"
                />
              </div>
              <div>
                <label style={{ display: 'block', fontSize: '12px', fontWeight: '600', marginBottom: '4px' }}>ID Khách hàng bị gộp (Source):</label>
                <input
                  type="text"
                  className="crm-form-input"
                  placeholder="Guid khách gộp vào..."
                  value={manualSourceId}
                  onChange={(e) => setManualSourceId(e.target.value)}
                  data-testid="input-source-id"
                />
              </div>
            </div>
            <Button
              variant="secondary"
              size="sm"
              disabled={!manualKeptId || !manualSourceId || merging}
              onClick={() => handleMerge(manualKeptId, manualSourceId)}
              data-testid="btn-manual-merge"
            >
              Thực hiện gộp hồ sơ
            </Button>
          </div>

          {/* Section: Split previously merged record */}
          <div style={{ borderTop: '1px solid var(--crm-border)', paddingTop: '16px' }}>
            <h4 style={{ margin: '0 0 8px', fontSize: '15px', fontWeight: '700' }}>Tách lại khách đã gộp (Split)</h4>
            <p style={{ margin: '0 0 8px', fontSize: '12px', color: 'var(--crm-text-muted)' }}>
              Nhập mã bản ghi gộp (MergeRecordId) để khôi phục trạng thái ban đầu của hai khách hàng.
            </p>
            <div style={{ display: 'flex', gap: '8px' }}>
              <input
                type="text"
                className="crm-form-input"
                placeholder="Nhập MergeRecordId..."
                value={splitRecordId}
                onChange={(e) => setSplitRecordId(e.target.value)}
                data-testid="input-split-record-id"
              />
              <Button
                variant="outline"
                size="sm"
                disabled={!splitRecordId.trim() || merging}
                onClick={handleSplit}
                data-testid="btn-split-customer"
              >
                Tách hồ sơ
              </Button>
            </div>
          </div>
        </div>

        <div style={{ padding: '16px 24px', borderTop: '1px solid var(--crm-border)', display: 'flex', justifyContent: 'flex-end' }}>
          <Button variant="ghost" onClick={onClose}>
            Đóng
          </Button>
        </div>
      </div>
    </div>
  )
}

export default MergeModal
