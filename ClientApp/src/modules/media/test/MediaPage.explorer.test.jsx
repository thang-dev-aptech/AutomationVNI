import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react'
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

const FOLDER_A_ROOT_WITH_PAGE = { ...FOLDER_A_ROOT, pageName: 'Page A' }
const FOLDER_B_ROOT_WITH_PAGE = { ...FOLDER_B_ROOT, pageName: 'Page B' }
const FILE_ASSET = { id: 'file-1', fileName: 'shot.jpg', mimeType: 'image/jpeg' }

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
    useMediaAssets.mockImplementation(() => ({
      data: { items: [], total: 0, size: 48 },
      isLoading: false,
      isError: false,
      error: null,
      refetch: vi.fn(),
    }))
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
    const folderHeading = screen.getByRole('heading', { name: 'Thư mục' })
    const breadcrumb = screen.getByRole('navigation', { name: 'Đường dẫn thư mục' })
    expect(folderHeading.parentElement).toContainElement(breadcrumb)
  })

  it('renders the folder pager next to the Thư mục heading, not below the grid', async () => {
    mediaFolderApi.pageRoots.mockResolvedValue(
      wrapPaged([FOLDER_A_ROOT_WITH_PAGE, FOLDER_B_ROOT_WITH_PAGE], { total: 40, size: 20 }),
    )
    const queryClient = new QueryClient({
      defaultOptions: { queries: { retry: false, refetchOnWindowFocus: false } },
    })
    render(
      <QueryClientProvider client={queryClient}>
        <MediaPage />
      </QueryClientProvider>,
    )

    await screen.findByText('Campaign A')
    const folderHeading = screen.getByRole('heading', { name: 'Thư mục' })
    const pagerLabel = screen.getByText('Trang 1 / 2')
    const pager = pagerLabel.closest('.media-page-pager')
    expect(pager).not.toBeNull()
    expect(folderHeading.parentElement).toContainElement(pager)
    expect(document.querySelector('.media-main-content > .media-page-pager')).toBeNull()

    fireEvent.click(screen.getByRole('button', { name: 'Sau' }))
    await waitFor(() => {
      expect(mediaFolderApi.pageRoots).toHaveBeenCalledWith({ index: 2, size: 20 })
    })
  })

  it('renders an independent file pager next to the Tệp heading', async () => {
    mediaFolderApi.pageRoots.mockResolvedValue(
      wrapPaged([FOLDER_A_ROOT_WITH_PAGE, FOLDER_B_ROOT_WITH_PAGE], { total: 40, size: 20 }),
    )
    useMediaAssets.mockImplementation((params) => ({
      data: {
        items: [{ ...FILE_ASSET, fileName: `shot-p${params?.index ?? 1}.jpg` }],
        index: params?.index ?? 1,
        size: 48,
        total: 96,
      },
      isLoading: false,
      isError: false,
      error: null,
      refetch: vi.fn(),
    }))

    const queryClient = new QueryClient({
      defaultOptions: { queries: { retry: false, refetchOnWindowFocus: false } },
    })
    render(
      <QueryClientProvider client={queryClient}>
        <MediaPage />
      </QueryClientProvider>,
    )

    await screen.findByText('Campaign A')
    const fileHeading = screen.getByRole('heading', { name: 'Tệp' })
    const filePager = fileHeading.parentElement.querySelector('.media-page-pager')
    expect(filePager).not.toBeNull()
    expect(fileHeading.parentElement).toContainElement(filePager)
    expect(within(filePager).getByText('Trang 1 / 2')).toBeInTheDocument()

    const folderHeading = screen.getByRole('heading', { name: 'Thư mục' })
    const folderPager = folderHeading.parentElement.querySelector('.media-page-pager')
    expect(folderPager).not.toBe(filePager)
    expect(within(folderPager).getByText('Trang 1 / 2')).toBeInTheDocument()

    fireEvent.click(within(filePager).getByRole('button', { name: 'Sau' }))
    await waitFor(() => {
      const last = useMediaAssets.mock.calls.at(-1)[0]
      expect(last.index).toBe(2)
      expect(last.size).toBe(48)
    })
    expect(mediaFolderApi.pageRoots).not.toHaveBeenCalledWith({ index: 2, size: 20 })
    expect(within(folderPager).getByText('Trang 1 / 2')).toBeInTheDocument()
  })

  it('resets file pagination to page 1 when keyword, source, or unassigned selection change', async () => {
    useMediaAssets.mockImplementation((params) => ({
      data: {
        items: [FILE_ASSET],
        index: params?.index ?? 1,
        size: 48,
        total: 96,
      },
      isLoading: false,
      isError: false,
      error: null,
      refetch: vi.fn(),
    }))

    const queryClient = new QueryClient({
      defaultOptions: { queries: { retry: false, refetchOnWindowFocus: false } },
    })
    render(
      <QueryClientProvider client={queryClient}>
        <MediaPage />
      </QueryClientProvider>,
    )

    await screen.findByText('Campaign A')
    const fileHeading = screen.getByRole('heading', { name: 'Tệp' })
    fireEvent.click(within(fileHeading.parentElement).getByRole('button', { name: 'Sau' }))
    await waitFor(() => {
      expect(useMediaAssets.mock.calls.at(-1)[0].index).toBe(2)
    })

    fireEvent.change(screen.getByLabelText('Tìm kiếm'), { target: { value: 'banner' } })
    await waitFor(() => {
      const last = useMediaAssets.mock.calls.at(-1)[0]
      expect(last.index).toBe(1)
      expect(last.keyword).toBe('banner')
    })

    fireEvent.click(within(fileHeading.parentElement).getByRole('button', { name: 'Sau' }))
    await waitFor(() => {
      expect(useMediaAssets.mock.calls.at(-1)[0].index).toBe(2)
    })
    fireEvent.change(screen.getByLabelText('Nguồn'), { target: { value: '1' } })
    await waitFor(() => {
      const last = useMediaAssets.mock.calls.at(-1)[0]
      expect(last.index).toBe(1)
      expect(last.source).toBe(1)
    })

    fireEvent.click(within(fileHeading.parentElement).getByRole('button', { name: 'Sau' }))
    await waitFor(() => {
      expect(useMediaAssets.mock.calls.at(-1)[0].index).toBe(2)
    })
    fireEvent.click(screen.getByRole('button', { name: 'Chưa phân loại' }))
    await waitFor(() => {
      const last = useMediaAssets.mock.calls.at(-1)[0]
      expect(last.index).toBe(1)
      expect(last.unassigned).toBe(true)
    })
  })

  it('right-click on grid whitespace shows create-folder and add-media actions', async () => {
    const queryClient = new QueryClient({
      defaultOptions: { queries: { retry: false, refetchOnWindowFocus: false } },
    })
    render(
      <QueryClientProvider client={queryClient}>
        <MediaPage />
      </QueryClientProvider>,
    )

    await screen.findByText('Campaign A')
    fireEvent.contextMenu(document.querySelector('.media-browser-grid'))

    expect(screen.getByRole('menuitem', { name: 'Tạo thư mục' })).toBeInTheDocument()
    expect(screen.getByRole('menuitem', { name: 'Thêm media' })).toBeInTheDocument()
  })
})
