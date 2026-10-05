import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import MediaPage from '../pages/MediaPage'
import { useMediaAssets } from '../hooks/useMediaAssets'
import { mediaFolderApi } from '../services/mediaFolderApi'
import { mediaCaptionJobApi } from '../services/mediaCaptionJobApi'
import { confirmAction } from '@/shared/utils/confirmAction'
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

vi.mock('../services/mediaCaptionJobApi', async (importOriginal) => {
  const actual = await importOriginal()
  return { ...actual, mediaCaptionJobApi: { create: vi.fn(), list: vi.fn(), get: vi.fn(), retryFailed: vi.fn(), retryItem: vi.fn() } }
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

function renderPage(entry = '/media') {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false, refetchOnWindowFocus: false } },
  })
  return render(
    <QueryClientProvider client={queryClient}>
      <MemoryRouter initialEntries={[entry]}>
        <Routes>
          <Route path="/media" element={<MediaPage />} />
          <Route path="/media/caption-jobs" element={<div>CAPTION JOB LIST</div>} />
          <Route path="/media/caption-jobs/:jobId" element={<div>CAPTION JOB PAGE</div>} />
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>,
  )
}

async function openDetailsPopup() {
  await screen.findByText('Campaign A')
  fireEvent.click(screen.getByRole('button', { name: 'Chi tiết' }))
  expect(await screen.findByText('Caption Facebook')).toBeInTheDocument()
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
      expect(generateCaptionMutate).toHaveBeenCalledWith({ id: FILE_ASSET.id })
    })
    // Đang ở danh sách gốc (không thuộc Page nào) ⇒ KHÔNG gửi socialChannelId.
    expect(generateCaptionMutate.mock.calls[0][0]).not.toHaveProperty('socialChannelId')
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

const DRIVE_FOLDER = 'd0000000-0000-4000-8000-000000000001'
const PAGE_FOLDER = 'e0000000-0000-4000-8000-000000000002'
const PAGE_ID = '11111111-1111-4111-8111-111111111111'

describe('MEDIA-CAPTION-03 image-caption-ui-test (AC 92587ba5): popup Chi tiết media', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    generateCaptionMutate.mockResolvedValue({ ...FILE_ASSET, caption: 'Bài mới' })
    useMediaAssets.mockImplementation(() => ({
      data: { items: [FILE_ASSET], total: 1, size: 48 },
      isLoading: false, isError: false, error: null, refetch: vi.fn(),
    }))
    mediaFolderApi.searchGlobal.mockResolvedValue(wrapPaged([]))
    mediaFolderApi.pageRoots.mockResolvedValue(wrapPaged([]))
    mediaFolderApi.children.mockResolvedValue(wrapPaged([]))
    mediaFolderApi.breadcrumb.mockResolvedValue({ data: { success: true, data: { ancestors: [] } } })
  })

  async function openDetailsIn(entry) {
    renderPage(entry)
    fireEvent.click(await screen.findByRole('button', { name: 'Chi tiết' }))
    await screen.findByText('Caption Facebook')
  }

  it('relabels the block: no longer says 5 dòng', async () => {
    await openDetailsIn(`/media?folder=${PAGE_FOLDER}&page=${PAGE_ID}`)

    expect(screen.getByText('Caption Facebook')).toBeInTheDocument()
    expect(screen.queryByText(/5 dòng/)).not.toBeInTheDocument()
  })

  it('sends the Page of the open folder when generating', async () => {
    await openDetailsIn(`/media?folder=${PAGE_FOLDER}&page=${PAGE_ID}`)
    fireEvent.click(screen.getByRole('button', { name: '✍️ Sinh caption' }))

    await waitFor(() => expect(generateCaptionMutate).toHaveBeenCalledWith({
      id: FILE_ASSET.id, socialChannelId: PAGE_ID,
    }))
  })

  it('sends no Page for a folder outside any Page (Google Drive tree)', async () => {
    await openDetailsIn(`/media?folder=${DRIVE_FOLDER}`)
    fireEvent.click(screen.getByRole('button', { name: '✍️ Sinh caption' }))

    await waitFor(() => expect(generateCaptionMutate).toHaveBeenCalledTimes(1))
    expect(generateCaptionMutate.mock.calls[0][0]).toEqual({ id: FILE_ASSET.id })
    expect(generateCaptionMutate.mock.calls[0][0]).not.toHaveProperty('socialChannelId')
  })

  it('409 on generate: error toast and the text being typed is kept', async () => {
    generateCaptionMutate.mockRejectedValueOnce(Object.assign(new Error('conflict'), {
      response: { status: 409, data: { errorCode: 'MEDIA_CAPTION_QUEUED', message: 'Ảnh đang trong hàng chờ sinh caption' } },
    }))
    await openDetailsIn(`/media?folder=${PAGE_FOLDER}&page=${PAGE_ID}`)
    const textarea = screen.getByPlaceholderText(/Chưa có caption/)
    fireEvent.change(textarea, { target: { value: 'đang gõ dở' } })

    fireEvent.click(screen.getByRole('button', { name: '✍️ Sinh caption' }))

    await waitFor(() => expect(toast.error).toHaveBeenCalledWith('Ảnh đang trong hàng chờ sinh caption'))
    expect(screen.getByPlaceholderText(/Chưa có caption/)).toHaveValue('đang gõ dở')
  })
})

