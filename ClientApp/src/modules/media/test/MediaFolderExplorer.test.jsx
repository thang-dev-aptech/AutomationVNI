import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import MediaFolderExplorer from '../components/MediaFolderExplorer'
import { useMediaFolderExplorer } from '../hooks/useMediaFolderExplorer'
import { mediaFolderApi } from '../services/mediaFolderApi'
import {
  CHANNELS,
  FOLDER_A_CHILD,
  FOLDER_A_GRAND,
  FOLDER_A_ROOT,
  FOLDER_B_ROOT,
  PAGE_A,
  PAGE_B,
  deferred,
  wrapBreadcrumb,
  wrapPaged,
} from './mediaFolderExplorerFixtures'

vi.mock('../services/mediaFolderApi', async (importOriginal) => {
  const actual = await importOriginal()
  return {
    ...actual,
    mediaFolderApi: {
      ...actual.mediaFolderApi,
      tree: vi.fn(),
      children: vi.fn(),
      breadcrumb: vi.fn(),
    },
  }
})

function createQueryClient() {
  return new QueryClient({
    defaultOptions: {
      queries: {
        retry: false,
        refetchOnWindowFocus: false,
        staleTime: 0,
      },
    },
  })
}

function ExplorerHarness({ socialChannelId, onSocialChannelChange = () => {}, ...rest }) {
  const explorer = useMediaFolderExplorer({ socialChannelId })
  return (
    <MediaFolderExplorer
      channels={CHANNELS}
      onSocialChannelChange={onSocialChannelChange}
      canManage
      {...explorer}
      {...rest}
    />
  )
}

function renderExplorer(socialChannelId, options = {}) {
  const queryClient = createQueryClient()
  const user = userEvent.setup()
  const view = render(
    <QueryClientProvider client={queryClient}>
      <ExplorerHarness socialChannelId={socialChannelId} {...options} />
    </QueryClientProvider>,
  )
  return { user, queryClient, ...view }
}

