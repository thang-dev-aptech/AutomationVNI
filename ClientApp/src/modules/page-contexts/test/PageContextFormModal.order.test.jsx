import { render, screen, within } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import PageContextFormModal from '../components/PageContextFormModal'

vi.mock('@/modules/social-channels/hooks/useSocialChannels', () => ({
  useSocialChannelAll: () => ({
    data: [
      { id: 'z', pageName: 'Zeta' },
      { id: 'v1', pageName: 'VNi Hà Nội' },
      { id: 'a', pageName: 'Alpha' },
      { id: 'used', pageName: 'VNi Đã Có Context' },
    ],
  }),
}))
vi.mock('@/modules/prompt-templates/hooks/usePromptTemplates', () => ({
  usePromptTemplateList: () => ({ data: { items: [] } }),
}))
vi.mock('@/modules/media/hooks/useMediaAssets', () => ({ useMediaAssetAll: () => ({ data: [] }) }))
vi.mock('@/modules/media/hooks/useMediaFolders', () => ({ useMediaFolderTree: () => ({ data: [] }) }))

const options = () => within(screen.getByLabelText('Kênh MXH')).getAllByRole('option').map((o) => o.textContent)

describe('PageContextFormModal channel select order', () => {
  it('lists available channels VNi first then A→Z, still hiding channels that already have a context', () => {
    render(<PageContextFormModal open mode="create" unavailableChannelIds={['used']} onClose={vi.fn()} onSubmit={vi.fn()} />)

    expect(options()).toEqual(['Chọn kênh', 'VNi Hà Nội', 'Alpha', 'Zeta'])
  })

  it('applies the same order when editing', () => {
    render(<PageContextFormModal open mode="edit" initialData={{ socialChannelId: 'a' }} onClose={vi.fn()} onSubmit={vi.fn()} />)

    expect(options()).toEqual(['Chọn kênh', 'VNi Đã Có Context', 'VNi Hà Nội', 'Alpha', 'Zeta'])
  })
})
