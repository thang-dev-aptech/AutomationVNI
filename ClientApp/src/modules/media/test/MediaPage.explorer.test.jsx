import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import MediaPage from '../pages/MediaPage'
import { mediaFolderApi } from '../services/mediaFolderApi'
import {
  CHANNELS,
  FOLDER_A_ROOT,
  FOLDER_B_ROOT,
  wrapPaged,
} from './mediaFolderExplorerFixtures'

const FOLDER_A_ROOT_WITH_PAGE = { ...FOLDER_A_ROOT, pageName: 'Page A' }
const FOLDER_B_ROOT_WITH_PAGE = { ...FOLDER_B_ROOT, pageName: 'Page B' }

vi.mock('@/shared/hooks/usePermissions', () => ({
  usePermissions: () => ({ canManageMedia: true }),
}))

vi.mock('@/modules/social-channels/hooks/useSocialChannels', () => ({
  useSocialChannelAll: () => ({ data: CHANNELS }),
}))

vi.mock('@/modules/categories/hooks/useCategories', () => ({
  useCategoryList: () => ({ data: { items: [] } }),
}))

vi.mock('@/shared/stores/toastStore', () => ({
  toast: { success: vi.fn(), error: vi.fn(), warning: vi.fn() },
}))

vi.mock('@/shared/utils/confirmAction', () => ({
  confirmAction: vi.fn(() => true),
  CONFIRM_MESSAGES: { deleteMedia: () => '' },
}))

vi.mock('../components/AiBackgroundPromptModal', () => ({ default: () => null }))
vi.mock('../components/MediaUploadForm', () => ({ default: () => null }))
vi.mock('../components/MediaFolderFormModal', () => ({ default: () => null }))
vi.mock('../components/MoveMediaFolderModal', () => ({ default: () => null }))
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
      pageRoots: vi.fn(),
      children: vi.fn(),
      breadcrumb: vi.fn(),
      create: vi.fn(),
      update: vi.fn(),
      softDelete: vi.fn(),
    },
  }
})

describe('MEDIA-04 MediaPage cross-Page browser flow', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    mediaFolderApi.pageRoots.mockResolvedValue(
      wrapPaged([FOLDER_A_ROOT_WITH_PAGE, FOLDER_B_ROOT_WITH_PAGE]),
    )
    mediaFolderApi.children.mockResolvedValue(wrapPaged([]))
    mediaFolderApi.breadcrumb.mockResolvedValue({ data: { success: true, data: { ancestors: [] } } })
  })

  it('MEDIA-04-AC1: top level loads page-roots (one card per Page), never /api/MediaFolder/tree', async () => {
    const queryClient = new QueryClient({
      defaultOptions: { queries: { retry: false, refetchOnWindowFocus: false } },
    })
    render(
      <QueryClientProvider client={queryClient}>
        <MediaPage />
      </QueryClientProvider>,
    )

    await screen.findByText('Campaign A')
    expect(screen.getByText('Campaign B')).toBeInTheDocument()
    expect(screen.getByText('Page A')).toBeInTheDocument()
    expect(screen.getByText('Page B')).toBeInTheDocument()

    expect(mediaFolderApi.pageRoots).toHaveBeenCalled()
    expect(mediaFolderApi.tree).not.toHaveBeenCalled()
    // Top level shows no folder is "open" yet — children/breadcrumb only fire once a folder is entered.
    expect(mediaFolderApi.children).not.toHaveBeenCalled()
    expect(mediaFolderApi.breadcrumb).not.toHaveBeenCalled()
    expect(screen.getByRole('navigation', { name: 'Đường dẫn thư mục' })).toBeInTheDocument()
  })
})