describe('MEDIA-04-AC1 one-level Folder Explorer', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    mediaFolderApi.tree.mockResolvedValue(wrapPaged([
      FOLDER_A_ROOT,
      FOLDER_A_CHILD,
      FOLDER_A_GRAND,
      FOLDER_B_ROOT,
    ]))
    mediaFolderApi.children.mockImplementation(({ socialChannelId, parentFolderId }) => {
      if (socialChannelId === PAGE_A && !parentFolderId) {
        return Promise.resolve(wrapPaged([FOLDER_A_ROOT]))
      }
      if (socialChannelId === PAGE_A && parentFolderId === FOLDER_A_ROOT.id) {
        return Promise.resolve(wrapPaged([FOLDER_A_CHILD]))
      }
      if (socialChannelId === PAGE_A && parentFolderId === FOLDER_A_CHILD.id) {
        return Promise.resolve(wrapPaged([FOLDER_A_GRAND]))
      }
      if (socialChannelId === PAGE_B && !parentFolderId) {
        return Promise.resolve(wrapPaged([FOLDER_B_ROOT]))
      }
      return Promise.resolve(wrapPaged([]))
    })
    mediaFolderApi.breadcrumb.mockImplementation(({ folderId }) => {
      if (folderId === FOLDER_A_ROOT.id) {
        return Promise.resolve(wrapBreadcrumb([{ id: FOLDER_A_ROOT.id, name: FOLDER_A_ROOT.name }]))
      }
      if (folderId === FOLDER_A_CHILD.id) {
        return Promise.resolve(wrapBreadcrumb([
          { id: FOLDER_A_ROOT.id, name: FOLDER_A_ROOT.name },
          { id: FOLDER_A_CHILD.id, name: FOLDER_A_CHILD.name },
        ]))
      }
      return Promise.resolve(wrapBreadcrumb([]))
    })
  })

  it('loads only direct children once, renders breadcrumb/counts/HasChildren, and never calls /tree', async () => {
    renderExplorer(PAGE_A)

    await screen.findByRole('button', { name: /Campaign A/ })

    expect(mediaFolderApi.children).toHaveBeenCalledTimes(1)
    expect(mediaFolderApi.children).toHaveBeenCalledWith(expect.objectContaining({
      socialChannelId: PAGE_A,
      index: 1,
      size: 20,
    }))
    expect(mediaFolderApi.children.mock.calls[0][0].parentFolderId == null).toBe(true)
    expect(mediaFolderApi.tree).not.toHaveBeenCalled()
    expect(mediaFolderApi.breadcrumb).not.toHaveBeenCalled()

    expect(screen.getByRole('navigation', { name: 'Đường dẫn thư mục' })).toHaveTextContent('Thư mục gốc')
    expect(screen.getByTestId(`folder-counts-${FOLDER_A_ROOT.id}`)).toHaveTextContent('1 thư mục con')
    expect(screen.getByTestId(`folder-counts-${FOLDER_A_ROOT.id}`)).toHaveTextContent('2 ảnh')
    expect(screen.getByTestId(`folder-has-children-${FOLDER_A_ROOT.id}`)).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /Child A/ })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /Grand A/ })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /Campaign B/ })).not.toBeInTheDocument()
  })

  it('opens a folder with descendants by fetching that parent once and still skipping /tree', async () => {
    const { user } = renderExplorer(PAGE_A)
    await screen.findByRole('button', { name: /Campaign A/ })

    await user.click(screen.getByRole('button', { name: /Campaign A/ }))
    await screen.findByRole('button', { name: /Child A/ })

    const parentCalls = mediaFolderApi.children.mock.calls.filter((call) => (
      call[0].socialChannelId === PAGE_A && call[0].parentFolderId === FOLDER_A_ROOT.id
    ))
    expect(parentCalls).toHaveLength(1)
    expect(mediaFolderApi.tree).not.toHaveBeenCalled()
    expect(mediaFolderApi.breadcrumb).toHaveBeenCalledTimes(1)
    expect(mediaFolderApi.breadcrumb).toHaveBeenCalledWith({
      socialChannelId: PAGE_A,
      folderId: FOLDER_A_ROOT.id,
    })

    expect(screen.getByRole('navigation', { name: 'Đường dẫn thư mục' })).toHaveTextContent('Campaign A')
    expect(screen.queryByRole('button', { name: /Grand A/ })).not.toBeInTheDocument()
    expect(screen.getByTestId(`folder-has-children-${FOLDER_A_CHILD.id}`)).toBeInTheDocument()
    expect(screen.getByTestId(`folder-counts-${FOLDER_A_CHILD.id}`)).toHaveTextContent('1 thư mục con')
  })

  it('clicking anywhere on the card, not just the name, opens the folder', async () => {
    const { user } = renderExplorer(PAGE_A)
    await screen.findByRole('button', { name: /Campaign A/ })

    // Bấm vào vùng counts (không phải chữ tên) vẫn phải mở được folder — cả card là vùng bấm.
    await user.click(screen.getByTestId(`folder-counts-${FOLDER_A_ROOT.id}`))
    await screen.findByRole('button', { name: /Child A/ })

    expect(screen.getByRole('navigation', { name: 'Đường dẫn thư mục' })).toHaveTextContent('Campaign A')
  })

  it('clicking a management tool (e.g. delete) fires only that action, not also open-folder', async () => {
    const onDelete = vi.fn()
    const { user } = renderExplorer(PAGE_A, { onDelete })
    await screen.findByRole('button', { name: /Campaign A/ })

    await user.click(screen.getByTitle('Xóa'))

    expect(onDelete).toHaveBeenCalledWith(expect.objectContaining({ id: FOLDER_A_ROOT.id }))
    expect(mediaFolderApi.children.mock.calls.some((call) => call[0].parentFolderId === FOLDER_A_ROOT.id)).toBe(false)
    expect(screen.getByRole('navigation', { name: 'Đường dẫn thư mục' })).toHaveTextContent('Thư mục gốc')
  })

  it('paginates one level of the current Page/parent without calling /tree', async () => {
    mediaFolderApi.children.mockImplementation(({ socialChannelId, parentFolderId, index }) => {
      if (socialChannelId === PAGE_A && !parentFolderId) {
        return Promise.resolve(wrapPaged(
          index === 1 ? [FOLDER_A_ROOT] : [{ ...FOLDER_A_CHILD, name: 'Page 2 folder' }],
          { index, size: 20, total: 40 },
        ))
      }
      return Promise.resolve(wrapPaged([]))
    })

    const { user } = renderExplorer(PAGE_A)
    await screen.findByRole('button', { name: /Campaign A/ })
    expect(screen.getByText('Trang 1/2')).toBeInTheDocument()

    await user.click(screen.getByRole('button', { name: 'Sau' }))
    await screen.findByRole('button', { name: /Page 2 folder/ })

    const pageTwoCalls = mediaFolderApi.children.mock.calls.filter((call) => (
      call[0].socialChannelId === PAGE_A && call[0].index === 2 && call[0].parentFolderId == null
    ))
    expect(pageTwoCalls).toHaveLength(1)
    expect(mediaFolderApi.tree).not.toHaveBeenCalled()
    expect(screen.queryByRole('button', { name: /Grand A/ })).not.toBeInTheDocument()
  })
})

