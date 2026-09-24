import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import MediaFolderPickerTree from '../components/MediaFolderPickerTree'
import { mediaFolderApi } from '../services/mediaFolderApi'
import {
  FOLDER_A_CHILD,
  FOLDER_A_GRAND,
  FOLDER_A_ROOT,
  FOLDER_B_ROOT,
  PAGE_A,
  PAGE_B,
  deferred,
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
    },
  }
})

function createQueryClient() {
  return new QueryClient({
    defaultOptions: {
      queries: { retry: false, refetchOnWindowFocus: false, staleTime: 0 },
    },
  })
}

function renderPicker(props = {}) {
  const queryClient = createQueryClient()
  const user = userEvent.setup()
  const onChange = props.onChange ?? vi.fn()
  const view = render(
    <QueryClientProvider client={queryClient}>
      <MediaFolderPickerTree socialChannelId={PAGE_A} onChange={onChange} {...props} />
    </QueryClientProvider>,
  )
  return { user, onChange, ...view }
}

describe('MEDIA-07 MediaFolderPickerTree', () => {
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

  it('loads only the root level on mount and never calls /tree', async () => {
    renderPicker()

    await screen.findByRole('button', { name: /Campaign A/ })

    expect(mediaFolderApi.children).toHaveBeenCalledTimes(1)
    expect(mediaFolderApi.children).toHaveBeenCalledWith(expect.objectContaining({
      socialChannelId: PAGE_A,
      parentFolderId: null,
    }))
    expect(mediaFolderApi.tree).not.toHaveBeenCalled()
    expect(screen.queryByRole('button', { name: /Child A/ })).not.toBeInTheDocument()
  })

  it('does not fetch a node children until it is expanded, then fetches once', async () => {
    const { user } = renderPicker()
    await screen.findByRole('button', { name: /Campaign A/ })
    expect(mediaFolderApi.children).toHaveBeenCalledTimes(1)

    await user.click(screen.getByRole('button', { name: 'Mở rộng' }))
    await screen.findByRole('button', { name: /Child A/ })

    const childCalls = mediaFolderApi.children.mock.calls.filter(
      (call) => call[0].parentFolderId === FOLDER_A_ROOT.id,
    )
    expect(childCalls).toHaveLength(1)
    expect(mediaFolderApi.tree).not.toHaveBeenCalled()
  })

  it('selects the root pseudo-node with null and a folder node with its id', async () => {
    const { user, onChange } = renderPicker()
    await screen.findByRole('button', { name: /Campaign A/ })

    await user.click(screen.getByRole('button', { name: /Thư mục gốc/ }))
    expect(onChange).toHaveBeenLastCalledWith(null)

    await user.click(screen.getByRole('button', { name: /Campaign A/ }))
    expect(onChange).toHaveBeenLastCalledWith(FOLDER_A_ROOT.id)
  })

  it('highlights the folder matching `value`', async () => {
    renderPicker({ value: FOLDER_A_ROOT.id })
    const row = await screen.findByRole('button', { name: /Campaign A/ })
    expect(row.closest('.media-folder-row')).toHaveClass('is-active')
  })

  it('disables the excluded folder (editing target) for both select and expand, hiding its subtree', async () => {
    const { user, onChange } = renderPicker({ excludeFolderId: FOLDER_A_ROOT.id })
    const nameButton = await screen.findByRole('button', { name: /Campaign A/ })
    const toggleButton = screen.getByRole('button', { name: 'Mở rộng' })

    expect(nameButton).toBeDisabled()
    expect(toggleButton).toBeDisabled()

    await user.click(nameButton)
    expect(onChange).not.toHaveBeenCalled()

    await user.click(toggleButton)
    expect(screen.queryByRole('button', { name: /Child A/ })).not.toBeInTheDocument()
    expect(mediaFolderApi.children.mock.calls.some((call) => call[0].parentFolderId === FOLDER_A_ROOT.id)).toBe(false)
  })

  it('shows a per-node error with retry, and only that node refetches on retry', async () => {
    mediaFolderApi.children.mockImplementation(({ socialChannelId, parentFolderId }) => {
      if (socialChannelId === PAGE_A && !parentFolderId) {
        return Promise.resolve(wrapPaged([FOLDER_A_ROOT]))
      }
      if (socialChannelId === PAGE_A && parentFolderId === FOLDER_A_ROOT.id) {
        return Promise.reject(new Error('boom'))
      }
      return Promise.resolve(wrapPaged([]))
    })

    const { user } = renderPicker()
    await screen.findByRole('button', { name: /Campaign A/ })
    await user.click(screen.getByRole('button', { name: 'Mở rộng' }))

    await screen.findByText(/Lỗi tải thư mục con/)

    mediaFolderApi.children.mockImplementation(({ socialChannelId, parentFolderId }) => {
      if (socialChannelId === PAGE_A && !parentFolderId) {
        return Promise.resolve(wrapPaged([FOLDER_A_ROOT]))
      }
      if (socialChannelId === PAGE_A && parentFolderId === FOLDER_A_ROOT.id) {
        return Promise.resolve(wrapPaged([FOLDER_A_CHILD]))
      }
      return Promise.resolve(wrapPaged([]))
    })
    await user.click(screen.getByRole('button', { name: 'Thử lại' }))

    await screen.findByRole('button', { name: /Child A/ })
  })

  it('shows a loading hint while the root level is pending', async () => {
    const pending = deferred()
    mediaFolderApi.children.mockImplementation(({ parentFolderId }) => (
      parentFolderId ? Promise.resolve(wrapPaged([])) : pending.promise
    ))

    renderPicker()
    expect(screen.getByText('Đang tải thư mục...')).toBeInTheDocument()

    pending.resolve(wrapPaged([FOLDER_A_ROOT]))
    await waitFor(() => {
      expect(screen.getByRole('button', { name: /Campaign A/ })).toBeInTheDocument()
    })
  })

  it('renders a hint instead of fetching when no socialChannelId is given', () => {
    renderPicker({ socialChannelId: null })

    expect(screen.getByText(/Chọn Page trước/)).toBeInTheDocument()
    expect(mediaFolderApi.children).not.toHaveBeenCalled()
  })
})
