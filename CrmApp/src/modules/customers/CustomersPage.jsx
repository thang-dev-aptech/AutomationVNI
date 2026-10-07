import React, { useState } from 'react'
import { useAuth } from '../../auth/useAuth'
import Button from '../../shared/components/Button'
import Badge from '../../shared/components/Badge'
import Icon from '../../shared/components/Icon'
import './CustomersPage.css'

const initialCustomers = [
  {
    id: 'cust-1',
    name: 'Nguyễn Văn An',
    phone: '0981234567',
    email: 'an.nguyen@example.com',
    status: 'Tiềm năng',
    tag: 'Quan tâm kế toán',
    owner: 'Reviewer Care',
  },
  {
    id: 'cust-2',
    name: 'Trần Thị Bích',
    phone: '0972345678',
    email: 'bich.tran@example.com',
    status: 'Đã đăng ký',
    tag: 'Khóa K45',
    owner: 'ContentManager Admin',
  },
  {
    id: 'cust-3',
    name: 'Lê Hoàng Long',
    phone: '0913456789',
    email: 'long.le@example.com',
    status: 'Đang tư vấn',
    tag: 'Hà Nội',
    owner: 'Reviewer Care',
  },
]

export const CustomersPage = () => {
  const { canCare, canManage } = useAuth()
  const [customers, setCustomers] = useState(initialCustomers)
  const [searchTerm, setSearchTerm] = useState('')
  const [showCreateModal, setShowCreateModal] = useState(false)
  const [newName, setNewName] = useState('')
  const [newPhone, setNewPhone] = useState('')

  const filtered = customers.filter(
    (c) =>
      c.name.toLowerCase().includes(searchTerm.toLowerCase()) ||
      c.phone.includes(searchTerm) ||
      c.email.toLowerCase().includes(searchTerm.toLowerCase()),
  )

  const handleCreateCustomer = (e) => {
    e.preventDefault()
    if (!newName.trim() || !newPhone.trim()) return

    const newCust = {
      id: `cust-${Date.now()}`,
      name: newName.trim(),
      phone: newPhone.trim(),
      email: `${newName.toLowerCase().replace(/\s+/g, '')}@example.com`,
      status: 'Mới',
      tag: 'Tạo thủ công',
      owner: 'Tôi',
    }

    setCustomers([newCust, ...customers])
    setNewName('')
    setNewPhone('')
    setShowCreateModal(false)
  }

  const handleDelete = (id) => {
    if (window.confirm('Bạn có chắc chắn muốn xoá khách hàng này?')) {
      setCustomers(customers.filter((c) => c.id !== id))
    }
  }

  return (
    <div>
      <div className="crm-page-header">
        <div>
          <h2 style={{ margin: 0, fontSize: '20px', fontWeight: '800' }}>Hồ sơ Khách hàng</h2>
          <p style={{ margin: '4px 0 0', fontSize: '14px', color: 'var(--crm-text-muted)' }}>
            Quản lý thông tin liên hệ, trạng thái chăm sóc và kênh tương tác
          </p>
        </div>

        <div className="crm-header-actions" data-testid="customer-actions-bar">
          {/* Care Actions: Reviewer & Admin/ContentManager */}
          {canCare && (
            <>
              <Button
                variant="primary"
                icon={<Icon name="plus" size={16} />}
                onClick={() => setShowCreateModal(true)}
                data-testid="btn-create-customer"
              >
                Tạo khách hàng
              </Button>
              <Button
                variant="secondary"
                onClick={() => alert('Chọn 2 khách hàng để gộp hồ sơ')}
                data-testid="btn-merge-customer"
              >
                Gộp hồ sơ
              </Button>
            </>
          )}

          {/* Management Actions: Admin & ContentManager ONLY */}
          {canManage && (
            <>
              <Button
                variant="outline"
                onClick={() => alert('Tính năng nhập danh sách CSV')}
                data-testid="btn-import-csv"
              >
                Nhập CSV
              </Button>
              <Button
                variant="outline"
                onClick={() => alert('Xuất dữ liệu khách hàng')}
                data-testid="btn-export-customers"
              >
                Xuất dữ liệu
              </Button>
            </>
          )}
        </div>
      </div>

      {/* Search Bar */}
      <div style={{ marginBottom: '16px', display: 'flex', gap: '12px' }}>
        <input
          type="text"
          className="crm-form-input"
          placeholder="Tìm theo họ tên, số điện thoại hoặc email..."
          value={searchTerm}
          onChange={(e) => setSearchTerm(e.target.value)}
          style={{ maxWidth: '400px' }}
          data-testid="customer-search-input"
        />
      </div>

      {/* Customer Modal */}
      {showCreateModal && canCare && (
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
        >
          <div
            style={{
              background: '#ffffff',
              borderRadius: 'var(--crm-radius-lg)',
              padding: '24px',
              maxWidth: '420px',
              width: '100%',
            }}
          >
            <h3 style={{ margin: '0 0 16px', fontSize: '18px', fontWeight: '700' }}>Tạo khách hàng mới</h3>
            <form onSubmit={handleCreateCustomer}>
              <div style={{ marginBottom: '12px' }}>
                <label style={{ display: 'block', fontSize: '13px', fontWeight: '600', marginBottom: '4px' }}>
                  Họ và tên
                </label>
                <input
                  type="text"
                  className="crm-form-input"
                  required
                  value={newName}
                  onChange={(e) => setNewName(e.target.value)}
                  placeholder="Ví dụ: Nguyễn Văn A"
                  data-testid="input-new-customer-name"
                />
              </div>
              <div style={{ marginBottom: '20px' }}>
                <label style={{ display: 'block', fontSize: '13px', fontWeight: '600', marginBottom: '4px' }}>
                  Số điện thoại
                </label>
                <input
                  type="text"
                  className="crm-form-input"
                  required
                  value={newPhone}
                  onChange={(e) => setNewPhone(e.target.value)}
                  placeholder="Ví dụ: 0987654321"
                  data-testid="input-new-customer-phone"
                />
              </div>
              <div style={{ display: 'flex', justifyContent: 'flex-end', gap: '8px' }}>
                <Button variant="ghost" onClick={() => setShowCreateModal(false)}>
                  Huỷ
                </Button>
                <Button type="submit" variant="primary" data-testid="btn-save-customer">
                  Lưu hồ sơ
                </Button>
              </div>
            </form>
          </div>
        </div>
      )}

      {/* Customer Table */}
      <div className="crm-table-container">
        <table className="crm-table" data-testid="customer-table">
          <thead>
            <tr>
              <th>Khách hàng</th>
              <th>Số điện thoại</th>
              <th>Email</th>
              <th>Trạng thái</th>
              <th>Nhãn (Tag)</th>
              <th>Phụ trách</th>
              {canManage && <th>Thao tác</th>}
            </tr>
          </thead>
          <tbody>
            {filtered.map((c) => (
              <tr key={c.id} data-testid={`customer-row-${c.id}`}>
                <td>
                  <div className="crm-customer-name">{c.name}</div>
                  <div className="crm-customer-sub">ID: {c.id}</div>
                </td>
                <td>{c.phone}</td>
                <td>{c.email}</td>
                <td>
                  <Badge variant={c.status === 'Đã đăng ký' ? 'success' : 'primary'} size="sm">
                    {c.status}
                  </Badge>
                </td>
                <td>
                  <Badge variant="default" size="sm">
                    {c.tag}
                  </Badge>
                </td>
                <td>{c.owner}</td>
                {canManage && (
                  <td>
                    <Button
                      variant="danger"
                      size="sm"
                      onClick={() => handleDelete(c.id)}
                      data-testid={`btn-delete-${c.id}`}
                    >
                      Xoá
                    </Button>
                  </td>
                )}
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </div>
  )
}

export default CustomersPage
