import { useEffect, useMemo, useRef, useState } from 'react'
import './ChannelMultiSelect.css'

/**
 * Dropdown multi-select kênh (page) — giống combobox: ô trigger → panel search + checkbox.
 */
export default function ChannelMultiSelect({
  channels = [],
  value = [],
  onChange,
  getBadge,
  label = 'Chọn page',
  placeholder = 'Chọn page',
  maxHeight = 280,
}) {
  const [open, setOpen] = useState(false)
  const [query, setQuery] = useState('')
  const rootRef = useRef(null)
  const searchRef = useRef(null)
  const selected = useMemo(() => new Set(value), [value])

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

  return (
    <div className="channel-multi-select" ref={rootRef}>
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
              placeholder="Tìm page..."
              aria-label="Tìm page"
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
            {channels.length === 0 && (
              <p className="channel-multi-select__empty">Chưa có kênh nào.</p>
            )}
            {channels.length > 0 && filtered.length === 0 && (
              <p className="channel-multi-select__empty">Không tìm thấy “{query}”.</p>
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
