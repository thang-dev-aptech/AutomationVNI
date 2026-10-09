import React, { useState, useEffect } from 'react'
import { customerApi } from '../api/customerApi'
import { useAuth } from '../../../auth/useAuth'
import Button from '../../../shared/components/Button'
import Badge from '../../../shared/components/Badge'
import Icon from '../../../shared/components/Icon'
import { formatVietnamDateTime } from '../../../shared/utils/dateUtils'
import CsvImportModal from './CsvImportModal'
import MergeModal from './MergeModal'

const defaultInitialCustomers = [
  {
    id: 'cust-1',
    displayName: 'Nguyễn Văn An',
    phoneE164: '+84901234567',
    identityCount: 1,
    createdAt: '2026-10-06T10:00:00Z',
  },
]

export const CustomerList = ({ onSelectCustomer }) => {
  const { canCare, canManage } = useAuth()
  const [customers, setCustomers] = useState(defaultInitialCustomers)
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState(null)

  // Filter params
  const [keyword, setKeyword] = useState('')
  const [hasPhoneFilter, setHasPhoneFilter] = useState('all') // 'all' | 'true' | 'false'

  // Modal controls
  const [createModalOpen, setCreateModalOpen] = useState(false)
  const [newName, setNewName] = useState('')
  const [newPhone, setNewPhone] = useState('')
  const [creating, setCreating] = useState(false)

  const [importModalOpen, setImportModalOpen] = useState(false)
  const [mergeModalOpen, setMergeModalOpen] = useState(false)

  useEffect(() => {
    loadCustomers()
  }, [hasPhoneFilter])

  const loadCustomers = async () => {
    setLoading(true)
    setError(null)
    try {
      const payload = {
        keyword: keyword.trim() || null,
        hasPhone: hasPhoneFilter === 'true' ? true : hasPhoneFilter === 'false' ? false : null,
        pageSize: 50,
      }
      const data = await customerApi.filter(payload)
      setCustomers(data?.items || data || [])
    } catch (err) {
      setError(err?.response?.data?.message || err?.message || 'Không thể tải danh sách khách hàng')
    } finally {
      setLoading(false)
    }
  }

  const handleSearchSubmit = (e) => {
    e.preventDefault()
    loadCustomers()
  }

  // Create Customer Manually
  const handleCreateCustomer = async (e) => {
    e.preventDefault()
    if (!newName.trim() || !canCare) return
    setCreating(true)
    try {
      await customerApi.create({
        displayName: newName.trim(),
        phoneE164: newPhone.trim() || null,
      })
      setNewName('')
      setNewPhone('')
      setCreateModalOpen(false)
      loadCustomers()
    } catch (err) {
      alert('Lỗi tạo khách: ' + (err?.response?.data?.message || err?.message))
    } finally {
      setCreating(false)
    }
  }

  // Delete Customer (Soft Delete)
  const handleDeleteCustomer = async (id) => {
    if (!canManage) return
    if (!window.confirm('Bạn có chắc muốn xoá khách hàng này?')) return
    try {
      await customerApi.softDelete(id)
      loadCustomers()
    } catch (err) {
      alert('Lỗi xoá khách hàng: ' + (err?.response?.data?.message || err?.message))
    }
  }

  // Export CSV
  const handleExportCsv = async () => {
    if (!canManage) return
    try {
      const blob = await customerApi.exportCsv()
      const url = window.URL.createObjectURL(new Blob([blob]))
      const link = document.createElement('a')
      link.href = url
      link.setAttribute('download', `danh-sach-khach-hang-${Date.now()}.csv`)
      document.body.appendChild(link)
      link.click()
      link.remove()
    } catch (err) {
      alert('Lỗi khi xuất file CSV: ' + (err?.message || ''))
    }
  }

  return (
    <div>
      {/* Page Header */}
      <div className="crm-page-header">
        <div>
          <h2 style={{ margin: 0, fontSize: '20px', fontWeight: '800' }}>Hồ sơ Khách hàng</h2>
          <p style={{ margin: '4px 0 0', fontSize: '14px', color: 'var(--crm-text-muted)' }}>
            Quản lý hồ sơ, danh tính đa kênh và hoạt động chăm sóc
          </p>
        </div>

        <div className="crm-header-actions" data-testid="customer-actions-bar">
          {canCare && (
            <>
              <Button
                variant="primary"
                icon={<Icon name="plus" size={16} />}
                onClick={() => setCreateModalOpen(true)}
                data-testid="btn-create-customer"
              >
                Tạo khách tay
              </Button>
              <Button
                variant="secondary"
                onClick={() => setMergeModalOpen(true)}
                data-testid="btn-merge-customer"
              >
                Gợi ý gộp
              </Button>
            </>
          )}

          {canManage && (
            <>
              <Button
                variant="outline"
                onClick={() => setImportModalOpen(true)}
                data-testid="btn-import-csv"
              >
                Nhập CSV
              </Button>
              <Button
                variant="outline"
                onClick={handleExportCsv}
                data-testid="btn-export-customers"
              >
                Xuất CSV
              </Button>
            </>
          )}
        </div>
      </div>

      {/* Search & Filter Bar */}
      <form onSubmit={handleSearchSubmit} style={{ display: 'flex', gap: '12px', marginBottom: '20px', flexWrap: 'wrap' }}>
        <input
          type="text"
          className="crm-form-input"
          placeholder="Tìm theo tên hoặc số điện thoại..."
          value={keyword}
          onChange={(e) => setKeyword(e.target.value)}
          style={{ maxWidth: '360px', flex: 1 }}
          data-testid="customer-search-input"
        />
        <select
          className="crm-form-input"
          style={{ width: 'auto', cursor: 'pointer' }}
          value={hasPhoneFilter}
          onChange={(e) => setHasPhoneFilter(e.target.value)}
          data-testid="customer-filter-has-phone"
        >
          <option value="all">Tất cả khách hàng</option>
          <option value="true">Có số điện thoại</option>
          <option value="false">Chưa có số điện thoại</option>
        </select>
        <Button type="submit" variant="secondary" icon={<Icon name="search" size={16} />} data-testid="btn-search-customer">
          Tìm kiếm
        </Button>
      </form>

      {/* Error state */}
      {error && (
        <div style={{ padding: '12px', background: 'var(--crm-danger-light)', color: 'var(--crm-danger-text)', borderRadius: 'var(--crm-radius-md)', marginBottom: '16px' }}>
          {error}
        </div>
      )}

      {/* Customers Table */}
      <div className="crm-table-container">
        <table className="crm-table" data-testid="customer-table">
          <thead>
            <tr>
              <th>Khách hàng</th>
              <th>Số điện thoại</th>
              <th>Danh tính</th>
              <th>Ngày tạo</th>
              <th>Thao tác</th>
            </tr>
          </thead>
          <tbody>
            {loading && customers.length === 0 ? (
              <tr>
                <td colSpan={5} style={{ textAlign: 'center', padding: '32px', color: 'var(--crm-text-muted)' }}>
                  Đang tải danh sách khách hàng...
                </td>
              </tr>
            ) : customers.length === 0 ? (
              <tr>
                <td colSpan={5} style={{ textAlign: 'center', padding: '32px', color: 'var(--crm-text-muted)' }}>
                  Không tìm thấy khách hàng nào phù hợp với bộ lọc.
                </td>
              </tr>
            ) : (
              customers.map((c) => (
                <tr key={c.id} data-testid={`customer-row-${c.id}`} style={{ cursor: 'pointer' }}>
                  <td onClick={() => onSelectCustomer(c.id)}>
                    <div className="crm-customer-name" data-testid={`customer-name-${c.id}`}>{c.displayName}</div>
                    <div className="crm-customer-sub">ID: {c.id}</div>
                  </td>
                  <td onClick={() => onSelectCustomer(c.id)}>
                    {c.phoneE164 ? (
                      <Badge variant="success" size="sm">{c.phoneE164}</Badge>
                    ) : (
                      <span style={{ color: 'var(--crm-text-subtle)', fontSize: '13px' }}>—</span>
                    )}
                  </td>
                  <td onClick={() => onSelectCustomer(c.id)}>
                    <Badge variant="primary" size="sm">
                      {c.identityCount || 1} kênh liên kết
                    </Badge>
                  </td>
                  <td onClick={() => onSelectCustomer(c.id)}>
                    <span style={{ fontSize: '13px', color: 'var(--crm-text-muted)' }}>
                      {formatVietnamDateTime(c.createdAt)}
                    </span>
                  </td>
                  <td>
                    <div style={{ display: 'flex', gap: '8px' }}>
                      <Button
                        variant="secondary"
                        size="sm"
                        onClick={() => onSelectCustomer(c.id)}
                        data-testid={`btn-view-customer-${c.id}`}
                      >
                        Xem hồ sơ
                      </Button>
                      {canManage && (
                        <Button
                          variant="danger"
                          size="sm"
                          onClick={() => handleDeleteCustomer(c.id)}
                          data-testid={`btn-delete-${c.id}`}
                        >
                          Xoá
                        </Button>
                      )}
                    </div>
                  </td>
                </tr>
              ))
            )}
          </tbody>
        </table>
      </div>

      {/* Manual Create Modal */}
      {createModalOpen && (
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
          data-testid="create-customer-modal"
        >
          <div style={{ background: '#ffffff', borderRadius: 'var(--crm-radius-lg)', padding: '24px', maxWidth: '420px', width: '100%' }}>
            <h3 style={{ margin: '0 0 16px', fontSize: '18px', fontWeight: '700' }}>Tạo khách hàng mới (Thủ công)</h3>
            <form onSubmit={handleCreateCustomer}>
              <div style={{ marginBottom: '12px' }}>
                <label style={{ display: 'block', fontSize: '13px', fontWeight: '600', marginBottom: '4px' }}>Họ và tên</label>
                <input
                  type="text"
                  className="crm-form-input"
                  required
                  placeholder="Ví dụ: Nguyễn Văn A"
                  value={newName}
                  onChange={(e) => setNewName(e.target.value)}
                  data-testid="input-new-customer-name"
                />
              </div>
              <div style={{ marginBottom: '20px' }}>
                <label style={{ display: 'block', fontSize: '13px', fontWeight: '600', marginBottom: '4px' }}>Số điện thoại (+84...)</label>
                <input
                  type="text"
                  className="crm-form-input"
                  placeholder="Ví dụ: 0912345678 hoặc +84912345678"
                  value={newPhone}
                  onChange={(e) => setNewPhone(e.target.value)}
                  data-testid="input-new-customer-phone"
                />
              </div>
              <div style={{ display: 'flex', justifyContent: 'flex-end', gap: '8px' }}>
                <Button variant="ghost" onClick={() => setCreateModalOpen(false)}>Huỷ</Button>
                <Button type="submit" variant="primary" isLoading={creating} data-testid="btn-save-customer">Lưu khách hàng</Button>
              </div>
            </form>
          </div>
        </div>
      )}

      {/* CSV Import Modal */}
      <CsvImportModal
        isOpen={importModalOpen}
        onClose={() => setImportModalOpen(false)}
        onSuccess={() => {
          setImportModalOpen(false)
          loadCustomers()
        }}
      />

      {/* Merge Modal */}
      <MergeModal
        isOpen={mergeModalOpen}
        onClose={() => setMergeModalOpen(false)}
        onSuccess={() => {
          setMergeModalOpen(false)
          loadCustomers()
        }}
      />
    </div>
  )
}

export default CustomerList
