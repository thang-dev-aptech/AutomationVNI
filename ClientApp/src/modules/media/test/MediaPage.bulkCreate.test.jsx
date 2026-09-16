import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import MediaPage from '../pages/MediaPage'
import { usePermissions } from '@/shared/hooks/usePermissions'
import { mediaFolderApi } from '../services/mediaFolderApi'
import { CHANNELS, FOLDER_A_ROOT, wrapPaged } from './mediaFolderExplorerFixtures'

vi.mock('@/shared/hooks/usePermissions', () => ({
  usePermissions: vi.fn(),
}))

vi.mock('@/modules/social-channels/hooks/useSocialChannels', () => ({
  useSocialChannelAll: () => ({ data: CHANNELS }),
}))

vi.mock('@/modules/categories/hooks/useCategories', () => ({
  useCategoryList: () => ({ data: { items: [] } }),
}))

vi.mock('@/shared/stores/toastStore', () => ({
  toast: { success: vi.fn(), error: vi.fn() },
}))

vi.mock('@/shared/utils/confirmAction', () => ({
  confirmAction: vi.fn(() => true),
  CONFIRM_MESSAGES: { deleteMedia: () => '' },
}))

vi.mock('../components/AiBackgroundPromptModal', () => ({ default: () => null }))
vi.mock('../components/MediaUploadForm', () => ({ default: () => null }))
vi.mock('../components/MediaFolderFormModal', () => ({ default: () => null }))
vi.mock('../components/MediaFolderBulkCreateModal', () => ({ default: () => null }))
vi.mock('../components/MediaGrid', () => ({ default: () => null }))

vi.mock('../hooks/useMediaAssets', () => {
  const noop = () => ({ mutateAsync: vi.fn(), isPending: false })
  return {
    useMediaAssets: () => ({ data: { items: [] }, isLoading: false, isError: false, error: null, refetch: vi.fn() }),
    useCreateMediaAsset: noop,
    useUploadMediaAsset: noop,
    useUploadMediaBatch: noop,
    useUpdateMediaAsset: noop,
    useDeleteMediaAsset: noop,
    useMoveMediaAssets: noop,
    useAnalyzeMediaAsset: noop,
    useAnalyzeAllMediaAssets: noop,
    useAnalyzeLayoutFolder: noop,
    useAnalyzeLayout: noop,
  }
})

vi.mock('../services/mediaFolderApi', async (importOriginal) => {
  const actual = await importOriginal()
  return {
    ...actual,
    mediaFolderApi: {
      ...actual.mediaFolderApi,
      tree: vi.fn(),
      children: vi.fn(),
      breadcrumb: vi.fn(),
      create: vi.fn(),
      update: vi.fn(),
      softDelete: vi.fn(),
    },
  }
})

function renderMediaPage() {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false, refetchOnWindowFocus: false } },
  })
  return render(
    <QueryClientProvider client={queryClient}>
      <MediaPage />
    </QueryClientProvider>,
  )
}

describe('MEDIA-06-AC1 bulk-create trigger visibility', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    mediaFolderApi.tree.mockResolvedValue(wrapPaged([FOLDER_A_ROOT]))
    mediaFolderApi.children.mockResolvedValue(wrapPaged([FOLDER_A_ROOT]))
    mediaFolderApi.breadcrumb.mockResolvedValue({ data: { success: true, data: { ancestors: [] } } })
  })

  it('Admin/ContentManager (canManageMedia) sees the bulk-create trigger', async () => {
    usePermissions.mockReturnValue({ canManageMedia: true })
    renderMediaPage()

    expect(await screen.findByRole('button', { name: /Tạo hàng loạt/ })).toBeInTheDocument()
  })

  it('Viewer/Reviewer (not canManageMedia) never sees or can activate the bulk-create trigger', async () => {
    usePermissions.mockReturnValue({ canManageMedia: false })
    renderMediaPage()

    await screen.findByRole('button', { name: /Campaign A/ })
    expect(screen.queryByRole('button', { name: /Tạo hàng loạt/ })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /Tạo thư mục/ })).not.toBeInTheDocument()
  })
})
