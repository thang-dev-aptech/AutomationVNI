import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'
import AiBackgroundPromptModal from '../components/AiBackgroundPromptModal'

vi.mock('@/shared/stores/toastStore', () => ({ toast: { success: vi.fn(), error: vi.fn(), warning: vi.fn() } }))
// PageContext hiển thị bằng TÊN KÊNH (không phải BrandName): thứ tự phải theo nhãn đang hiển thị.
vi.mock('@/modules/page-contexts/hooks/usePageContexts', () => ({
  usePageContextList: () => ({
    data: {
      items: [
        { id: 'c1', socialChannelId: 'z', brandName: 'AAA brand' },
        { id: 'c2', socialChannelId: 'v1', brandName: 'ZZZ brand' },
        { id: 'c3', socialChannelId: 'a', brandName: 'MMM brand' },
        { id: 'c4', socialChannelId: 'v2', brandName: 'BBB brand' },
      ],
    },
  }),
}))
vi.mock('@/modules/social-channels/hooks/useSocialChannels', () => ({
  useSocialChannelAll: () => ({
    data: [
      { id: 'z', pageName: 'Zeta' },
      { id: 'v1', pageName: 'VNi Hà Nội' },
      { id: 'a', pageName: 'Alpha' },
      { id: 'v2', pageName: 'Beta VNI' },
    ],
  }),
}))
vi.mock('@/modules/prompt-templates/hooks/usePromptTemplates', () => ({
  usePromptTemplateList: () => ({ data: { items: [] } }),
}))

const pageLabels = () => screen.getAllByRole('checkbox').slice(1).map((cb) => cb.closest('label').textContent.trim())

describe('AiBackgroundPromptModal page checklist order', () => {
  it('orders by displayed channel name, VNi first then A→Z', () => {
    render(<AiBackgroundPromptModal open onClose={vi.fn()} />)

    expect(pageLabels()).toEqual(['Beta VNI', 'VNi Hà Nội', 'Alpha', 'Zeta'])
  })

  it('keeps the order while searching', async () => {
    const user = userEvent.setup()
    render(<AiBackgroundPromptModal open onClose={vi.fn()} />)
    await user.type(screen.getByLabelText('Chọn kênh (được chọn nhiều)'), 'a')

    expect(pageLabels()).toEqual(['Beta VNI', 'Alpha', 'Zeta'])
  })
})