describe('MEDIA-CAPTION-02 caption-job-ui-test (AC 07f817bc a,b): nút Sinh caption toàn bộ', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    confirmAction.mockReturnValue(true)
    useMediaAssets.mockImplementation(() => ({
      data: { items: [], total: 0, size: 48 },
      isLoading: false, isError: false, error: null, refetch: vi.fn(),
    }))
    mediaFolderApi.searchGlobal.mockResolvedValue(wrapPaged([]))
    mediaFolderApi.pageRoots.mockResolvedValue(wrapPaged([]))
    mediaFolderApi.children.mockResolvedValue(wrapPaged([]))
    mediaFolderApi.breadcrumb.mockResolvedValue({ data: { success: true, data: { ancestors: [] } } })
    mediaCaptionJobApi.create.mockResolvedValue({ data: { success: true, data: { id: 'job-1' } } })
  })

  it('(a) page-less (Drive) folder shows Sinh caption toàn bộ and hides Quét Vùng An Toàn', async () => {
    renderPage(`/media?folder=${DRIVE_FOLDER}`)

    expect(await screen.findByRole('button', { name: '✍️ Sinh caption toàn bộ' })).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: '✨ Quét Vùng An Toàn' })).not.toBeInTheDocument()
  })

  it('(a) Page folder keeps Quét Vùng An Toàn and hides Sinh caption toàn bộ', async () => {
    renderPage(`/media?folder=${PAGE_FOLDER}&page=${PAGE_ID}`)

    expect(await screen.findByRole('button', { name: '✨ Quét Vùng An Toàn' })).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: '✍️ Sinh caption toàn bộ' })).not.toBeInTheDocument()
  })

  it('(b) click + confirm creates the job for the current folder then navigates to the job page', async () => {
    renderPage(`/media?folder=${DRIVE_FOLDER}`)

    fireEvent.click(await screen.findByRole('button', { name: '✍️ Sinh caption toàn bộ' }))

    await waitFor(() => expect(mediaCaptionJobApi.create).toHaveBeenCalledWith(DRIVE_FOLDER))
    expect(confirmAction).toHaveBeenCalledWith(expect.stringContaining('thư mục con'))
    expect(await screen.findByText('CAPTION JOB PAGE')).toBeInTheDocument()
  })

  it('(b) declining the confirm creates nothing', async () => {
    confirmAction.mockReturnValue(false)
    renderPage(`/media?folder=${DRIVE_FOLDER}`)

    fireEvent.click(await screen.findByRole('button', { name: '✍️ Sinh caption toàn bộ' }))

    expect(mediaCaptionJobApi.create).not.toHaveBeenCalled()
    expect(screen.queryByText('CAPTION JOB PAGE')).not.toBeInTheDocument()
  })

  it('links to the job list', async () => {
    renderPage()
    expect(await screen.findByRole('link', { name: 'Job sinh caption' })).toHaveAttribute('href', '/media/caption-jobs')
  })
})

