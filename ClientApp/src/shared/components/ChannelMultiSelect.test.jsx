import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'
import ChannelMultiSelect from './ChannelMultiSelect'

const CHANNELS = Object.freeze([
  { id: 'z', pageName: 'Zeta' },
  { id: 'v1', pageName: 'VNi Hà Nội' },
  { id: 'a', pageName: 'Alpha' },
  { id: 'v2', pageName: 'vni sài gòn' },
  { id: 'b', pageName: 'Beta VNI' },
])

const rowNames = () => [...document.querySelectorAll('.channel-multi-select__name')].map((el) => el.textContent)

async function open(user) {
  await user.click(screen.getByRole('button', { name: 'Chọn page' }))
}

describe('ChannelMultiSelect ordering', () => {
  it('lists VNi pages first (A→Z), then the rest A→Z, without mutating the prop', async () => {
    const user = userEvent.setup()
    render(<ChannelMultiSelect channels={CHANNELS} value={[]} onChange={vi.fn()} />)
    await open(user)

    expect(rowNames()).toEqual(['Beta VNI', 'VNi Hà Nội', 'vni sài gòn', 'Alpha', 'Zeta'])
    expect(CHANNELS.map((c) => c.id)).toEqual(['z', 'v1', 'a', 'v2', 'b'])
  })

  it('keeps VNi first while filtering', async () => {
    const user = userEvent.setup()
    render(<ChannelMultiSelect channels={CHANNELS} value={[]} onChange={vi.fn()} />)
    await open(user)
    // 'a' (không dấu) chỉ khớp Beta VNI, Alpha, Zeta: nhóm VNi vẫn đứng trước dù input để Zeta lên đầu.
    await user.type(screen.getByRole('searchbox', { name: 'Tìm page' }), 'a')

    expect(rowNames()).toEqual(['Beta VNI', 'Alpha', 'Zeta'])

    await user.clear(screen.getByRole('searchbox', { name: 'Tìm page' }))
    await user.type(screen.getByRole('searchbox', { name: 'Tìm page' }), 'et')
    expect(rowNames()).toEqual(['Beta VNI', 'Zeta'])
  })

  it('selects all filtered results in display order', async () => {
    const onChange = vi.fn()
    const user = userEvent.setup()
    render(<ChannelMultiSelect channels={CHANNELS} value={[]} onChange={onChange} />)
    await open(user)
    await user.click(screen.getByRole('button', { name: 'Chọn hết kết quả' }))

    expect(onChange).toHaveBeenCalledWith(['b', 'v1', 'v2', 'a', 'z'])
  })

  it('shows the selected-page chips in the same order', () => {
    render(<ChannelMultiSelect channels={CHANNELS} value={['z', 'a', 'v1']} onChange={vi.fn()} />)

    const chips = [...document.querySelectorAll('.channel-multi-select__chip')].map((el) => el.textContent)
    expect(chips).toEqual(['VNi Hà Nội×', 'Alpha×', 'Zeta×'])
  })
})
