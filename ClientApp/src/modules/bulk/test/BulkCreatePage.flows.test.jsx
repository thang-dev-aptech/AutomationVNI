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

  it('offers only "Sinh toàn bộ bằng AI" by default: no template, no Media option', () => {
    render(<MemoryRouter><BulkCreatePage /></MemoryRouter>)

    expect(screen.getByRole('button', { name: /Sinh toàn bộ bằng AI/ })).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /AI sinh text, ghép vào ảnh mẫu/ })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /Dùng ảnh có sẵn trong Media/ })).not.toBeInTheDocument()
    // Full AI là mặc định: checkbox RAG của Full AI hiện, ô số ảnh của Template không hiện.
    expect(screen.getByText(/tự tìm thêm 2–3 ảnh từ kho media/)).toBeInTheDocument()
    expect(screen.queryByLabelText(/Số ảnh mỗi bài/)).not.toBeInTheDocument()
  })

  it('shows the template method again when its flag is on, still without the Media option', () => {
    FEATURES.aiTemplate = true
    render(<MemoryRouter><BulkCreatePage /></MemoryRouter>)

    expect(screen.getByRole('button', { name: /Sinh toàn bộ bằng AI/ })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /AI sinh text, ghép vào ảnh mẫu/ })).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /Dùng ảnh có sẵn trong Media/ })).not.toBeInTheDocument()
  })
})
