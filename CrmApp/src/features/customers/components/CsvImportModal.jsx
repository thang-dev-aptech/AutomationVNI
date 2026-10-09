import React, { useState } from 'react'
import { customerApi } from '../api/customerApi'
import Button from '../../../shared/components/Button'
import Badge from '../../../shared/components/Badge'

export const CsvImportModal = ({ isOpen, onClose, onSuccess }) => {
  const [file, setFile] = useState(null)
  const [previewData, setPreviewData] = useState(null)
  const [loading, setLoading] = useState(false)
  const [commitResult, setCommitResult] = useState(null)
  const [error, setError] = useState(null)

  if (!isOpen) return null

  const handleFileChange = (e) => {
    const selected = e.target.files?.[0]
    setFile(selected || null)
    setPreviewData(null)
    setCommitResult(null)
    setError(null)
  }

  const handlePreview = async () => {
    if (!file) {
      setError('Vui lòng chọn file CSV')
      return
    }
    setLoading(true)
    setError(null)
    try {
      const data = await customerApi.importPreview(file)
      setPreviewData(data)
    } catch (err) {
      setError(err?.response?.data?.message || err?.message || 'Lỗi khi đọc file CSV')
    } finally {
      setLoading(false)
    }
  }

  const handleCommit = async () => {
    if (!file) return
    setLoading(true)
    setError(null)
    try {
      const data = await customerApi.importCommit(file)
      setCommitResult(data)
      if (onSuccess) onSuccess()
    } catch (err) {
      setError(err?.response?.data?.message || err?.message || 'Lỗi khi nhập dữ liệu CSV')
    } finally {
      setLoading(false)
    }
  }

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
      data-testid="csv-import-modal"
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
            <h3 style={{ margin: 0, fontSize: '18px', fontWeight: '800' }}>Nhập danh sách khách hàng từ CSV</h3>
            <p style={{ margin: '4px 0 0', fontSize: '13px', color: 'var(--crm-text-muted)' }}>
              Xem trước tính hợp lệ trước khi ghi vào hệ thống CRM
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
            <div style={{ padding: '12px', background: 'var(--crm-danger-light)', color: 'var(--crm-danger-text)', borderRadius: 'var(--crm-radius-md)', marginBottom: '16px', fontSize: '13px' }}>
              {error}
            </div>
          )}

          {!commitResult && (
            <div style={{ marginBottom: '20px' }}>
              <label style={{ display: 'block', fontSize: '14px', fontWeight: '600', marginBottom: '8px' }}>
                Chọn file CSV (.csv)
              </label>
              <input
                type="file"
                accept=".csv"
                onChange={handleFileChange}
                data-testid="csv-file-input"
                style={{ padding: '8px', border: '1px solid var(--crm-border)', borderRadius: 'var(--crm-radius-md)', width: '100%' }}
              />
              <div style={{ marginTop: '12px' }}>
                <Button
                  variant="secondary"
                  size="sm"
                  onClick={handlePreview}
                  disabled={!file || loading}
                  isLoading={loading && !previewData}
                  data-testid="btn-csv-preview"
                >
                  Xem trước dữ liệu
                </Button>
              </div>
            </div>
          )}

          {/* Preview Details */}
          {previewData && !commitResult && (
            <div data-testid="csv-preview-pane">
              <div style={{ display: 'grid', gridTemplateColumns: 'repeat(4, 1fr)', gap: '10px', marginBottom: '16px' }}>
                <div style={{ padding: '10px', background: 'var(--crm-surface-subtle)', borderRadius: 'var(--crm-radius-md)', textAlign: 'center' }}>
                  <div style={{ fontSize: '11px', color: 'var(--crm-text-muted)' }}>Tổng số dòng</div>
                  <div style={{ fontSize: '18px', fontWeight: '800' }}>{previewData.totalRows}</div>
                </div>
                <div style={{ padding: '10px', background: 'var(--crm-success-light)', borderRadius: 'var(--crm-radius-md)', textAlign: 'center' }}>
                  <div style={{ fontSize: '11px', color: 'var(--crm-success-text)' }}>Hợp lệ tạo mới</div>
                  <div style={{ fontSize: '18px', fontWeight: '800', color: 'var(--crm-success-text)' }}>{previewData.createCount}</div>
                </div>
                <div style={{ padding: '10px', background: 'var(--crm-warning-light)', borderRadius: 'var(--crm-radius-md)', textAlign: 'center' }}>
                  <div style={{ fontSize: '11px', color: 'var(--crm-warning-text)' }}>Trùng số ĐT (bỏ qua)</div>
                  <div style={{ fontSize: '18px', fontWeight: '800', color: 'var(--crm-warning-text)' }}>{previewData.duplicatePhoneCount}</div>
                </div>
                <div style={{ padding: '10px', background: 'var(--crm-danger-light)', borderRadius: 'var(--crm-radius-md)', textAlign: 'center' }}>
                  <div style={{ fontSize: '11px', color: 'var(--crm-danger-text)' }}>Không hợp lệ</div>
                  <div style={{ fontSize: '18px', fontWeight: '800', color: 'var(--crm-danger-text)' }}>{previewData.invalidCount}</div>
                </div>
              </div>

              <div style={{ maxHeight: '200px', overflowY: 'auto', border: '1px solid var(--crm-border)', borderRadius: 'var(--crm-radius-md)' }}>
                <table style={{ width: '100%', fontSize: '12px', borderCollapse: 'collapse' }}>
                  <thead>
                    <tr style={{ background: 'var(--crm-surface-subtle)', textAlign: 'left' }}>
                      <th style={{ padding: '8px 10px' }}>Dòng</th>
                      <th style={{ padding: '8px 10px' }}>Họ tên</th>
                      <th style={{ padding: '8px 10px' }}>SĐT</th>
                      <th style={{ padding: '8px 10px' }}>Trạng thái</th>
                    </tr>
                  </thead>
                  <tbody>
                    {previewData.rows?.map((r, i) => (
                      <tr key={i} style={{ borderTop: '1px solid var(--crm-border)' }}>
                        <td style={{ padding: '6px 10px' }}>{r.lineNumber}</td>
                        <td style={{ padding: '6px 10px', fontWeight: '600' }}>{r.displayName}</td>
                        <td style={{ padding: '6px 10px' }}>{r.phoneE164 || r.phoneRaw || '-'}</td>
                        <td style={{ padding: '6px 10px' }}>
                          <Badge variant={r.status === 'create' ? 'success' : r.status === 'duplicate_phone' ? 'warning' : 'danger'}>
                            {r.status === 'create' ? 'Tạo mới' : r.status === 'duplicate_phone' ? 'Trùng số' : 'Lỗi'}
                          </Badge>
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            </div>
          )}

          {/* Commit Result */}
          {commitResult && (
            <div style={{ padding: '20px', background: 'var(--crm-success-light)', borderRadius: 'var(--crm-radius-md)', textAlign: 'center' }} data-testid="csv-commit-success">
              <h4 style={{ margin: '0 0 8px', color: 'var(--crm-success-text)', fontSize: '16px' }}>Đã nhập thành công!</h4>
              <p style={{ margin: 0, fontSize: '13px', color: 'var(--crm-success-text)' }}>
                Đã tạo mới <strong>{commitResult.created}</strong> khách hàng.
                {commitResult.skippedDuplicatePhone > 0 && ` Bỏ qua ${commitResult.skippedDuplicatePhone} số trùng lặp.`}
              </p>
            </div>
          )}
        </div>

        <div style={{ padding: '16px 24px', borderTop: '1px solid var(--crm-border)', display: 'flex', justifyContent: 'flex-end', gap: '8px' }}>
          <Button variant="ghost" onClick={onClose}>
            {commitResult ? 'Đóng' : 'Huỷ'}
          </Button>
          {previewData && !commitResult && (
            <Button
              variant="primary"
              onClick={handleCommit}
              disabled={loading || previewData.createCount === 0}
              isLoading={loading}
              data-testid="btn-csv-commit"
            >
              Xác nhận nhập ({previewData.createCount})
            </Button>
          )}
        </div>
      </div>
    </div>
  )
}

export default CsvImportModal
