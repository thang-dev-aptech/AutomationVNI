import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import MediaPage from '../pages/MediaPage'
import { useMediaAssets } from '../hooks/useMediaAssets'
import { mediaFolderApi } from '../services/mediaFolderApi'
import {
  CHANNELS,
  FOLDER_A_ROOT,
  FOLDER_B_ROOT,
  wrapPaged,
} from './mediaFolderExplorerFixtures'
import { toast } from '@/shared/stores/toastStore'

const FOLDER_A_ROOT_WITH_PAGE = { ...FOLDER_A_ROOT, pageName: 'Page A' }
const FOLDER_B_ROOT_WITH_PAGE = { ...FOLDER_B_ROOT, pageName: 'Page B' }

const CAPTION_FIVE_LINES = [
  'Dòng mở đầu hấp dẫn',
  'Chi tiết sản phẩm nổi bật',
  'Lợi ích cho khách hàng',
  'Kêu gọi hành động rõ ràng',
  'Liên hệ hoặc đặt hàng ngay',
].join('\n')

const FILE_ASSET = {
  id: 'file-caption-1',
  fileName: 'shot.jpg',
  originalFileName: 'shot.jpg',
  mimeType: 'image/jpeg',
  publicUrl: 'https://cdn.example/shot.jpg',
  caption: null,
  keywords: [],
  tags: {},
  source: 1,
  fileSize: 1024,
  createdAt: '2026-01-01T00:00:00Z',
}

const generateCaptionMutate = vi.fn()
const updateMutate = vi.fn()

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
    useMediaAssets: vi.fn(() => ({
      data: { items: [], total: 0, size: 48 },
      isLoading: false,
      isError: false,
      error: null,
      refetch: vi.fn(),
    })),
    useCreateMediaAsset: noop,
    useUploadMediaAsset: noop,
    useUploadMediaBatch: noop,
    useUpdateMediaAsset: () => ({ mutateAsync: updateMutate, isPending: false }),
    useDeleteMediaAsset: noop,
    useMoveMediaAssets: noop,
    useAnalyzeMediaAsset: noop,
    useAnalyzeAllMediaAssets: noop,
    useAnalyzeLayoutFolder: noop,
    useAnalyzeLayout: noop,
    useGenerateCaption: () => ({ mutateAsync: generateCaptionMutate, isPending: false }),
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
      searchGlobal: vi.fn(),
      create: vi.fn(),
      update: vi.fn(),
      softDelete: vi.fn(),
    },
  }
})

function renderPage() {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false, refetchOnWindowFocus: false } },
  })
  return render(
    <QueryClientProvider client={queryClient}>
      <MediaPage />
    </QueryClientProvider>,
  )
}

async function openDetailsPopup() {
  await screen.findByText('Campaign A')
  fireEvent.click(screen.getByRole('button', { name: 'Chi tiết' }))
  expect(await screen.findByText('Caption Facebook (5 dòng)')).toBeInTheDocument()
}

describe('MEDIA-CAPTION-01 MediaPage caption block (AC 1d94cc25)', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    generateCaptionMutate.mockResolvedValue({
      ...FILE_ASSET,
      caption: CAPTION_FIVE_LINES,
    })
    updateMutate.mockResolvedValue({
      ...FILE_ASSET,
      caption: CAPTION_FIVE_LINES,
    })
    useMediaAssets.mockImplementation(() => ({
      data: { items: [FILE_ASSET], total: 1, size: 48 },
      isLoading: false,
      isError: false,
      error: null,
      refetch: vi.fn(),
    }))
    mediaFolderApi.searchGlobal.mockResolvedValue(wrapPaged([]))
    mediaFolderApi.pageRoots.mockResolvedValue(
      wrapPaged([FOLDER_A_ROOT_WITH_PAGE, FOLDER_B_ROOT_WITH_PAGE]),
    )
    mediaFolderApi.children.mockResolvedValue(wrapPaged([]))
    mediaFolderApi.breadcrumb.mockResolvedValue({ data: { success: true, data: { ancestors: [] } } })
    Object.defineProperty(navigator, 'clipboard', {
      configurable: true,
      value: { writeText: vi.fn().mockResolvedValue(undefined) },
    })
  })

  it('shows Caption Facebook block with disclaimer in Chi tiết media', async () => {
    renderPage()
    await openDetailsPopup()
    expect(screen.getByText('AI có thể sai tên/số liệu — kiểm tra trước khi đăng')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: '✍️ Sinh caption' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Lưu' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Copy' })).toBeInTheDocument()
  })

  it('Sinh caption calls generateCaption API and fills textarea', async () => {
    renderPage()
    await openDetailsPopup()
    fireEvent.click(screen.getByRole('button', { name: '✍️ Sinh caption' }))
    await waitFor(() => {
      expect(generateCaptionMutate).toHaveBeenCalledWith(FILE_ASSET.id)
    })
    await waitFor(() => {
      expect(screen.getByPlaceholderText(/Chưa có caption/)).toHaveValue(CAPTION_FIVE_LINES)
    })
    expect(toast.success).toHaveBeenCalled()
  })

  it('Lưu sends edited caption via update mutation', async () => {
    const edited = `${CAPTION_FIVE_LINES}\n(đã sửa)`
    updateMutate.mockResolvedValue({ ...FILE_ASSET, caption: edited })
    renderPage()
    await openDetailsPopup()
    const textarea = screen.getByPlaceholderText(/Chưa có caption/)
    fireEvent.change(textarea, { target: { value: edited } })
    fireEvent.click(screen.getByRole('button', { name: 'Lưu' }))
    await waitFor(() => {
      expect(updateMutate).toHaveBeenCalledWith({
        id: FILE_ASSET.id,
        payload: { caption: edited },
      })
    })
    expect(toast.success).toHaveBeenCalled()
  })

  it('Copy writes caption draft to clipboard', async () => {
    renderPage()
    await openDetailsPopup()
    const draft = 'một\nhai\nba\nbốn\nnăm'
    fireEvent.change(screen.getByPlaceholderText(/Chưa có caption/), {
      target: { value: draft },
    })
    fireEvent.click(screen.getByRole('button', { name: 'Copy' }))
    await waitFor(() => {
      expect(navigator.clipboard.writeText).toHaveBeenCalledWith(draft)
    })
    expect(toast.success).toHaveBeenCalled()
  })
})
