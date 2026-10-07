import { render } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import PageContextListPage from '../pages/PageContextListPage'

vi.mock('@/shared/stores/toastStore', () => ({ toast: { success: vi.fn(), error: vi.fn() } }))
vi.mock('@/modules/prompt-templates/hooks/usePromptTemplates', () => ({
  usePromptTemplateList: () => ({ data: { items: [] } }),
}))
// 7 kênh chưa có context; 2 kênh VNi đứng cuối theo thứ tự input nên trước đây không lọt top 5 của banner.
vi.mock('@/modules/social-channels/hooks/useSocialChannels', () => ({
  useSocialChannelAll: () => ({
    data: [
      { id: '1', pageName: 'Alpha' }, { id: '2', pageName: 'Beta' }, { id: '3', pageName: 'Gamma' },
      { id: '4', pageName: 'Delta' }, { id: '5', pageName: 'Epsilon' }, { id: '6', pageName: 'Zeta VNi' },
      { id: '7', pageName: 'vni Hà Nội' }, { id: '8', pageName: 'Inactive VNi', isActive: false },
    ],
  }),
}))
vi.mock('../components/PageContextFormModal', () => ({ default: () => null }))

const { mutation } = vi.hoisted(() => ({
  mutation: () => ({ mutate: () => {}, mutateAsync: () => {}, isPending: false }),
}))
vi.mock('../hooks/usePageContexts', () => ({
  useCreatePageContext: mutation,
  useDeletePageContext: mutation,
  useImportPageContexts: mutation,
  useUpdatePageContext: mutation,
  usePageContextList: () => ({
    data: { items: [] }, isLoading: false, isError: false, error: null, refetch: vi.fn(),
  }),
}))

describe('PageContextListPage missing-context banner', () => {
  it('names VNi pages first, then A→Z, within the first five shown', () => {
    render(<PageContextListPage />)

    const banner = document.querySelector('.alert-warning').textContent
    expect(banner).toContain('7 page chưa có context: vni Hà Nội, Zeta VNi, Alpha, Beta, Delta…')
    expect(banner).not.toContain('Inactive VNi')
  })
})
