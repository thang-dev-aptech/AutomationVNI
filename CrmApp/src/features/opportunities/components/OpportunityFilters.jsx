import React, { useState, useEffect } from 'react'
import Icon from '../../../shared/components/Icon'

export const OpportunityFilters = ({
  filters,
  onChange,
  stages = [],
  users = [],
  canViewAll = false,
  isOpen = true,
  onReset,
}) => {
  const [searchTerm, setSearchTerm] = useState(filters.keyword || '')

  useEffect(() => {
    setSearchTerm(filters.keyword || '')
  }, [filters.keyword])

  // Debounce search term
  useEffect(() => {
    const handler = setTimeout(() => {
      if ((filters.keyword || '') !== searchTerm) {
        onChange({ keyword: searchTerm || null, index: 1 })
      }
    }, 300)
    return () => clearTimeout(handler)
  }, [searchTerm])

  if (!isOpen) return null

  return (
    <div className="crm-opp-filters-bar" data-testid="opportunity-filters-bar">
      {/* Keyword Search */}
      <div className="crm-opp-filter-item crm-opp-filter-item--search">
        <label className="crm-opp-filter-lbl" htmlFor="opp-search-input">Tìm kiếm</label>
        <div className="crm-opp-input-wrap">
          <input
            id="opp-search-input"
            type="text"
            className="crm-opp-input"
            placeholder="Tên cơ hội, khách hàng, SĐT..."
            value={searchTerm}
            onChange={(e) => setSearchTerm(e.target.value)}
            data-testid="input-opp-search"
          />
          {searchTerm && (
            <button
              type="button"
              className="crm-opp-input-clear"
              onClick={() => setSearchTerm('')}
              title="Xoá tìm kiếm"
              aria-label="Xoá tìm kiếm"
            >
              <Icon name="close" size={14} />
            </button>
          )}
        </div>
      </div>

      {/* Stage Dropdown */}
      <div className="crm-opp-filter-item">
        <label className="crm-opp-filter-lbl" htmlFor="opp-stage-select">Giai đoạn</label>
        <select
          id="opp-stage-select"
          className="crm-opp-select"
          value={filters.stageId || filters.stage || ''}
          onChange={(e) => onChange({ stage: e.target.value || null, stageId: e.target.value || null, index: 1 })}
          data-testid="select-opp-stage"
        >
          <option value="">Tất cả giai đoạn</option>
          {stages.map((st) => (
            <option key={st.id} value={st.id}>
              {st.name}
            </option>
          ))}
        </select>
      </div>

      {/* Assignee Filter */}
      <div className="crm-opp-filter-item">
        <label className="crm-opp-filter-lbl" htmlFor="opp-assignee-select">Phụ trách</label>
        <select
          id="opp-assignee-select"
          className="crm-opp-select"
          value={filters.assignee || 'mine'}
          onChange={(e) => onChange({ assignee: e.target.value, index: 1 })}
          data-testid="select-opp-assignee"
        >
          <option value="mine">Phụ trách: Tôi</option>
          {canViewAll && <option value="all">Tất cả nhân viên</option>}
          <option value="unassigned">Chưa gán phụ trách</option>
          {users.length > 0 && (
            <optgroup label="Chọn nhân viên">
              {users.map((u) => (
                <option key={u.id} value={u.id}>
                  {u.displayName || u.userName}
                </option>
              ))}
            </optgroup>
          )}
        </select>
      </div>

      {/* Source Filter */}
      <div className="crm-opp-filter-item">
        <label className="crm-opp-filter-lbl" htmlFor="opp-source-select">Nguồn</label>
        <select
          id="opp-source-select"
          className="crm-opp-select"
          value={filters.source || ''}
          onChange={(e) => onChange({ source: e.target.value ? Number(e.target.value) : null, index: 1 })}
          data-testid="select-opp-source"
        >
          <option value="">Tất cả nguồn</option>
          <option value="1">Thủ công</option>
          <option value="2">Tin nhắn</option>
          <option value="3">Bình luận</option>
        </select>
      </div>

      {/* Reset button */}
      <div className="crm-opp-filter-actions">
        <button
          type="button"
          className="crm-opp-btn crm-opp-btn--ghost"
          onClick={onReset}
          data-testid="btn-reset-filters"
          title="Đặt lại toàn bộ bộ lọc"
        >
          Đặt lại
        </button>
      </div>
    </div>
  )
}

export default OpportunityFilters
