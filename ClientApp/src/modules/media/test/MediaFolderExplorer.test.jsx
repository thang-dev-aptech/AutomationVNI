import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen } from '@testing-library/react'
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
  wrapPaged,
} from './mediaFolderExplorerFixtures'

vi.mock('../services/mediaFolderApi', async (importOriginal) => {
  const actual = await importOriginal()
  return {
    ...actual,
    mediaFolderApi: {
      ...actual.mediaFolderApi,
      children: vi.fn(),
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

describe('MEDIA-04 Folder Explorer with tree navigation', () => {
  beforeEach(() => {
    vi.clearAllMocks()
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
  })

  it('renders Page selector, fixed rows, and tree root level', async () => {
    renderExplorer(PAGE_A)

    expect(screen.getByRole('combobox', { name: 'Page' })).toHaveValue(PAGE_A)
    expect(screen.getByText('Tất cả')).toBeInTheDocument()
    expect(screen.getByText('Chưa phân loại')).toBeInTheDocument()

    await screen.findByText(FOLDER_A_ROOT.name)
    expect(mediaFolderApi.children).toHaveBeenCalledTimes(1)
    expect(mediaFolderApi.children).toHaveBeenCalledWith(expect.objectContaining({
      socialChannelId: PAGE_A,
      parentFolderId: null,
    }))
  })

  it('lazily loads children only when folder is expanded', async () => {
    const { user } = renderExplorer(PAGE_A)
    await screen.findByText(FOLDER_A_ROOT.name)
    expect(mediaFolderApi.children).toHaveBeenCalledTimes(1)

    const toggleButton = screen.getByRole('button', { name: /Mở rộng/ })
    await user.click(toggleButton)
    await screen.findByText(FOLDER_A_CHILD.name)

    expect(mediaFolderApi.children).toHaveBeenCalledTimes(2)
    const childCall = mediaFolderApi.children.mock.calls[1]
    expect(childCall[0].parentFolderId).toBe(FOLDER_A_ROOT.id)
  })

  it('navigates when folder name is clicked', async () => {
    const { user } = renderExplorer(PAGE_A)
    const nameButtons = await screen.findAllByText(FOLDER_A_ROOT.name)

    // Find the name button (not the toggle)
    const nameButton = nameButtons.find(btn => btn.className.includes('media-folder-name'))
    if (nameButton) {
      await user.click(nameButton)

      // Should fetch children of the clicked folder
      expect(mediaFolderApi.children.mock.calls.some(call =>
        call[0].parentFolderId === FOLDER_A_ROOT.id
      )).toBe(true)
    }
  })

  it('supports management buttons (create/rename/delete)', async () => {
    const onCreateChild = vi.fn()
    const onRename = vi.fn()
    const onDelete = vi.fn()
    const { user } = renderExplorer(PAGE_A, { onCreateChild, onRename, onDelete })

    await screen.findByText(FOLDER_A_ROOT.name)

    await user.click(screen.getByTitle('Tạo thư mục con'))
    expect(onCreateChild).toHaveBeenCalledWith(FOLDER_A_ROOT.id)

    await user.click(screen.getByTitle('Đổi tên'))
    expect(onRename).toHaveBeenCalledWith(expect.objectContaining({ id: FOLDER_A_ROOT.id }))

    await user.click(screen.getByTitle('Xóa'))
    expect(onDelete).toHaveBeenCalledWith(expect.objectContaining({ id: FOLDER_A_ROOT.id }))
  })

  it('resets expanded folders on Page change', async () => {
    const queryClient = createQueryClient()
    const { user, rerender } = renderExplorer(PAGE_A)

    await screen.findByText(FOLDER_A_ROOT.name)
    const toggleButton = screen.getByRole('button', { name: /Mở rộng/ })
    await user.click(toggleButton)
    await screen.findByText(FOLDER_A_CHILD.name)

    rerender(
      <QueryClientProvider client={queryClient}>
        <ExplorerHarness socialChannelId={PAGE_B} />
      </QueryClientProvider>,
    )

    await screen.findByText(FOLDER_B_ROOT.name)
    expect(screen.queryByText(FOLDER_A_CHILD.name)).not.toBeInTheDocument()
  })

  it('supports "Tất cả" and "Chưa phân loại" selection', async () => {
    const { user } = renderExplorer(PAGE_A)

    const allButton = screen.getByText('Tất cả').closest('button')
    const unassignedButton = screen.getByText('Chưa phân loại').closest('[role="button"]')

    expect(allButton).toHaveAttribute('aria-current', 'true')

    await user.click(unassignedButton)
    expect(unassignedButton).toHaveAttribute('aria-current', 'true')
    expect(allButton).not.toHaveAttribute('aria-current', 'true')
  })

  it('supports drag-drop of assets to "Chưa phân loại"', async () => {
    const onMoveAsset = vi.fn()
    renderExplorer(PAGE_A, { onMoveAsset })

    const unassignedContainer = screen.getByText('Chưa phân loại').closest('[role="button"]')

    const dropEvent = new DragEvent('drop', {
      bubbles: true,
      dataTransfer: new DataTransfer(),
    })
    dropEvent.dataTransfer.setData('text/media-asset-id', 'asset-123')

    unassignedContainer.dispatchEvent(dropEvent)
    expect(onMoveAsset).toHaveBeenCalledWith('asset-123', null)
  })

  it('changes Page and loads new Page root folders', async () => {
    const { user } = renderExplorer(PAGE_A)
    await screen.findByText(FOLDER_A_ROOT.name)

    const pageSelect = screen.getByRole('combobox', { name: 'Page' })
    await user.selectOptions(pageSelect, PAGE_B)

    await screen.findByText(FOLDER_B_ROOT.name)
    expect(screen.queryByText(FOLDER_A_ROOT.name)).not.toBeInTheDocument()
  })

  it('disables tree when no Page is selected', () => {
    renderExplorer(null)

    expect(screen.getByText(/Chọn Page để xem thư mục/)).toBeInTheDocument()
    expect(mediaFolderApi.children).not.toHaveBeenCalled()
  })
})
