import React, { useState, useRef, useEffect } from 'react'
import { Link } from 'react-router-dom'
import { SourceBadge } from '../../inbox/components/SourceBadge'
import { formatVietnamDateTime } from '../../../shared/utils/dateUtils'
import { formatCurrencyVnd } from './OpportunityStatsBar'

export const OpportunityTable = ({
  items = [],
  total = 0,
  pageIndex = 1,
  pageSize = 20,
  loading = false,
  error = null,
  activeTab = 'all',
  onTabChange,
  columns,
  stats = null,
  stages = [],
  isReadOnly = false,
  onOpenCreate,
  onRefresh,
  onRowClick,
  onEdit,
  onMoveStage,
  onArchive,
  onUnarchive,
  onDelete,
  onPageChange,
  showFilters,
  onToggleFilters,
  activeFilterCount = 0,
  childrenHeaderRight,
  childrenFilters,
  childrenActivities,
}) => {
  // Action dropdown menu state
  const [activeMenuId, setActiveMenuId] = useState(null)
  const menuRef = useRef(null)

  // Move stage sub-modal
  const [movingStageItem, setMovingStageItem] = useState(null)
  const [targetStageId, setTargetStageId] = useState('')
  const [lostReason, setLostReason] = useState('')
  const [movingStageError, setMovingStageError] = useState(null)

  useEffect(() => {
    const handleOutsideClick = (e) => {
      if (menuRef.current && !menuRef.current.contains(e.target)) {
        setActiveMenuId(null)
      }
    }
    document.addEventListener('mousedown', handleOutsideClick)
    return () => document.removeEventListener('mousedown', handleOutsideClick)
  }, [])

  // Calculate pagination display
  const startItem = total === 0 ? 0 : (pageIndex - 1) * pageSize + 1
  const endItem = total === 0 ? 0 : Math.min(pageIndex * pageSize, total)
  const maxPage = Math.ceil(total / pageSize) || 1

  const handleConfirmMoveStage = async () => {
    if (!movingStageItem || !targetStageId) return
    const targetStage = stages.find((s) => s.id === targetStageId)
    const isLost = targetStage?.kind === 3 || targetStage?.kind === 'Lost'
    if (isLost && !lostReason.trim()) {
      setMovingStageError('Vui lòng nhập lý do thất bại')
      return
    }

    try {
      await onMoveStage(movingStageItem.id, {
        stageId: targetStageId,
        lostReason: isLost ? lostReason.trim() : null,
      })
      setMovingStageItem(null)
      setTargetStageId('')
      setLostReason('')
      setMovingStageError(null)
    } catch (err) {
      setMovingStageError(err?.response?.data?.message || err?.message || 'Lỗi chuyển giai đoạn')
    }
  }

  return (
    <div className="crm-opp-table-container" data-testid="opportunity-table-container">
      {/* Tabs Header */}
      <div className="crm-opp-tabs-bar">
        <div className="crm-opp-tabs" role="tablist">
          <button
            type="button"
            role="tab"
            aria-selected={activeTab === 'all'}
            className={`crm-opp-tab ${activeTab === 'all' ? 'crm-opp-tab--active' : ''}`}
            onClick={() => onTabChange('all')}
            data-testid="tab-all"
          >
            Tất cả
            {stats && <span className="crm-opp-tab-badge" data-testid="badge-tab-all">{stats.total ?? 0}</span>}
          </button>

          <button
            type="button"
            role="tab"
            aria-selected={activeTab === 'open'}
            className={`crm-opp-tab ${activeTab === 'open' ? 'crm-opp-tab--active' : ''}`}
            onClick={() => onTabChange('open')}
            data-testid="tab-open"
          >
            Đang mở
            {stats && <span className="crm-opp-tab-badge" data-testid="badge-tab-open">{stats.open ?? 0}</span>}
          </button>

          <button
            type="button"
            role="tab"
            aria-selected={activeTab === 'archived'}
            className={`crm-opp-tab ${activeTab === 'archived' ? 'crm-opp-tab--active' : ''}`}
            onClick={() => onTabChange('archived')}
            data-testid="tab-archived"
          >
            Lưu trữ
          </button>

          <button
            type="button"
            role="tab"
            aria-selected={activeTab === 'activities'}
            className={`crm-opp-tab ${activeTab === 'activities' ? 'crm-opp-tab--active' : ''}`}
            onClick={() => onTabChange('activities')}
            data-testid="tab-activities"
          >
            Hoạt động
            {stats && stats.activity > 0 && (
              <span className="crm-opp-tab-badge" data-testid="badge-tab-activities">{stats.activity}</span>
            )}
          </button>
        </div>

        {/* Toolbar buttons */}
        <div className="crm-opp-toolbar-actions">
          {!isReadOnly && (
            <button
              type="button"
              className="crm-opp-btn crm-opp-btn--primary"
              onClick={onOpenCreate}
              data-testid="btn-create-opportunity"
            >
              + Cơ hội
            </button>
          )}

          <button
            type="button"
            className="crm-opp-btn crm-opp-btn--secondary"
            onClick={onRefresh}
            data-testid="btn-refresh-opportunities"
            title="Làm mới danh sách"
          >
            🔄 Làm mới
          </button>

          <button
            type="button"
            className={`crm-opp-btn crm-opp-btn--secondary ${showFilters ? 'crm-opp-btn--active' : ''}`}
            onClick={onToggleFilters}
            data-testid="btn-toggle-filters"
          >
            🔍 Lọc
            {activeFilterCount > 0 && (
              <span className="crm-opp-filter-badge" data-testid="active-filter-badge">
                {activeFilterCount}
              </span>
            )}
          </button>

          {childrenHeaderRight}
        </div>
      </div>

      {/* Tab Hoạt động */}
      {activeTab === 'activities' ? (
        <div
          className="crm-opp-activities-pane"
          data-testid="opportunity-activities"
        >
          {childrenActivities || (
            <div className="crm-opp-activities-placeholder">
              <h4>📋 Hoạt động & Nhắc việc</h4>
              <p>Tab Hoạt động quản lý các việc cần làm và nhắc hẹn.</p>
            </div>
          )}
        </div>
      ) : (
        <>
          {childrenFilters}
          {/* Main Table Body */}
          <div className="crm-opp-table-wrap">
            <table className="crm-opp-table" data-testid="opportunity-table">
              <thead>
                <tr>
                  {columns.title !== false && <th>Tên cơ hội</th>}
                  {columns.customer !== false && <th>Khách hàng</th>}
                  {columns.phone !== false && <th>Số điện thoại</th>}
                  {columns.source !== false && <th>Kênh nguồn</th>}
                  {columns.assignee !== false && <th>Phụ trách</th>}
                  {columns.watchers !== false && <th>Theo dõi</th>}
                  {columns.stage !== false && <th>Giai đoạn</th>}
                  {columns.value !== false && <th className="text-right">Giá trị</th>}
                  {columns.lastActivity !== false && <th>Hoạt động cuối</th>}
                  {!isReadOnly && columns.actions !== false && <th className="text-center">Hành động</th>}
                </tr>
              </thead>
              <tbody>
                {loading && (
                  <tr>
                    <td colSpan={10} className="crm-opp-table-state-cell">
                      <div className="crm-opp-loading-indicator" data-testid="table-loading-indicator">
                        Đang tải danh sách cơ hội...
                      </div>
                    </td>
                  </tr>
                )}

                {error && !loading && (
                  <tr>
                    <td colSpan={10} className="crm-opp-table-state-cell">
                      <div className="crm-opp-error-banner" data-testid="table-error-banner">
                        <span>⚠️ {error}</span>
                        <button
                          type="button"
                          className="crm-opp-btn crm-opp-btn--sm crm-opp-btn--danger"
                          onClick={onRefresh}
                          data-testid="btn-retry-table"
                        >
                          Thử lại
                        </button>
                      </div>
                    </td>
                  </tr>
                )}

                {!loading && !error && items.length === 0 && (
                  <tr>
                    <td colSpan={10} className="crm-opp-table-state-cell">
                      <div className="crm-opp-empty-state" data-testid="table-empty-state">
                        <span className="crm-opp-empty-icon">📭</span>
                        <p>Không có cơ hội nào phù hợp với bộ lọc.</p>
                      </div>
                    </td>
                  </tr>
                )}

                {!loading && !error && items.map((opp) => (
                  <tr
                    key={opp.id}
                    className="crm-opp-row"
                    onClick={() => onRowClick?.(opp.id)}
                    data-testid={`opp-row-${opp.id}`}
                  >
                    {/* Tên cơ hội */}
                    {columns.title !== false && (
                      <td className="crm-opp-cell-title">
                        <strong className="crm-opp-title-link" data-testid={`opp-title-${opp.id}`}>
                          {opp.title}
                        </strong>
                        {opp.isArchived && (
                          <span className="crm-opp-badge crm-opp-badge--archived">Lưu trữ</span>
                        )}
                      </td>
                    )}

                    {/* Khách hàng */}
                    {columns.customer !== false && (
                      <td className="crm-opp-cell-customer">
                        {opp.crmCustomerId ? (
                          <Link
                            to={`/customers/${opp.crmCustomerId}`}
                            className="crm-opp-link"
                            onClick={(e) => e.stopPropagation()}
                            data-testid={`opp-customer-${opp.id}`}
                          >
                            👤 {opp.customerName || 'Khách hàng'}
                          </Link>
                        ) : (
                          <span>—</span>
                        )}
                      </td>
                    )}

                    {/* SĐT */}
                    {columns.phone !== false && (
                      <td className="crm-opp-cell-phone" data-testid={`opp-phone-${opp.id}`}>
                        {opp.customerPhoneE164 || '—'}
                      </td>
                    )}

                    {/* Kênh nguồn */}
                    {columns.source !== false && (
                      <td className="crm-opp-cell-source">
                        {opp.channelPlatform ? (
                          <span className="crm-opp-source-tag">
                            <SourceBadge
                              item={{
                                platform: opp.channelPlatform,
                                kind: opp.source === 2 ? 1 : 2,
                                id: opp.id,
                              }}
                            />
                            <span>{opp.channelName || 'Kênh'}</span>
                          </span>
                        ) : (
                          <span className="crm-opp-source-tag">✍️ Thủ công</span>
                        )}
                      </td>
                    )}

                    {/* Phụ trách */}
                    {columns.assignee !== false && (
                      <td className="crm-opp-cell-assignee" data-testid={`opp-assignee-${opp.id}`}>
                        {opp.assignedTo || <span className="crm-opp-text-muted">Chưa gán</span>}
                      </td>
                    )}

                    {/* Người theo dõi */}
                    {columns.watchers !== false && (
                      <td className="crm-opp-cell-watchers">
                        {opp.watcherUserIds?.length > 0 ? (
                          <span className="crm-opp-watchers-badge" title="Người theo dõi">
                            👁️ {opp.watcherUserIds.length}
                          </span>
                        ) : (
                          <span className="crm-opp-text-muted">—</span>
                        )}
                      </td>
                    )}

                    {/* Giai đoạn */}
                    {columns.stage !== false && (
                      <td className="crm-opp-cell-stage">
                        <span
                          className="crm-opp-stage-pill"
                          style={{
                            backgroundColor: opp.stageColor ? `${opp.stageColor}22` : '#e0e7ff',
                            color: opp.stageColor || '#4338ca',
                          }}
                          data-testid={`opp-stage-${opp.id}`}
                        >
                          {opp.stageName || 'Giai đoạn'}
                        </span>
                      </td>
                    )}

                    {/* Giá trị */}
                    {columns.value !== false && (
                      <td className="crm-opp-cell-value text-right" data-testid={`opp-value-${opp.id}`}>
                        <strong>{formatCurrencyVnd(opp.expectedValue)}</strong>
                      </td>
                    )}

                    {/* Hoạt động cuối */}
                    {columns.lastActivity !== false && (
                      <td className="crm-opp-cell-last-act">
                        <small>{formatVietnamDateTime(opp.lastActivityAtUtc || opp.createdAt)}</small>
                      </td>
                    )}

                    {/* Hành động (Admin/ContentManager/Reviewer) */}
                    {!isReadOnly && columns.actions !== false && (
                      <td
                        className="crm-opp-cell-actions text-center"
                        onClick={(e) => e.stopPropagation()}
                      >
                        <div className="crm-opp-menu-wrap" ref={activeMenuId === opp.id ? menuRef : null}>
                          <button
                            type="button"
                            className="crm-opp-menu-trigger"
                            onClick={() => setActiveMenuId(activeMenuId === opp.id ? null : opp.id)}
                            aria-label="Thao tác cơ hội"
                            data-testid={`btn-actions-${opp.id}`}
                          >
                            ⋮
                          </button>

                          {activeMenuId === opp.id && (
                            <div className="crm-opp-dropdown-menu" data-testid={`menu-actions-${opp.id}`}>
                              <button
                                type="button"
                                className="crm-opp-dropdown-item"
                                onClick={() => {
                                  setActiveMenuId(null)
                                  onEdit?.(opp)
                                }}
                                data-testid={`action-edit-${opp.id}`}
                              >
                                ✏️ Chỉnh sửa
                              </button>

                              <button
                                type="button"
                                className="crm-opp-dropdown-item"
                                onClick={() => {
                                  setActiveMenuId(null)
                                  setMovingStageItem(opp)
                                  setTargetStageId(opp.stageId || '')
                                  setLostReason('')
                                  setMovingStageError(null)
                                }}
                                data-testid={`action-move-stage-${opp.id}`}
                              >
                                🔄 Chuyển giai đoạn
                              </button>

                              {opp.isArchived ? (
                                <button
                                  type="button"
                                  className="crm-opp-dropdown-item"
                                  onClick={() => {
                                    setActiveMenuId(null)
                                    onUnarchive?.(opp.id)
                                  }}
                                  data-testid={`action-unarchive-${opp.id}`}
                                >
                                  📂 Bỏ lưu trữ
                                </button>
                              ) : (
                                <button
                                  type="button"
                                  className="crm-opp-dropdown-item"
                                  onClick={() => {
                                    setActiveMenuId(null)
                                    onArchive?.(opp.id)
                                  }}
                                  data-testid={`action-archive-${opp.id}`}
                                >
                                  📦 Lưu trữ
                                </button>
                              )}

                              <button
                                type="button"
                                className="crm-opp-dropdown-item crm-opp-dropdown-item--danger"
                                onClick={() => {
                                  setActiveMenuId(null)
                                  const confirmed = typeof window.confirm === 'function' ? window.confirm(`Xác nhận xoá cơ hội "${opp.title}"?`) : true
                                  if (confirmed) {
                                    onDelete?.(opp.id)
                                  }
                                }}
                                data-testid={`action-delete-${opp.id}`}
                              >
                                🗑️ Xoá
                              </button>
                            </div>
                          )}
                        </div>
                      </td>
                    )}
                  </tr>
                ))}
              </tbody>
            </table>
          </div>

          {/* Phân trang dạng "x - y / tổng" */}
          <div className="crm-opp-pagination-bar" data-testid="opportunity-pagination">
            <span className="crm-opp-pagination-info" data-testid="pagination-info">
              {startItem} - {endItem} / {total}
            </span>

            <div className="crm-opp-pagination-nav">
              <button
                type="button"
                className="crm-opp-btn crm-opp-btn--sm crm-opp-btn--secondary"
                disabled={pageIndex <= 1 || loading}
                onClick={() => onPageChange(pageIndex - 1)}
                data-testid="btn-prev-page"
              >
                ← Trước
              </button>

              <span className="crm-opp-pagination-page">
                Trang {pageIndex} / {maxPage}
              </span>

              <button
                type="button"
                className="crm-opp-btn crm-opp-btn--sm crm-opp-btn--secondary"
                disabled={pageIndex >= maxPage || loading}
                onClick={() => onPageChange(pageIndex + 1)}
                data-testid="btn-next-page"
              >
                Sau →
              </button>
            </div>
          </div>
        </>
      )}

      {/* Move Stage Dialog */}
      {movingStageItem && (
        <div className="crm-opp-modal-backdrop" role="dialog" aria-modal="true" data-testid="move-stage-modal">
          <div className="crm-opp-modal-content crm-opp-modal-content--sm">
            <div className="crm-opp-modal-header">
              <h3 className="crm-opp-modal-title">Chuyển giai đoạn cơ hội</h3>
              <button
                type="button"
                className="crm-opp-modal-close"
                onClick={() => setMovingStageItem(null)}
              >
                ✕
              </button>
            </div>

            <div className="crm-opp-modal-body">
              {movingStageError && (
                <div className="crm-opp-form-error">⚠️ {movingStageError}</div>
              )}
              <p>
                Cơ hội: <strong>{movingStageItem.title}</strong>
              </p>

              <div className="crm-opp-form-group">
                <label className="crm-opp-form-label" htmlFor="move-stage-select">
                  Chọn giai đoạn đích
                </label>
                <select
                  id="move-stage-select"
                  className="crm-opp-form-control"
                  value={targetStageId}
                  onChange={(e) => setTargetStageId(e.target.value)}
                  data-testid="select-target-stage"
                >
                  {stages.map((st) => (
                    <option key={st.id} value={st.id}>
                      {st.name} ({st.kind === 2 ? 'Won' : st.kind === 3 ? 'Lost' : 'Open'})
                    </option>
                  ))}
                </select>
              </div>

              {/* Nếu chọn giai đoạn Lost, bắt buộc nhập LostReason */}
              {stages.find((s) => s.id === targetStageId)?.kind === 3 && (
                <div className="crm-opp-form-group">
                  <label className="crm-opp-form-label" htmlFor="move-stage-lost-reason">
                    Lý do thất bại <span className="crm-opp-req">*</span>
                  </label>
                  <textarea
                    id="move-stage-lost-reason"
                    className="crm-opp-form-control"
                    rows="2"
                    placeholder="Nhập lý do cơ hội thất bại..."
                    value={lostReason}
                    onChange={(e) => setLostReason(e.target.value)}
                    required
                    data-testid="input-lost-reason"
                  />
                </div>
              )}
            </div>

            <div className="crm-opp-modal-footer">
              <button
                type="button"
                className="crm-opp-btn crm-opp-btn--secondary"
                onClick={() => setMovingStageItem(null)}
              >
                Huỷ
              </button>
              <button
                type="button"
                className="crm-opp-btn crm-opp-btn--primary"
                onClick={handleConfirmMoveStage}
                data-testid="btn-confirm-move-stage"
              >
                Xác nhận
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  )
}

export default OpportunityTable
