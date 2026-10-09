import React, { useState, useEffect, useRef } from 'react'
import { customerApi } from '../../customers/api/customerApi'
import { opportunityApi } from '../api/opportunityApi'
import Icon from '../../../shared/components/Icon'
import './OpportunityFormModal.css'

export const OpportunityFormModal = ({
  isOpen = false,
  onClose,
  onSuccess,
  initialData = null,
  stages = [],
  users = [],
}) => {
  const isEdit = Boolean(initialData?.id)

  const [title, setTitle] = useState('')
  const [selectedCustomer, setSelectedCustomer] = useState(null)
  const [customerSearch, setCustomerSearch] = useState('')
  const [customerResults, setCustomerResults] = useState([])
  const [searchingCustomer, setSearchingCustomer] = useState(false)
  const [customerPickerOpen, setCustomerPickerOpen] = useState(false)

  const [stageId, setStageId] = useState('')
  const [expectedValue, setExpectedValue] = useState('')
  const [assigneeUserId, setAssigneeUserId] = useState('')
  const [source, setSource] = useState(1) // 1: Manual, 2: Message, 3: Comment
  const [lostReason, setLostReason] = useState('')

  const [saving, setSaving] = useState(false)
  const [error, setError] = useState(null)

  const customerPickerRef = useRef(null)

  // Initialize form state
  useEffect(() => {
    if (initialData) {
      setTitle(initialData.title || '')
      setStageId(initialData.stageId || (stages[0]?.id || ''))
      setExpectedValue(initialData.expectedValue != null ? String(initialData.expectedValue) : '0')
      setAssigneeUserId(initialData.assigneeUserId || '')
      setSource(initialData.source || 1)
      setLostReason(initialData.lostReason || '')
      if (initialData.crmCustomerId) {
        setSelectedCustomer({
          id: initialData.crmCustomerId,
          displayName: initialData.customerName || 'Khách hàng',
          phoneE164: initialData.customerPhoneE164 || '',
        })
      } else {
        setSelectedCustomer(null)
      }
    } else {
      setTitle('')
      setStageId(stages[0]?.id || '')
      setExpectedValue('0')
      setAssigneeUserId('')
      setSource(1)
      setLostReason('')
      setSelectedCustomer(null)
    }
    setCustomerSearch('')
    setCustomerResults([])
    setError(null)
  }, [initialData, stages, isOpen])

  // Customer search with debounce via customerApi.filter({ keyword, index: 1, size: 20 })
  useEffect(() => {
    if (!customerSearch.trim() || !customerPickerOpen) {
      setCustomerResults([])
      return
    }

    const timer = setTimeout(async () => {
      setSearchingCustomer(true)
      try {
        const res = await customerApi.filter({
          keyword: customerSearch.trim(),
          index: 1,
          size: 20,
        })
        const items = res?.items || []
        setCustomerResults(items)
      } catch (err) {
        // ignore search error
      } finally {
        setSearchingCustomer(false)
      }
    }, 300)

    return () => clearTimeout(timer)
  }, [customerSearch, customerPickerOpen])

  // Close customer dropdown on outside click
  useEffect(() => {
    const handleClickOutside = (e) => {
      if (customerPickerRef.current && !customerPickerRef.current.contains(e.target)) {
        setCustomerPickerOpen(false)
      }
    }
    document.addEventListener('mousedown', handleClickOutside)
    return () => document.removeEventListener('mousedown', handleClickOutside)
  }, [])

  if (!isOpen) return null

  const handleSubmit = async (e) => {
    e.preventDefault()
    if (!title.trim()) {
      setError('Vui lòng nhập tên cơ hội')
      return
    }
    if (!isEdit && !selectedCustomer?.id) {
      setError('Vui lòng chọn khách hàng')
      return
    }

    setSaving(true)
    setError(null)

    try {
      const parsedVal = parseFloat(expectedValue) || 0

      if (isEdit) {
        await opportunityApi.update(initialData.id, {
          title: title.trim(),
          expectedValue: parsedVal,
          assigneeUserId: assigneeUserId || null,
          lostReason: lostReason || null,
        })
        if (stageId && stageId !== initialData.stageId) {
          await opportunityApi.moveStage(initialData.id, {
            stageId,
            lostReason: lostReason || null,
          })
        }
      } else {
        await opportunityApi.create({
          crmCustomerId: selectedCustomer.id,
          title: title.trim(),
          stageId: stageId || null,
          assigneeUserId: assigneeUserId || null,
          expectedValue: parsedVal,
          source: Number(source) || 1,
        })
      }

      onSuccess?.()
      onClose()
    } catch (err) {
      setError(err?.response?.data?.message || err?.message || 'Có lỗi xảy ra khi lưu cơ hội')
    } finally {
      setSaving(false)
    }
  }

  return (
    <div
      className="crm-opp-modal-backdrop crm-opp-form-modal-backdrop"
      role="dialog"
      aria-modal="true"
      data-testid="opportunity-form-modal"
    >
      <div className="crm-opp-modal-content crm-opp-form-modal-content">
        <div className="crm-opp-modal-header">
          <h3 className="crm-opp-modal-title">
            {isEdit ? 'Chỉnh sửa cơ hội' : 'Thêm cơ hội mới'}
          </h3>
          <button
            type="button"
            className="crm-opp-modal-close"
            onClick={onClose}
            aria-label="Đóng"
            data-testid="btn-close-modal"
          >
            <Icon name="close" size={16} />
          </button>
        </div>

        <form onSubmit={handleSubmit} className="crm-opp-form">
          {error && (
            <div className="crm-opp-form-error" data-testid="modal-error">
              <Icon name="alert" size={14} /> {error}
            </div>
          )}

          {/* Tên cơ hội */}
          <div className="crm-opp-form-group">
            <label className="crm-opp-form-label" htmlFor="opp-title-input">
              Tên cơ hội <span className="crm-opp-req">*</span>
            </label>
            <input
              id="opp-title-input"
              type="text"
              className="crm-opp-form-control"
              placeholder="Ví dụ: Tư vấn khóa học Tiếng Anh B2"
              value={title}
              onChange={(e) => setTitle(e.target.value)}
              required
              data-testid="input-form-title"
            />
          </div>

          {/* Khách hàng */}
          <div className="crm-opp-form-group" ref={customerPickerRef}>
            <label className="crm-opp-form-label">
              Khách hàng <span className="crm-opp-req">*</span>
            </label>
            {isEdit ? (
              <div className="crm-opp-readonly-customer" data-testid="readonly-customer">
                <Icon name="user" size={16} aria-hidden="true" />
                <strong>{selectedCustomer?.displayName || 'Khách hàng'}</strong>
                {selectedCustomer?.phoneE164 && <span> • {selectedCustomer.phoneE164}</span>}
              </div>
            ) : (
              <div className="crm-opp-customer-picker">
                {selectedCustomer ? (
                  <div className="crm-opp-selected-customer-pill" data-testid="selected-customer-pill">
                    <span>
                      <Icon name="user" size={14} aria-hidden="true" />{' '}
                      <strong>{selectedCustomer.displayName}</strong>
                      {selectedCustomer.phoneE164 && ` (${selectedCustomer.phoneE164})`}
                    </span>
                    <button
                      type="button"
                      className="crm-opp-btn-remove-cust"
                      onClick={() => setSelectedCustomer(null)}
                      title="Chọn khách khác"
                      aria-label="Chọn khách khác"
                      data-testid="btn-remove-customer"
                    >
                      <Icon name="close" size={14} />
                    </button>
                  </div>
                ) : (
                  <div className="crm-opp-picker-input-wrap">
                    <span className="crm-opp-picker-search-icon" aria-hidden="true">
                      <Icon name="search" size={16} />
                    </span>
                    <input
                      type="text"
                      className="crm-opp-form-control"
                      placeholder="Tìm khách hàng theo tên, SĐT..."
                      value={customerSearch}
                      onChange={(e) => {
                        setCustomerSearch(e.target.value)
                        setCustomerPickerOpen(true)
                      }}
                      onFocus={() => setCustomerPickerOpen(true)}
                      data-testid="input-customer-search"
                    />
                    {searchingCustomer && (
                      <span className="crm-opp-searching-indicator">Đang tìm...</span>
                    )}

                    {customerPickerOpen && customerResults.length > 0 && (
                      <ul className="crm-opp-picker-results" data-testid="customer-picker-results">
                        {customerResults.map((c) => (
                          <li
                            key={c.id}
                            className="crm-opp-picker-item"
                            onClick={() => {
                              setSelectedCustomer(c)
                              setCustomerPickerOpen(false)
                              setCustomerSearch('')
                            }}
                            data-testid={`customer-option-${c.id}`}
                          >
                            <strong>{c.displayName || 'Không có tên'}</strong>
                            {c.phoneE164 && <small>{c.phoneE164}</small>}
                          </li>
                        ))}
                      </ul>
                    )}
                  </div>
                )}
              </div>
            )}
          </div>

          <div className="crm-opp-form-row">
            {/* Giai đoạn */}
            <div className="crm-opp-form-group">
              <label className="crm-opp-form-label" htmlFor="opp-form-stage">
                Giai đoạn
              </label>
              <select
                id="opp-form-stage"
                className="crm-opp-form-control"
                value={stageId}
                onChange={(e) => setStageId(e.target.value)}
                data-testid="select-form-stage"
              >
                {stages.map((st) => (
                  <option key={st.id} value={st.id}>
                    {st.name}
                  </option>
                ))}
              </select>
            </div>

            {/* Giá trị dự kiến */}
            <div className="crm-opp-form-group">
              <label className="crm-opp-form-label" htmlFor="opp-form-value">
                Giá trị dự kiến (VNĐ)
              </label>
              <input
                id="opp-form-value"
                type="number"
                min="0"
                step="1000"
                className="crm-opp-form-control"
                placeholder="0"
                value={expectedValue}
                onChange={(e) => setExpectedValue(e.target.value)}
                data-testid="input-form-value"
              />
            </div>
          </div>

          <div className="crm-opp-form-row">
            {/* Người phụ trách */}
            <div className="crm-opp-form-group">
              <label className="crm-opp-form-label" htmlFor="opp-form-assignee">
                Người phụ trách
              </label>
              <select
                id="opp-form-assignee"
                className="crm-opp-form-control"
                value={assigneeUserId}
                onChange={(e) => setAssigneeUserId(e.target.value)}
                data-testid="select-form-assignee"
              >
                <option value="">Chưa phân công</option>
                {users.map((u) => (
                  <option key={u.id} value={u.id}>
                    {u.displayName || u.userName}
                  </option>
                ))}
              </select>
            </div>

            {/* Kênh nguồn */}
            {!isEdit && (
              <div className="crm-opp-form-group">
                <label className="crm-opp-form-label" htmlFor="opp-form-source">
                  Nguồn
                </label>
                <select
                  id="opp-form-source"
                  className="crm-opp-form-control"
                  value={source}
                  onChange={(e) => setSource(Number(e.target.value))}
                  data-testid="select-form-source"
                >
                  <option value={1}>Thủ công</option>
                  <option value={2}>Tin nhắn</option>
                  <option value={3}>Bình luận</option>
                </select>
              </div>
            )}
          </div>

          {/* Lý do thất bại (nếu Lost) */}
          {isEdit && initialData?.status === 3 && (
            <div className="crm-opp-form-group">
              <label className="crm-opp-form-label" htmlFor="opp-form-lost-reason">
                Lý do thất bại
              </label>
              <textarea
                id="opp-form-lost-reason"
                className="crm-opp-form-control"
                rows="2"
                placeholder="Nhập lý do thất bại..."
                value={lostReason}
                onChange={(e) => setLostReason(e.target.value)}
                data-testid="textarea-form-lost-reason"
              />
            </div>
          )}

          <div className="crm-opp-modal-footer">
            <button
              type="button"
              className="crm-opp-btn crm-opp-btn--secondary"
              onClick={onClose}
              disabled={saving}
              data-testid="btn-cancel-modal"
            >
              Huỷ
            </button>
            <button
              type="submit"
              className="crm-opp-btn crm-opp-btn--primary"
              disabled={saving}
              data-testid="btn-submit-modal"
            >
              {saving ? 'Đang lưu...' : isEdit ? 'Cập nhật' : 'Tạo cơ hội'}
            </button>
          </div>
        </form>
      </div>
    </div>
  )
}

export default OpportunityFormModal
