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

describe('BulkCreatePage generation methods (user request 2026-10-02)', () => {
  afterEach(() => {
    FEATURES.aiFullImage = false
  })

  it('keeps the page but hides "Sinh toàn bộ bằng AI" and never offers the Media option', () => {
    render(<MemoryRouter><BulkCreatePage /></MemoryRouter>)

    expect(screen.queryByRole('button', { name: /Sinh toàn bộ bằng AI/ })).not.toBeInTheDocument()
    expect(screen.getByRole('button', { name: /AI sinh text, ghép vào ảnh mẫu/ })).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /Dùng ảnh có sẵn trong Media/ })).not.toBeInTheDocument()
    // Template là mặc định nên ô số ảnh của Template hiện, checkbox RAG (chỉ của Full AI) không hiện.
    expect(screen.getByLabelText(/Số ảnh mỗi bài/)).toBeInTheDocument()
    expect(screen.queryByText(/tự tìm thêm 2–3 ảnh từ kho media/)).not.toBeInTheDocument()
  })

  it('shows "Sinh toàn bộ bằng AI" again when the flag is on, still without the Media option', () => {
    FEATURES.aiFullImage = true
    render(<MemoryRouter><BulkCreatePage /></MemoryRouter>)

    expect(screen.getByRole('button', { name: /Sinh toàn bộ bằng AI/ })).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /Dùng ảnh có sẵn trong Media/ })).not.toBeInTheDocument()
  })
})
