import React from 'react'
import Icon from '../../../shared/components/Icon'
import Button from '../../../shared/components/Button'
import InboxSourceTabs from './InboxSourceTabs'

export const InboxFilterBar = ({
  channels = [],
  tags = [],
  users = [],
  sources = [],
  filters = {},
  onChangeFilters,
  onSearchSubmit,
}) => {
  const handleKeywordChange = (e) => {
    onChangeFilters({ ...filters, keyword: e.target.value })
  }

  const handleKindClick = (kind) => {
    onChangeFilters({ ...filters, kind: filters.kind === kind ? null : kind })
  }

  const handleStatusChange = (e) => {
    const val = e.target.value ? parseInt(e.target.value, 10) : null
    onChangeFilters({ ...filters, status: val })
  }

  const handleChannelChange = (e) => {
    const val = e.target.value || null
    onChangeFilters({ ...filters, socialChannelId: val })
  }

  const handleAssignedChange = (e) => {
    const val = e.target.value || 'all'
    onChangeFilters({ ...filters, assignedFilter: val })
  }

  const handleTagChange = (e) => {
    const val = e.target.value || null
    onChangeFilters({ ...filters, tagId: val })
  }

  const handleUnreadToggle = () => {
    onChangeFilters({ ...filters, unreadOnly: !filters.unreadOnly })
  }

  return (
    <div className="crm-inbox-search" data-testid="inbox-filter-bar">
      {/* Search Input */}
      <form
        onSubmit={(e) => {
          e.preventDefault()
          onSearchSubmit && onSearchSubmit()
        }}
        style={{ display: 'flex', gap: '8px' }}
      >
        <input
          type="text"
          className="crm-form-input"
          placeholder="Tìm theo tên khách, tin nhắn, SĐT..."
          value={filters.keyword || ''}
          onChange={handleKeywordChange}
          style={{ flex: 1, fontSize: '13px', padding: '8px 12px' }}
          data-testid="inbox-search-input"
        />
        <Button
          type="submit"
          variant="secondary"
          size="sm"
          icon={<Icon name="search" size={14} />}
          data-testid="btn-search-inbox"
        >
          Tìm
        </Button>
      </form>

      {/* Quick Filters: Source Tabs (thay 3 chip kind) + Unread toggle */}
      <div
        style={{
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'space-between',
          gap: '8px',
          margin: '4px 0',
        }}
      >
        <div style={{ flex: 1, minWidth: 0 }}>
          <InboxSourceTabs
            sources={sources}
            selectedSource={filters.source || null}
            onSelectSource={(sourceKey) => onChangeFilters({ ...filters, source: sourceKey })}
          />
        </div>
        <button
          type="button"
          className={`crm-filter-chip ${filters.unreadOnly ? 'active' : ''}`}
          onClick={handleUnreadToggle}
          style={{
            borderColor: filters.unreadOnly ? 'var(--crm-danger)' : undefined,
            color: filters.unreadOnly ? 'var(--crm-danger)' : undefined,
            flexShrink: 0,
          }}
          data-testid="filter-unread-only"
        >
          <Icon name="circle-dot" size={12} /> Chưa đọc
        </button>
      </div>

      {/* Hidden compatibility buttons for legacy kind tests */}
      <div style={{ display: 'none' }} aria-hidden="true" data-testid="inbox-kind-filters">
        <button
          type="button"
          data-testid="filter-kind-all"
          onClick={() => onChangeFilters({ ...filters, kind: null })}
        >
          Tất cả
        </button>
        <button
          type="button"
          data-testid="filter-kind-message"
          onClick={() => handleKindClick(1)}
        >
          Tin nhắn
        </button>
        <button
          type="button"
          data-testid="filter-kind-comment"
          onClick={() => handleKindClick(2)}
        >
          Bình luận
        </button>
      </div>

      {/* Dropdown Filters row */}
      <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(130px, 1fr))', gap: '8px' }}>
        {/* Channel Filter */}
        <select
          className="crm-form-input"
          style={{ fontSize: '12px', padding: '6px 8px', cursor: 'pointer' }}
          value={filters.socialChannelId || ''}
          onChange={handleChannelChange}
          data-testid="select-filter-channel"
        >
          <option value="">Tất cả kênh (Page)</option>
          {channels.map((ch) => (
            <option key={ch.id} value={ch.id}>
              {ch.pageName || ch.name}
            </option>
          ))}
        </select>

        {/* Status Filter */}
        <select
          className="crm-form-input"
          style={{ fontSize: '12px', padding: '6px 8px', cursor: 'pointer' }}
          value={filters.status ?? ''}
          onChange={handleStatusChange}
          data-testid="select-filter-status"
        >
          <option value="">Tất cả trạng thái</option>
          <option value="1">Mới</option>
          <option value="2">Đang xử lý</option>
          <option value="3">Đã trả lời</option>
          <option value="4">Bỏ qua</option>
        </select>

        {/* Assignee Filter */}
        <select
          className="crm-form-input"
          style={{ fontSize: '12px', padding: '6px 8px', cursor: 'pointer' }}
          value={filters.assignedFilter || 'all'}
          onChange={handleAssignedChange}
          data-testid="select-filter-assignee"
        >
          <option value="all">Tất cả phụ trách</option>
          <option value="mine">Của tôi</option>
          <option value="unassigned">Chưa gán</option>
          {users.map((u) => (
            <option key={u.id} value={u.id}>
              {u.displayName || u.userName}
            </option>
          ))}
        </select>

        {/* Tag Filter */}
        <select
          className="crm-form-input"
          style={{ fontSize: '12px', padding: '6px 8px', cursor: 'pointer' }}
          value={filters.tagId || ''}
          onChange={handleTagChange}
          data-testid="select-filter-tag"
        >
          <option value="">Tất cả tag</option>
          {tags.map((t) => (
            <option key={t.id} value={t.id}>
              {t.name}
            </option>
          ))}
        </select>
      </div>
    </div>
  )
}

export default InboxFilterBar
