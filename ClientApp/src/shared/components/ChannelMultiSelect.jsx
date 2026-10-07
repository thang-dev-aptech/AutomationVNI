import { useEffect, useMemo, useRef, useState } from 'react'
import { useChannelGroupAll } from '@/modules/social-channels/hooks/useChannelGroups'
import { sortChannelsVniFirst } from '@/shared/utils/channelSort'
import './ChannelMultiSelect.css'

/**
 * Thành viên nhóm giao với danh sách kênh màn hình cho phép chọn.
 * Export để test + revert-to-prove (bỏ lọc → lẫn id ngoài `channels`).
 */
export function availableGroupMemberIds(group, allowedIdSet) {
  const members = group?.channels ?? group?.Channels ?? []
  return members
    .map((m) => m.id ?? m.Id)
    .filter((id) => id != null && allowedIdSet.has(id))
}

export function groupUnavailableCount(group, allowedIdSet) {
  const members = group?.channels ?? group?.Channels ?? []
  return members.filter((m) => {
    const id = m.id ?? m.Id
    return id != null && !allowedIdSet.has(id)
  }).length
}

export function groupSelectionState(availableIds, selectedSet) {
  if (availableIds.length === 0) return 'empty'
  const selectedCount = availableIds.filter((id) => selectedSet.has(id)).length
  if (selectedCount === 0) return 'none'
  if (selectedCount === availableIds.length) return 'all'
  return 'partial'
}

/**
 * Dropdown multi-select kênh (page) — giống combobox: ô trigger → panel search + checkbox.
 * Tuỳ chọn phần "Nhóm kênh" (snapshot thành viên khả dụng vào value).
 */