describe('MEDIA-CAPTION-02 caption-lock-ui-test (AC a811682b)', () => {
  const QUEUED_ASSET = { ...FILE_ASSET, caption: 'cũ', captionQueued: true }

  beforeEach(() => {
    vi.clearAllMocks()
    confirmAction.mockReturnValue(true)
    mediaFolderApi.searchGlobal.mockResolvedValue(wrapPaged([]))
    mediaFolderApi.pageRoots.mockResolvedValue(wrapPaged([FOLDER_A_ROOT_WITH_PAGE]))
    mediaFolderApi.children.mockResolvedValue(wrapPaged([]))
    mediaFolderApi.breadcrumb.mockResolvedValue({ data: { success: true, data: { ancestors: [] } } })
    Object.defineProperty(navigator, 'clipboard', {
      configurable: true,
      value: { writeText: vi.fn().mockResolvedValue(undefined) },
    })
  })

  const useAsset = (asset) => useMediaAssets.mockImplementation(() => ({
    data: { items: [asset], total: 1, size: 48 },
    isLoading: false, isError: false, error: null, refetch: vi.fn(),
  }))

  it('(a) captionQueued=true disables textarea, Lưu and Sinh lại; shows notice + job link; Copy stays enabled', async () => {
    useAsset(QUEUED_ASSET)
    renderPage()
    await openDetailsPopup()

    expect(screen.getByPlaceholderText(/Chưa có caption/)).toBeDisabled()
    expect(screen.getByRole('button', { name: 'Lưu' })).toBeDisabled()
    expect(screen.getByRole('button', { name: '✍️ Sinh lại' })).toBeDisabled()
    expect(screen.getByText(/Ảnh đang trong hàng chờ sinh caption/)).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Xem job' })).toHaveAttribute('href', '/media/caption-jobs')
    expect(screen.getByRole('button', { name: 'Copy' })).toBeEnabled()
  })

  it('(b) captionQueued=false leaves the popup editable with no notice', async () => {
    useAsset({ ...QUEUED_ASSET, captionQueued: false })
    renderPage()
    await openDetailsPopup()

    expect(screen.getByPlaceholderText(/Chưa có caption/)).toBeEnabled()
    expect(screen.getByRole('button', { name: 'Lưu' })).toBeEnabled()
    expect(screen.queryByText(/Ảnh đang trong hàng chờ sinh caption/)).not.toBeInTheDocument()
  })

  it('(c) 409 MEDIA_CAPTION_QUEUED on Lưu -> error toast, typed text kept, popup locks', async () => {
    updateMutate.mockRejectedValue({
      response: { status: 409, data: { errorCode: 'MEDIA_CAPTION_QUEUED', message: 'Ảnh đang trong hàng chờ sinh caption' } },
    })
    useAsset({ ...QUEUED_ASSET, captionQueued: false })
    renderPage()
    await openDetailsPopup()

    const textarea = screen.getByPlaceholderText(/Chưa có caption/)
    fireEvent.change(textarea, { target: { value: 'đang gõ dở' } })
    fireEvent.click(screen.getByRole('button', { name: 'Lưu' }))

    await waitFor(() => expect(toast.error).toHaveBeenCalledWith('Ảnh đang trong hàng chờ sinh caption'))
    expect(screen.getByPlaceholderText(/Chưa có caption/)).toHaveValue('đang gõ dở')
    await waitFor(() => expect(screen.getByPlaceholderText(/Chưa có caption/)).toBeDisabled())
  })
})
