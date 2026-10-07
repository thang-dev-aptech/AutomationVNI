import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import ChannelMultiSelect, {
  availableGroupMemberIds,
  groupSelectionState,
  groupUnavailableCount,
} from './ChannelMultiSelect'

vi.mock('@/modules/social-channels/hooks/useChannelGroups', () => ({
  useChannelGroupAll: vi.fn(),
}))

import { useChannelGroupAll } from '@/modules/social-channels/hooks/useChannelGroups'

const CHANNELS = Object.freeze([
  { id: 'z', pageName: 'Zeta' },
  { id: 'v1', pageName: 'VNi Hà Nội' },
  { id: 'a', pageName: 'Alpha' },
  { id: 'v2', pageName: 'vni sài gòn' },
  { id: 'b', pageName: 'Beta VNI' },
])

const GROUP_CHANNELS = Object.freeze([
  { id: 'A', pageName: 'Page A' },
  { id: 'B', pageName: 'Page B' },
  { id: 'C', pageName: 'Page C' },
])

const GROUPS = Object.freeze([
  {
    id: 'G1',
    name: 'Nhóm G1',
    channels: [{ id: 'A' }, { id: 'B' }, { id: 'X' }],
  },
  {
    id: 'G2',
    name: 'Nhóm G2',
    channels: [{ id: 'X' }, { id: 'Y' }],
  },
])

const channelRowNames = () =>
  [...document.querySelectorAll('.channel-multi-select__row .channel-multi-select__name')].map(
    (el) => el.textContent,
  )

async function open(user) {
  const trigger = screen.getByTestId('channel-multi-select').querySelector(
    '.channel-multi-select__trigger',
  )
  await user.click(trigger)
}

describe('ChannelMultiSelect ordering', () => {
  beforeEach(() => {
    useChannelGroupAll.mockReturnValue({ data: [] })
  })

  it('lists VNi pages first (A→Z), then the rest A→Z, without mutating the prop', async () => {
    const user = userEvent.setup()
    render(
      <ChannelMultiSelect
        channels={CHANNELS}
        value={[]}
        onChange={vi.fn()}
        enableGroups={false}
      />,
    )
    await open(user)

    expect(channelRowNames()).toEqual(['Beta VNI', 'VNi Hà Nội', 'vni sài gòn', 'Alpha', 'Zeta'])
    expect(CHANNELS.map((c) => c.id)).toEqual(['z', 'v1', 'a', 'v2', 'b'])
  })

  it('keeps VNi first while filtering', async () => {
    const user = userEvent.setup()
    render(
      <ChannelMultiSelect
        channels={CHANNELS}
        value={[]}
        onChange={vi.fn()}
        enableGroups={false}
      />,
    )
    await open(user)
    await user.type(screen.getByRole('searchbox', { name: 'Tìm page' }), 'a')

    expect(channelRowNames()).toEqual(['Beta VNI', 'Alpha', 'Zeta'])

    await user.clear(screen.getByRole('searchbox', { name: 'Tìm page' }))
    await user.type(screen.getByRole('searchbox', { name: 'Tìm page' }), 'et')
    expect(channelRowNames()).toEqual(['Beta VNI', 'Zeta'])
  })

  it('selects all filtered results in display order', async () => {
    const onChange = vi.fn()
    const user = userEvent.setup()
    render(
      <ChannelMultiSelect
        channels={CHANNELS}
        value={[]}
        onChange={onChange}
        enableGroups={false}
      />,
    )
    await open(user)
    await user.click(screen.getByRole('button', { name: 'Chọn hết kết quả' }))

    expect(onChange).toHaveBeenCalledWith(['b', 'v1', 'v2', 'a', 'z'])
  })

  it('shows the selected-page chips in the same order', () => {
    render(
      <ChannelMultiSelect
        channels={CHANNELS}
        value={['z', 'a', 'v1']}
        onChange={vi.fn()}
        enableGroups={false}
      />,
    )

    const chips = [...document.querySelectorAll('.channel-multi-select__chip')].map((el) => el.textContent)
    expect(chips).toEqual(['VNi Hà Nội×', 'Alpha×', 'Zeta×'])
  })
})

describe('ChannelMultiSelect group pick — available channels only', () => {
  beforeEach(() => {
    useChannelGroupAll.mockReturnValue({ data: GROUPS })
  })

  it('adds only members present in channels; never emits outsider or group ids', async () => {
    const onChange = vi.fn()
    const user = userEvent.setup()
    render(
      <ChannelMultiSelect
        channels={GROUP_CHANNELS}
        value={[]}
        onChange={onChange}
      />,
    )
    await open(user)

    const g1 = screen.getByTestId('channel-group-G1')
    expect(g1).toHaveTextContent('2 kênh')
    expect(g1).toHaveTextContent('1 kênh không áp dụng ở đây')

    const g2 = screen.getByTestId('channel-group-G2')
    expect(g2).toBeDisabled()

    await user.click(g2)
    expect(onChange).not.toHaveBeenCalled()

    await user.click(g1)
    expect(onChange).toHaveBeenCalledTimes(1)
    const next = onChange.mock.calls[0][0]
    expect(next).toEqual(['A', 'B'])
    expect(next).not.toContain('X')
    expect(next).not.toContain('G1')
  })

  it('keeps prior selection and dedupes when adding a group', async () => {
    const onChange = vi.fn()
    const user = userEvent.setup()
    render(
      <ChannelMultiSelect
        channels={GROUP_CHANNELS}
        value={['C']}
        onChange={onChange}
      />,
    )
    await open(user)
    await user.click(screen.getByTestId('channel-group-G1'))
    expect(onChange).toHaveBeenCalledWith(['C', 'A', 'B'])
  })

  it('revert-to-prove: availableGroupMemberIds filters by allowed set', () => {
    const allowed = new Set(['A', 'B', 'C'])
    expect(availableGroupMemberIds(GROUPS[0], allowed)).toEqual(['A', 'B'])
    expect(groupUnavailableCount(GROUPS[0], allowed)).toBe(1)
    expect(availableGroupMemberIds(GROUPS[1], allowed)).toEqual([])
    expect(groupUnavailableCount(GROUPS[1], allowed)).toBe(2)
  })
})

