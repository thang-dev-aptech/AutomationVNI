import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, useLocation } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import MediaFolderPickerModal from '../components/MediaFolderPickerModal'
import { mediaAssetApi } from '../services/mediaAssetApi'
import { mediaFolderApi } from '../services/mediaFolderApi'

vi.mock('../services/mediaFolderApi', async (importOriginal) => {
  const actual = await importOriginal()
  return {
    ...actual,
    mediaFolderApi: {
      ...actual.mediaFolderApi,
      pageRoots: vi.fn(),
      children: vi.fn(),
      breadcrumb: vi.fn(),
      tree: vi.fn(),
    },
  }
})

vi.mock('../services/mediaAssetApi', async (importOriginal) => {
  const actual = await importOriginal()
  return {
    ...actual,
    mediaAssetApi: {
      ...actual.mediaAssetApi,
      filter: vi.fn(),
    },
  }
})

const PAGE = '11111111-1111-4111-8111-111111111111'
const ROOT = 'a0000000-0000-4000-8000-000000000001'
const DRIVE = 'b0000000-0000-4000-8000-000000000002'
const CHILD = 'c0000000-0000-4000-8000-000000000003'
const ROOT_FOLDER = { id: ROOT, name: 'Page Campaign', pageName: 'Campaign', socialChannelId: PAGE }
const DRIVE_FOLDER = { id: DRIVE, name: 'Google Drive', pageName: null, socialChannelId: null }
const CHILD_FOLDER = { id: CHILD, name: 'Album', socialChannelId: PAGE, parentFolderId: ROOT }

const image = (id, name) => ({
  id, mimeType: 'image/jpeg', originalFileName: name, fileName: name,
})
const IMG_A = image('img-a', 'a.jpg')
const IMG_B = image('img-b', 'b.jpg')
const PDF = { id: 'doc-1', mimeType: 'application/pdf', originalFileName: 'note.pdf', fileName: 'note.pdf' }

const paged = (items) => ({ data: { items, index: 1, size: 20, total: items.length } })

let currentLocation
function LocationProbe() {
  currentLocation = useLocation()
  return null
}

function renderPicker(props = {}) {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false, refetchOnWindowFocus: false } },
  })
  const onConfirm = vi.fn()
  const onClose = vi.fn()
  const user = userEvent.setup()
  const view = render(
    <QueryClientProvider client={queryClient}>
      <MemoryRouter initialEntries={['/posts/create?keep=1']}>
        <LocationProbe />
        <MediaFolderPickerModal open onClose={onClose} onConfirm={onConfirm} {...props} />
      </MemoryRouter>
    </QueryClientProvider>,
  )
  return { ...view, user, onConfirm, onClose }
}

