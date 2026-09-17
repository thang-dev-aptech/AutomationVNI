import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import MediaPage from '../pages/MediaPage'
import { mediaFolderApi } from '../services/mediaFolderApi'
import {
  CHANNELS,
  FOLDER_A_GRAND,
  FOLDER_A_ROOT,
  FOLDER_B_ROOT,
  PAGE_A,
  wrapPaged,
} from './mediaFolderExplorerFixtures'

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
  toast: { success: vi.fn(), error: vi.fn() },
}))

vi.mock('@/shared/utils/confirmAction', () => ({
  confirmAction: vi.fn(() => true),
  CONFIRM_MESSAGES: { deleteMedia: () => '' },
}))

vi.mock('../components/AiBackgroundPromptModal', () => ({ default: () => null }))
vi.mock('../components/MediaUploadForm', () => ({ default: () => null }))
vi.mock('../components/MediaFolderFormModal', () => ({ default: () => null }))
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

describe('MEDIA-04 MediaPage explorer flow', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    mediaFolderApi.tree.mockResolvedValue(wrapPaged([FOLDER_A_ROOT, FOLDER_A_GRAND, FOLDER_B_ROOT]))
    mediaFolderApi.children.mockResolvedValue(wrapPaged([FOLDER_A_ROOT]))
    mediaFolderApi.breadcrumb.mockResolvedValue({ data: { success: true, data: { ancestors: [] } } })
  })

  it('MEDIA-04-AC1: Media Page explorer does not call /api/MediaFolder/tree', async () => {
    const queryClient = new QueryClient({
      defaultOptions: { queries: { retry: false, refetchOnWindowFocus: false } },
    })
    render(
      <QueryClientProvider client={queryClient}>
        <MediaPage />
      </QueryClientProvider>,
    )

    // Regex neo vào icon 📁 để chỉ khớp nút tên thư mục, không khớp nút mở rộng
    // (aria-label của nút mở rộng cũng chứa tên folder, vd "Mở rộng thư mục Campaign A").
    await screen.findByRole('button', { name: /📁.*Campaign A/ })
    // 2 lời gọi hợp lệ ở mức root: useMediaFolderExplorer tự gọi children (phục vụ
    // folderOptions cho form upload) song song với MediaFolderTreeNav tự tải root eager
    // cho cây sidebar — không phải trùng lặp, là 2 nhu cầu khác nhau.
    expect(mediaFolderApi.children).toHaveBeenCalledTimes(2)
    expect(mediaFolderApi.children).toHaveBeenCalledWith(expect.objectContaining({
      socialChannelId: PAGE_A,
    }))
    expect(mediaFolderApi.tree).not.toHaveBeenCalled()
    expect(screen.queryByRole('button', { name: /Grand A/ })).not.toBeInTheDocument()
    expect(screen.getByRole('navigation', { name: 'Đường dẫn thư mục' })).toBeInTheDocument()
  })
})
