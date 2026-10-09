import React, { useState, useEffect, useRef } from 'react'
import { Icon } from '../../../shared/components/Icon'

export const DEFAULT_COLUMNS = {
  title: true,
  customer: true,
  phone: true,
  source: true,
  assignee: true,
  watchers: true,
  stage: true,
  value: true,
  lastActivity: true,
  actions: true,
}

export const COLUMN_DEFINITIONS = [
  { key: 'title', label: 'Tên cơ hội', fixed: true },
  { key: 'customer', label: 'Khách hàng' },
  { key: 'phone', label: 'Số điện thoại' },
  { key: 'source', label: 'Kênh nguồn' },
  { key: 'assignee', label: 'Phụ trách' },
  { key: 'watchers', label: 'Người theo dõi' },
  { key: 'stage', label: 'Giai đoạn' },
  { key: 'value', label: 'Giá trị' },
  { key: 'lastActivity', label: 'Hoạt động cuối' },
  { key: 'actions', label: 'Hành động', fixed: true },
]

export const getStoredColumns = () => {
  try {
    const raw = localStorage.getItem('crm_opp_columns')
    if (raw) {
      return { ...DEFAULT_COLUMNS, ...JSON.parse(raw) }
    }
  } catch {
    // ignore exceptions in sandbox/private mode
  }
  return DEFAULT_COLUMNS
}

export const setStoredColumns = (cols) => {
  try {
    localStorage.setItem('crm_opp_columns', JSON.stringify(cols))
  } catch {
    // ignore
  }
}

export const ColumnSettings = ({ columns = DEFAULT_COLUMNS, onChange }) => {
  const [open, setOpen] = useState(false)
  const menuRef = useRef(null)

  useEffect(() => {
    const handleClickOutside = (e) => {
      if (menuRef.current && !menuRef.current.contains(e.target)) {
        setOpen(false)
      }
    }
    if (open) {
      document.addEventListener('mousedown', handleClickOutside)
    }
    return () => {
      document.removeEventListener('mousedown', handleClickOutside)
    }
  }, [open])

  const handleToggle = (key) => {
    const next = { ...columns, [key]: !columns[key] }
    setStoredColumns(next)
    onChange?.(next)
  }

  const handleReset = () => {
    setStoredColumns(DEFAULT_COLUMNS)
    onChange?.(DEFAULT_COLUMNS)
  }

  return (
    <div className="crm-opp-col-settings-wrap" ref={menuRef}>
      <button
        type="button"
        className="crm-opp-btn crm-opp-btn--secondary"
        data-testid="btn-column-settings"
        onClick={() => setOpen(!open)}
        aria-haspopup="true"
        aria-expanded={open}
      >
        <Icon name="settings" size={16} /> Hiển thị cột
      </button>

      {open && (
        <div className="crm-opp-col-dropdown" data-testid="col-settings-dropdown">
          <div className="crm-opp-col-dropdown-header">
            <strong>Hiển thị cột</strong>
            <button
              type="button"
              className="crm-opp-btn-link"
              onClick={handleReset}
              data-testid="btn-reset-columns"
            >
              Mặc định
            </button>
          </div>
          <div className="crm-opp-col-list">
            {COLUMN_DEFINITIONS.map((col) => (
              <label
                key={col.key}
                className={`crm-opp-col-item ${col.fixed ? 'crm-opp-col-item--disabled' : ''}`}
              >
                <input
                  type="checkbox"
                  checked={columns[col.key] !== false}
                  disabled={col.fixed}
                  onChange={() => handleToggle(col.key)}
                  data-testid={`col-toggle-${col.key}`}
                />
                <span>{col.label}</span>
              </label>
            ))}
          </div>
        </div>
      )}
    </div>
  )
}

export default ColumnSettings