describe('MediaFolderPickerModal', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    mediaFolderApi.pageRoots.mockResolvedValue(paged([ROOT_FOLDER, DRIVE_FOLDER]))
    mediaFolderApi.children.mockResolvedValue(paged([CHILD_FOLDER]))
    mediaFolderApi.breadcrumb.mockResolvedValue({ data: { ancestors: [ROOT_FOLDER] } })
    mediaAssetApi.filter.mockResolvedValue(paged([IMG_A, IMG_B, PDF]))
  })

  it('lists page roots and the Google Drive tree without changing the page URL', async () => {
    const { user } = renderPicker()

    expect(await screen.findByRole('button', { name: /Page Campaign/ })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /Google Drive/ })).toBeInTheDocument()
    expect(mediaFolderApi.tree).not.toHaveBeenCalled()
    expect(mediaFolderApi.pageRoots).toHaveBeenCalled()

    await user.click(screen.getByRole('button', { name: /Page Campaign/ }))

    expect(await screen.findByRole('button', { name: /Album/ })).toBeInTheDocument()
    expect(mediaFolderApi.children).toHaveBeenCalledWith(expect.objectContaining({
      socialChannelId: PAGE,
      parentFolderId: ROOT,
    }))
    expect(mediaFolderApi.breadcrumb).toHaveBeenCalledWith(expect.objectContaining({
      socialChannelId: PAGE,
      folderId: ROOT,
    }))

    await user.click(screen.getByRole('button', { name: 'Thư mục gốc' }))
    await waitFor(() => expect(mediaFolderApi.pageRoots).toHaveBeenCalled())
    expect(currentLocation.pathname).toBe('/posts/create')
    expect(currentLocation.search).toBe('?keep=1')
    expect(window.location.search).not.toContain('folder=')
  })

  it('only images are selectable, and limit 1 replaces the previous image', async () => {
    mediaFolderApi.children.mockResolvedValue(paged([]))
    const { user, onConfirm, onClose } = renderPicker()
    await user.click(await screen.findByRole('button', { name: /Page Campaign/ }))

    const pdf = await screen.findByRole('button', { name: /note.pdf/ })
    expect(pdf).toBeDisabled()
    await user.click(screen.getByRole('button', { name: /a.jpg/ }))
    await user.click(screen.getByRole('button', { name: /b.jpg/ }))

    expect(screen.getByRole('button', { name: /a.jpg/ })).toHaveAttribute('aria-pressed', 'false')
    expect(screen.getByRole('button', { name: /b.jpg/ })).toHaveAttribute('aria-pressed', 'true')
    expect(screen.getByText('Đã chọn 1 ảnh')).toBeInTheDocument()

    await user.click(screen.getByRole('button', { name: 'Thư mục gốc' }))
    await user.click(await screen.findByRole('button', { name: /Page Campaign/ }))
    expect(await screen.findByRole('button', { name: /b.jpg/ })).toHaveAttribute('aria-pressed', 'true')

    await user.click(screen.getByRole('button', { name: 'Huỷ' }))
    expect(onClose).toHaveBeenCalled()
    expect(onConfirm).not.toHaveBeenCalled()

    await user.click(screen.getByRole('button', { name: 'Dùng ảnh đã chọn' }))
    expect(onConfirm).toHaveBeenCalledWith([expect.objectContaining({ id: 'img-b' })])
  })

  it('renders with the Media screen components and shows the selected state', async () => {
    mediaFolderApi.children.mockResolvedValue(paged([CHILD_FOLDER]))
    const { user, container } = renderPicker()

    await screen.findByRole('button', { name: /Page Campaign/ })
    expect(document.querySelectorAll('.media-folder-card').length).toBe(2)
    expect(document.querySelector('.media-browser-grid')).not.toBeNull()
    expect(document.querySelector('.media-folder-search input[type="search"]')).not.toBeNull()
    expect(document.querySelector('nav.media-folder-breadcrumb')).not.toBeNull()
    expect(container).toBeTruthy()

    await user.click(screen.getByRole('button', { name: /Page Campaign/ }))
    await screen.findByRole('button', { name: /a.jpg/ })
    expect(document.querySelectorAll('.media-asset-card').length).toBe(3)

    const card = (name) => screen.getByRole('button', { name }).closest('.media-asset-card')
    expect(card(/a.jpg/)).not.toHaveClass('is-selected')
    await user.click(screen.getByRole('button', { name: /a.jpg/ }))
    expect(card(/a.jpg/)).toHaveClass('is-selected')
    expect(card(/a.jpg/).querySelector('.media-asset-card-check')).not.toBeNull()
    expect(card(/note.pdf/)).toHaveClass('is-disabled')

    await user.click(screen.getByRole('button', { name: /b.jpg/ }))
    expect(card(/a.jpg/)).not.toHaveClass('is-selected')
    expect(card(/b.jpg/)).toHaveClass('is-selected')
  })

  it('shows no management actions or drag-and-drop in the picker', async () => {
    const { user } = renderPicker()
    await user.click(await screen.findByRole('button', { name: /Page Campaign/ }))
    await screen.findByRole('button', { name: /a.jpg/ })

    expect(screen.queryByRole('button', { name: 'Xóa' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Chi tiết' })).not.toBeInTheDocument()
    document.querySelectorAll('.media-asset-card').forEach((cardEl) => {
      expect(cardEl.getAttribute('draggable')).not.toBe('true')
    })
  })

  it('opens a folder from the keyboard', async () => {
    const { user } = renderPicker()
    const folder = await screen.findByRole('button', { name: /Page Campaign/ })
    folder.focus()
    await user.keyboard('{Enter}')

    expect(await screen.findByRole('button', { name: /Album/ })).toBeInTheDocument()
    expect(mediaFolderApi.children).toHaveBeenCalledWith(expect.objectContaining({ parentFolderId: ROOT }))
  })

  it('filters images by the search keyword', async () => {
    mediaFolderApi.children.mockResolvedValue(paged([]))
    const { user } = renderPicker()
    await user.click(await screen.findByRole('button', { name: /Page Campaign/ }))
    await user.type(screen.getByLabelText('Tìm ảnh'), 'beach')

    await waitFor(() => {
      expect(mediaAssetApi.filter).toHaveBeenCalledWith(expect.objectContaining({
        keyword: 'beach',
        folderId: undefined,
      }))
    })
  })
})
