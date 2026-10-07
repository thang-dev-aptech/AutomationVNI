import { render, screen } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { FEATURES } from '@/shared/config/features'
import BulkCreatePage from '../pages/BulkCreatePage'

vi.mock('@/shared/stores/toastStore', () => ({
  toast: { success: vi.fn(), error: vi.fn(), warning: vi.fn() },
}))
vi.mock('@/modules/social-channels/hooks/useSocialChannels', () => ({
  useSocialChannelAll: () => ({
    data: [{ id: 'page-1', pageName: 'Page Một', platform: 1 }],
    isLoading: false,
    isError: false,
    refetch: vi.fn(),
  }),
}))
vi.mock('@/modules/social-channels/hooks/useChannelGroups', () => ({
  useChannelGroupAll: () => ({ data: [], isLoading: false }),
}))
vi.mock('@/modules/prompt-templates/hooks/usePromptTemplates', () => ({
  usePromptTemplateList: () => ({ data: { items: [] }, isLoading: false }),
}))
vi.mock('@/modules/categories/hooks/useCategories', () => ({
  useCategoryList: () => ({ data: { items: [] }, isLoading: false }),
}))
vi.mock('@/modules/page-contexts/hooks/usePageContexts', () => ({
  usePageContextList: () => ({ data: { items: [] }, isLoading: false }),
}))
vi.mock('../hooks/useBulk', () => ({
  useBulkCreate: () => ({ mutateAsync: vi.fn(), isPending: false }),
  useBulkImport: () => ({ mutateAsync: vi.fn(), isPending: false }),
}))

const DEFAULTS = { ...FEATURES }

describe('BulkCreatePage generation methods (user request 2026-10-03)', () => {
  afterEach(() => {
    Object.assign(FEATURES, DEFAULTS)
  })

  it('offers only "Sinh toàn bộ bằng AI" by default (disabled), no template, no Media option', () => {
    render(<MemoryRouter><BulkCreatePage /></MemoryRouter>)

    const fullAi = screen.getByRole('button', { name: /Sinh toàn bộ bằng AI/ })
    expect(fullAi).toBeDisabled()
    expect(fullAi).toHaveTextContent('Tính năng tạm thời tắt')
    expect(screen.getAllByText('Tính năng tạm thời tắt').length).toBeGreaterThanOrEqual(1)
    expect(screen.queryByRole('button', { name: /AI sinh text, ghép vào ảnh mẫu/ })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /Dùng ảnh có sẵn trong Media/ })).not.toBeInTheDocument()
    // Full AI vẫn là flow mặc định trên state: checkbox RAG hiện, ô số ảnh Template không hiện.
    expect(screen.getByText(/tự tìm thêm 2–3 ảnh từ kho media/)).toBeInTheDocument()
    expect(screen.queryByLabelText(/Số ảnh mỗi bài/)).not.toBeInTheDocument()
    const lockedActions = screen.getAllByRole('button', { name: 'Tính năng tạm thời tắt' })
    expect(lockedActions.length).toBeGreaterThanOrEqual(2)
    lockedActions.forEach((btn) => expect(btn).toBeDisabled())
  })

  it('shows the template method again when its flag is on, still without the Media option', () => {
    FEATURES.aiTemplate = true
    render(<MemoryRouter><BulkCreatePage /></MemoryRouter>)

    expect(screen.getByRole('button', { name: /Sinh toàn bộ bằng AI/ })).toBeDisabled()
    expect(screen.getByRole('button', { name: /AI sinh text, ghép vào ảnh mẫu/ })).toBeDisabled()
    expect(screen.queryByRole('button', { name: /Dùng ảnh có sẵn trong Media/ })).not.toBeInTheDocument()
  })
})
