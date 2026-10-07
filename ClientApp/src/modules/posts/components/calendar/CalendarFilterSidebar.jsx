import { useMemo, useState } from 'react'
import { sortChannelsVniFirst } from '@/shared/utils/channelSort'
import { getSocialPlatformLabel } from '@/modules/social-channels/constants/socialPlatform'
import {
  CALENDAR_POST_TYPES,
  CALENDAR_STATUS_GROUPS,
  CHANNEL_FILTER_MODES,
} from '../../constants/calendarStatus'

function Accordion({ title, open, onToggle, children }) {
  return (
    <div className={`cal-filter-acc${open ? ' is-open' : ''}`}>
      <button type="button" className="cal-filter-acc-head" onClick={onToggle} aria-expanded={open}>
        <span>{title}</span>
        <span className="cal-filter-acc-chevron" aria-hidden>{open ? '▾' : '▸'}</span>
      </button>
      {open ? <div className="cal-filter-acc-body">{children}</div> : null}
    </div>
  )
}

/**
 * Sidebar "Bộ lọc bài đăng" — Kênh/Nhóm, trạng thái (ánh xạ R-033), facets, loại bài.
 */
export default function CalendarFilterSidebar({
  channelMode,
  onChannelModeChange,
  channelSearch,
  onChannelSearchChange,
  channels = [],
  selectedChannelIds = [],
  onToggleChannel,
  groups = [],
  selectedGroupIds = [],
  onToggleGroup,
  statusKeys = [],
  onToggleStatus,
  authors = [],
  selectedAuthorIds = [],
  onToggleAuthor,
  categories = [],
  selectedCategoryIds = [],
  onToggleCategory,
  selectedPostTypes = [],
  onTogglePostType,
}) {
  const [openSections, setOpenSections] = useState({
    channels: true,
    status: true,
    authors: true,
    categories: true,
    types: true,
  })

  const toggleSection = (key) => {
    setOpenSections((prev) => ({ ...prev, [key]: !prev[key] }))
  }

  const filteredChannels = useMemo(() => {
    const q = (channelSearch || '').trim().toLowerCase()
    const list = sortChannelsVniFirst(channels)
    if (!q) return list
    return list.filter((ch) => {
      const name = (ch.pageName || '').toLowerCase()
      const ext = (ch.externalPageId || '').toLowerCase()
      return name.includes(q) || ext.includes(q)
    })
  }, [channels, channelSearch])

  const channelsByPlatform = useMemo(() => {
    const map = new Map()
    filteredChannels.forEach((ch) => {
      const label = getSocialPlatformLabel(ch.platform)
      if (!map.has(label)) map.set(label, [])
      map.get(label).push(ch)
    })
    return [...map.entries()]
  }, [filteredChannels])

  const filteredGroups = useMemo(() => {
    const q = (channelSearch || '').trim().toLowerCase()
    const list = [...(groups || [])].sort((a, b) =>
      (a.name || '').localeCompare(b.name || '', 'vi'))
    if (!q) return list
    return list.filter((g) => (g.name || '').toLowerCase().includes(q))
  }, [groups, channelSearch])

  return (
    <aside className="cal-filter-sidebar" aria-label="Bộ lọc bài đăng">
      <h2 className="cal-filter-title">Bộ lọc bài đăng</h2>

      <Accordion
        title="KÊNH ĐĂNG"
        open={openSections.channels}
        onToggle={() => toggleSection('channels')}
      >
        <div className="cal-filter-radio-row" role="radiogroup" aria-label="Chế độ lọc kênh">
          <label className="cal-filter-radio">
            <input
              type="radio"
              name="channel-mode"
              checked={channelMode === CHANNEL_FILTER_MODES.channel}
              onChange={() => onChannelModeChange?.(CHANNEL_FILTER_MODES.channel)}
            />
            <span>Kênh</span>
          </label>
          <label className="cal-filter-radio">
            <input
              type="radio"
              name="channel-mode"
              checked={channelMode === CHANNEL_FILTER_MODES.group}
              onChange={() => onChannelModeChange?.(CHANNEL_FILTER_MODES.group)}
            />
            <span>Nhóm kênh</span>
          </label>
        </div>

        <label className="cal-filter-search">
          <span className="sr-only">Tìm kênh</span>
          <input
            type="search"
            value={channelSearch}
            onChange={(e) => onChannelSearchChange?.(e.target.value)}
            placeholder={channelMode === CHANNEL_FILTER_MODES.group ? 'Tìm nhóm…' : 'Tìm kênh…'}
            aria-label="Tìm kênh"
          />
        </label>

        {channelMode === CHANNEL_FILTER_MODES.group ? (
          <ul className="cal-filter-list">
            {filteredGroups.length === 0 ? (
              <li className="cal-filter-empty">Không có nhóm</li>
            ) : (
              filteredGroups.map((g) => (
                <li key={g.id}>
                  <label className="cal-filter-check">
                    <input
                      type="checkbox"
                      checked={selectedGroupIds.includes(g.id)}
                      onChange={() => onToggleGroup?.(g.id)}
                    />
                    <span>{g.name}</span>
                    <span className="cal-filter-count">{g.channelCount ?? g.channels?.length ?? 0}</span>
                  </label>
                </li>
              ))
            )}
          </ul>
        ) : (
          <div className="cal-filter-platform-groups">
            {channelsByPlatform.length === 0 ? (
              <p className="cal-filter-empty">Không có kênh</p>
            ) : (
              channelsByPlatform.map(([platform, items]) => (
                <div key={platform} className="cal-filter-platform">
                  <div className="cal-filter-platform-label">{platform}</div>
                  <ul className="cal-filter-list">
                    {items.map((ch) => (
                      <li key={ch.id}>
                        <label className="cal-filter-check">
                          <input
                            type="checkbox"
                            checked={selectedChannelIds.includes(ch.id)}
                            onChange={() => onToggleChannel?.(ch.id)}
                          />
                          <span>{ch.pageName}</span>
                        </label>
                      </li>
                    ))}
                  </ul>
                </div>
              ))
            )}
          </div>
        )}
      </Accordion>

      <Accordion
        title="TRẠNG THÁI BÀI ĐĂNG"
        open={openSections.status}
        onToggle={() => toggleSection('status')}
      >
        <ul className="cal-filter-list">
          {CALENDAR_STATUS_GROUPS.map((group) => (
            <li key={group.key}>
              <label className="cal-filter-check">
                <input
                  type="checkbox"
                  checked={statusKeys.includes(group.key)}
                  onChange={() => onToggleStatus?.(group.key)}
                />
                <span
                  className="cal-filter-status-dot"
                  style={{ background: group.color }}
                  aria-hidden
                />
                <span>{group.label}</span>
              </label>
            </li>
          ))}
        </ul>
      </Accordion>

      <Accordion
        title="NGƯỜI ĐĂNG"
        open={openSections.authors}
        onToggle={() => toggleSection('authors')}
      >
        <ul className="cal-filter-list">
          {authors.length === 0 ? (
            <li className="cal-filter-empty">Không có trong khoảng</li>
          ) : (
            authors.map((a) => (
              <li key={a.userId}>
                <label className="cal-filter-check">
                  <input
                    type="checkbox"
                    checked={selectedAuthorIds.includes(a.userId)}
                    onChange={() => onToggleAuthor?.(a.userId)}
                  />
                  <span>{a.name}</span>
                  <span className="cal-filter-count">{a.count}</span>
                </label>
              </li>
            ))
          )}
        </ul>
      </Accordion>

      <Accordion
        title="CHỦ ĐỀ NỘI DUNG"
        open={openSections.categories}
        onToggle={() => toggleSection('categories')}
      >
        <ul className="cal-filter-list">
          {categories.length === 0 ? (
            <li className="cal-filter-empty">Không có trong khoảng</li>
          ) : (
            categories.map((c) => (
              <li key={c.categoryId}>
                <label className="cal-filter-check">
                  <input
                    type="checkbox"
                    checked={selectedCategoryIds.includes(c.categoryId)}
                    onChange={() => onToggleCategory?.(c.categoryId)}
                  />
                  <span>{c.name}</span>
                  <span className="cal-filter-count">{c.count}</span>
                </label>
              </li>
            ))
          )}
        </ul>
      </Accordion>

      <Accordion
        title="LOẠI BÀI ĐĂNG"
        open={openSections.types}
        onToggle={() => toggleSection('types')}
      >
        <ul className="cal-filter-list">
          {CALENDAR_POST_TYPES.map((type) => (
            <li key={type}>
              <label className="cal-filter-check">
                <input
                  type="checkbox"
                  checked={selectedPostTypes.includes(type)}
                  onChange={() => onTogglePostType?.(type)}
                />
                <span>{type}</span>
              </label>
            </li>
          ))}
        </ul>
      </Accordion>
    </aside>
  )
}