export default function ChannelMultiSelect({
  channels: channelsProp = [],
  value = [],
  onChange,
  getBadge,
  label = 'Chọn page',
  placeholder = 'Chọn page',
  maxHeight = 280,
  enableGroups = true,
}) {
  const [open, setOpen] = useState(false)
  const [query, setQuery] = useState('')
  const rootRef = useRef(null)
  const searchRef = useRef(null)
  const selected = useMemo(() => new Set(value), [value])
  const channels = useMemo(() => sortChannelsVniFirst(channelsProp), [channelsProp])
  const allowedIds = useMemo(() => new Set(channels.map((c) => c.id)), [channels])

  const { data: groupsRaw = [] } = useChannelGroupAll({ enabled: enableGroups })

  const groupRows = useMemo(() => {
    if (!enableGroups) return []
    return (groupsRaw ?? []).map((g) => {
      const availableIds = availableGroupMemberIds(g, allowedIds)
      const unavailable = groupUnavailableCount(g, allowedIds)
      const state = groupSelectionState(availableIds, selected)
      return {
        id: g.id,
        name: g.name || g.Name || 'Nhóm',
        availableIds,
        unavailable,
        state,
        disabled: availableIds.length === 0,
      }
    })
  }, [enableGroups, groupsRaw, allowedIds, selected])

  const filteredGroups = useMemo(() => {
    const q = query.trim().toLowerCase()
    if (!q) return groupRows
    return groupRows.filter((g) => g.name.toLowerCase().includes(q))
  }, [groupRows, query])

  const filtered = useMemo(() => {
    const q = query.trim().toLowerCase()
    if (!q) return channels
    return channels.filter((ch) => {
      const name = (ch.pageName || ch.name || '').toLowerCase()
      return name.includes(q)
    })
  }, [channels, query])

  const selectedChannels = useMemo(
    () => channels.filter((c) => selected.has(c.id)),
    [channels, selected],
  )

  const triggerText = useMemo(() => {
    if (selectedChannels.length === 0) return placeholder
    if (selectedChannels.length === 1) {
      return selectedChannels[0].pageName || selectedChannels[0].name || '1 page'
    }
    return `Đã chọn ${selectedChannels.length} page`
  }, [selectedChannels, placeholder])

  useEffect(() => {
    if (!open) return undefined
    const onDoc = (e) => {
      if (rootRef.current && !rootRef.current.contains(e.target)) setOpen(false)
    }
    const onKey = (e) => {
      if (e.key === 'Escape') setOpen(false)
    }
    document.addEventListener('mousedown', onDoc)
    document.addEventListener('keydown', onKey)
    return () => {
      document.removeEventListener('mousedown', onDoc)
      document.removeEventListener('keydown', onKey)
    }
  }, [open])

  useEffect(() => {
    if (open) {
      setQuery('')
      requestAnimationFrame(() => searchRef.current?.focus())
    }
  }, [open])

  const toggle = (id) => {
    if (selected.has(id)) onChange(value.filter((x) => x !== id))
    else onChange([...value, id])
  }

  const toggleGroup = (row) => {
    if (row.disabled) return
    if (row.state === 'all') {
      const drop = new Set(row.availableIds)
      onChange(value.filter((id) => !drop.has(id)))
      return
    }
    const next = new Set(value)
    row.availableIds.forEach((id) => next.add(id))
    onChange([...next])
  }

  const filteredIds = filtered.map((c) => c.id)
  const allFilteredSelected =
    filteredIds.length > 0 && filteredIds.every((id) => selected.has(id))

  const selectAllFiltered = () => {
    const next = new Set(value)
    filteredIds.forEach((id) => next.add(id))
    onChange([...next])
  }

  const clearFiltered = () => {
    const drop = new Set(filteredIds)
    onChange(value.filter((id) => !drop.has(id)))
  }

  const showEmpty =
    channels.length === 0
    && (!enableGroups || groupRows.length === 0)
  const showNoMatch =
    !showEmpty
    && filtered.length === 0
    && filteredGroups.length === 0

  return (
    <div className="channel-multi-select" ref={rootRef} data-testid="channel-multi-select">
      {label && (
        <label className="channel-multi-select__field-label">{label}</label>
      )}

      <button
        type="button"
        className={`channel-multi-select__trigger${open ? ' is-open' : ''}${value.length ? ' has-value' : ''}`}
        onClick={() => setOpen((v) => !v)}
        aria-expanded={open}
        aria-haspopup="listbox"
      >
        <span className="channel-multi-select__trigger-text">{triggerText}</span>
        <span className="channel-multi-select__chevron" aria-hidden>▾</span>
      </button>

      {open && (
        <div className="channel-multi-select__dropdown" role="listbox" aria-multiselectable="true">
          <div className="channel-multi-select__search-wrap">
            <input
              ref={searchRef}
              type="search"
              className="channel-multi-select__search"
              value={query}
              onChange={(e) => setQuery(e.target.value)}
              placeholder={enableGroups ? 'Tìm nhóm hoặc page...' : 'Tìm page...'}
              aria-label={enableGroups ? 'Tìm nhóm hoặc page' : 'Tìm page'}
              onClick={(e) => e.stopPropagation()}
            />
            <span className="channel-multi-select__search-icon" aria-hidden>⌕</span>
          </div>

          <div className="channel-multi-select__dropdown-actions">
            <button
              type="button"
              className="channel-multi-select__link-btn"
              onClick={allFilteredSelected ? clearFiltered : selectAllFiltered}
              disabled={filteredIds.length === 0}
            >
              {allFilteredSelected ? 'Bỏ chọn kết quả' : 'Chọn hết kết quả'}
            </button>
            {value.length > 0 && (
              <button
                type="button"
                className="channel-multi-select__link-btn"
                onClick={() => onChange([])}
              >
                Xóa tất cả
              </button>
            )}
          </div>

          <div
            className="channel-multi-select__list"
            style={{ maxHeight: typeof maxHeight === 'number' ? `${maxHeight}px` : maxHeight }}
            onWheel={(e) => e.stopPropagation()}
          >
            {showEmpty && (
              <p className="channel-multi-select__empty">Chưa có kênh nào.</p>
            )}
            {showNoMatch && (
              <p className="channel-multi-select__empty">Không tìm thấy “{query}”.</p>
            )}

            {enableGroups && filteredGroups.length > 0 && (
              <div className="channel-multi-select__groups" data-testid="channel-groups-section">
                <div className="channel-multi-select__section-label">Nhóm kênh</div>
                {filteredGroups.map((g) => (
                  <button
                    key={g.id}
                    type="button"
                    className={`channel-multi-select__group-row is-${g.state}${g.disabled ? ' is-disabled' : ''}`}
                    data-testid={`channel-group-${g.id}`}
                    data-state={g.state}
                    disabled={g.disabled}
                    onClick={() => toggleGroup(g)}
                  >
                    <span
                      className={`channel-multi-select__group-mark is-${g.state}`}
                      aria-hidden
                    />
                    <span className="channel-multi-select__name">{g.name}</span>
                    <span className="channel-multi-select__group-meta">
                      {g.availableIds.length} kênh
                      {g.unavailable > 0
                        ? ` · ${g.unavailable} kênh không áp dụng ở đây`
                        : ''}
                    </span>
                  </button>
                ))}
              </div>
            )}

            {filtered.length > 0 && enableGroups && filteredGroups.length > 0 && (
              <div className="channel-multi-select__section-label">Kênh</div>
            )}

            {filtered.map((ch) => {
              const checked = selected.has(ch.id)
              const badge = typeof getBadge === 'function' ? getBadge(ch) : null
              return (
                <label
                  key={ch.id}
                  className={`channel-multi-select__row${checked ? ' is-selected' : ''}`}
                >
                  <input
                    type="checkbox"
                    checked={checked}
                    onChange={() => toggle(ch.id)}
                  />
                  <span className="channel-multi-select__name">
                    {ch.pageName || ch.name || ch.id}
                  </span>
                  {badge?.label && (
                    <span
                      className={`channel-multi-select__badge is-${badge.tone || 'muted'}`}
                      title={badge.title || ''}
                    >
                      {badge.label}
                    </span>
                  )}
                </label>
              )
            })}
          </div>
        </div>
      )}

      {selectedChannels.length > 1 && !open && (
        <div className="channel-multi-select__chips">
          {selectedChannels.slice(0, 6).map((ch) => (
            <button
              key={ch.id}
              type="button"
              className="channel-multi-select__chip"
              onClick={() => toggle(ch.id)}
              title="Bỏ chọn"
            >
              {ch.pageName || ch.name}
              <span aria-hidden>×</span>
            </button>
          ))}
          {selectedChannels.length > 6 && (
            <span className="channel-multi-select__chip-more">
              +{selectedChannels.length - 6}
            </span>
          )}
        </div>
      )}
    </div>
  )
}