describe('ChannelMultiSelect group pick — UI behavior', () => {
  beforeEach(() => {
    useChannelGroupAll.mockReturnValue({ data: GROUPS })
  })

  it('renders group section, filters groups by search, toggles add/remove', async () => {
    const onChange = vi.fn()
    const user = userEvent.setup()
    const { rerender } = render(
      <ChannelMultiSelect
        channels={GROUP_CHANNELS}
        value={[]}
        onChange={onChange}
      />,
    )
    await open(user)

    expect(screen.getByTestId('channel-groups-section')).toBeInTheDocument()
    expect(screen.getByText('Nhóm kênh')).toBeInTheDocument()
    expect(screen.getByTestId('channel-group-G1')).toHaveTextContent('Nhóm G1')

    await user.type(screen.getByRole('searchbox', { name: 'Tìm nhóm hoặc page' }), 'G2')
    expect(screen.queryByTestId('channel-group-G1')).not.toBeInTheDocument()
    expect(screen.getByTestId('channel-group-G2')).toBeInTheDocument()

    await user.clear(screen.getByRole('searchbox', { name: 'Tìm nhóm hoặc page' }))
    await user.click(screen.getByTestId('channel-group-G1'))
    expect(onChange).toHaveBeenLastCalledWith(['A', 'B'])

    onChange.mockClear()
    rerender(
      <ChannelMultiSelect
        channels={GROUP_CHANNELS}
        value={['A', 'B']}
        onChange={onChange}
      />,
    )
    expect(screen.getByTestId('channel-group-G1')).toHaveAttribute('data-state', 'all')
    await user.click(screen.getByTestId('channel-group-G1'))
    expect(onChange).toHaveBeenCalledWith([])
  })

  it('adds missing members when value already has some; preserves extras', async () => {
    const onChange = vi.fn()
    const user = userEvent.setup()
    render(
      <ChannelMultiSelect
        channels={GROUP_CHANNELS}
        value={['A', 'C']}
        onChange={onChange}
      />,
    )
    await open(user)
    expect(screen.getByTestId('channel-group-G1')).toHaveAttribute('data-state', 'partial')
    await user.click(screen.getByTestId('channel-group-G1'))
    expect(onChange).toHaveBeenCalledWith(['A', 'C', 'B'])
  })

  it('derives none / partial / all from value', () => {
    expect(groupSelectionState(['A', 'B'], new Set())).toBe('none')
    expect(groupSelectionState(['A', 'B'], new Set(['A']))).toBe('partial')
    expect(groupSelectionState(['A', 'B'], new Set(['A', 'B']))).toBe('all')
    expect(groupSelectionState([], new Set(['A']))).toBe('empty')
  })

  it('hides group section when enableGroups is false', async () => {
    const user = userEvent.setup()
    render(
      <ChannelMultiSelect
        channels={GROUP_CHANNELS}
        value={[]}
        onChange={vi.fn()}
        enableGroups={false}
      />,
    )
    await open(user)
    expect(screen.queryByTestId('channel-groups-section')).not.toBeInTheDocument()
    expect(screen.queryByText('Nhóm kênh')).not.toBeInTheDocument()
    expect(useChannelGroupAll).toHaveBeenCalledWith({ enabled: false })
  })

  it('keeps single-channel toggle and clear-all', async () => {
    const onChange = vi.fn()
    const user = userEvent.setup()
    render(
      <ChannelMultiSelect
        channels={GROUP_CHANNELS}
        value={['A']}
        onChange={onChange}
      />,
    )
    await open(user)
    await user.click(screen.getByLabelText('Page B'))
    expect(onChange).toHaveBeenCalledWith(['A', 'B'])

    onChange.mockClear()
    await user.click(screen.getByRole('button', { name: 'Xóa tất cả' }))
    expect(onChange).toHaveBeenCalledWith([])
  })
})

describe('CampaignFormModal disables snapshot groups', () => {
  it('passes enableGroups={false}; other consumers keep the default on', async () => {
    const fs = await import('node:fs')
    const path = await import('node:path')
    const { fileURLToPath } = await import('node:url')
    const here = path.dirname(fileURLToPath(import.meta.url))
    const root = path.resolve(here, '../..')

    const campaign = fs.readFileSync(
      path.join(root, 'modules/campaigns/components/CampaignFormModal.jsx'),
      'utf8',
    )
    expect(campaign).toMatch(/enableGroups=\{false\}/)

    const consumers = [
      'modules/posts/components/PostCreateForm.jsx',
      'modules/posts/components/PostFromMediaForm.jsx',
      'modules/bulk/pages/BulkCreatePage.jsx',
      'modules/bulk/pages/BulkChungChiPage.jsx',
      'modules/content-crawl/components/ApproveArticleModal.jsx',
      'modules/news-site/components/FanpageModal.jsx',
      'modules/social-channels/components/ChannelGroupTab.jsx',
    ]
    for (const rel of consumers) {
      const src = fs.readFileSync(path.join(root, rel), 'utf8')
      expect(src, rel).not.toMatch(/enableGroups=\{false\}/)
      expect(src, rel).toMatch(/ChannelMultiSelect/)
    }
  })
})