describe('MEDIA-04-AC2 reset on Page change', () => {
  it('resets folder/breadcrumb/selection/pagination and ignores a late Page A response', async () => {
    const pendingA = deferred()
    mediaFolderApi.tree.mockResolvedValue(wrapPaged([FOLDER_A_ROOT, FOLDER_B_ROOT]))
    mediaFolderApi.breadcrumb.mockResolvedValue(wrapBreadcrumb([]))
    mediaFolderApi.children.mockImplementation(({ socialChannelId, index }) => {
      if (socialChannelId === PAGE_A) {
        return pendingA.promise
      }
      return Promise.resolve(wrapPaged(
        index === 1 ? [FOLDER_B_ROOT] : [],
        { index, total: 1 },
      ))
    })

    const queryClient = createQueryClient()
    const user = userEvent.setup()
    const { rerender } = render(
      <QueryClientProvider client={queryClient}>
        <ExplorerHarness socialChannelId={PAGE_A} />
      </QueryClientProvider>,
    )

    await user.click(screen.getByRole('button', { name: /Tất cả/ }))
    expect(screen.getByRole('button', { name: /Tất cả/ })).toHaveAttribute('aria-current', 'true')

    rerender(
      <QueryClientProvider client={queryClient}>
        <ExplorerHarness socialChannelId={PAGE_B} />
      </QueryClientProvider>,
    )

    await screen.findByRole('button', { name: /Campaign B/ })
    expect(screen.queryByRole('button', { name: /Campaign A/ })).not.toBeInTheDocument()
    expect(screen.getByRole('navigation', { name: 'Đường dẫn thư mục' })).toHaveTextContent('Thư mục gốc')
    expect(screen.getByRole('button', { name: /Tất cả/ })).toHaveAttribute('aria-current', 'true')
    expect(screen.queryByText(/Trang 2/)).not.toBeInTheDocument()

    pendingA.resolve(wrapPaged([FOLDER_A_ROOT], { index: 2, total: 40, size: 20 }))
    await waitFor(() => {
      expect(screen.getByRole('button', { name: /Campaign B/ })).toBeInTheDocument()
    })
    expect(screen.queryByRole('button', { name: /Campaign A/ })).not.toBeInTheDocument()
    expect(mediaFolderApi.tree).not.toHaveBeenCalled()
  })
})
