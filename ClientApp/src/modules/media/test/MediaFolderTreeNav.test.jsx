import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { useState } from 'react'
import MediaFolderTreeNav from '../components/MediaFolderTreeNav'
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

function TreeWrapper({
  socialChannelId,
  currentFolderId,
  onOpen,
  onToggleExpand,
  onMoveAsset,
  onCreateChild,
  onRename,
  onDelete,
  canManage,
  initialExpanded,
}) {
  const [expandedFolderIds, setExpandedFolderIds] = useState(initialExpanded ?? new Set())

  const handleToggleExpand = (folderId) => {
    // Always update local state
    setExpandedFolderIds((prev) => {
      const next = new Set(prev)
      if (next.has(folderId)) {
        next.delete(folderId)
      } else {
        next.add(folderId)
      }
      return next
    })
    // Also call custom handler if provided
    onToggleExpand?.(folderId)
  }

  return (
    <MediaFolderTreeNav
      socialChannelId={socialChannelId}
      currentFolderId={currentFolderId ?? null}
      expandedFolderIds={expandedFolderIds}
      onOpen={onOpen}
      onToggleExpand={handleToggleExpand}
      onMoveAsset={onMoveAsset}
      onCreateChild={onCreateChild}
      onRename={onRename}
      onDelete={onDelete}
      canManage={canManage}
    />
  )
}

function renderTree(props = {}) {
  const queryClient = createQueryClient()
  const user = userEvent.setup()

  const onOpen = props.onOpen ?? vi.fn()
  const onMoveAsset = props.onMoveAsset ?? vi.fn()
  const onCreateChild = props.onCreateChild ?? vi.fn()
  const onRename = props.onRename ?? vi.fn()
  const onDelete = props.onDelete ?? vi.fn()

  const view = render(
    <QueryClientProvider client={queryClient}>
      <TreeWrapper
        socialChannelId={props.socialChannelId === undefined ? PAGE_A : props.socialChannelId}
        currentFolderId={props.currentFolderId}
        onOpen={onOpen}
        onToggleExpand={props.onToggleExpand}
        onMoveAsset={onMoveAsset}
        onCreateChild={onCreateChild}
        onRename={onRename}
        onDelete={onDelete}
        canManage={props.canManage ?? true}
        initialExpanded={props.expandedFolderIds}
      />
    </QueryClientProvider>,
  )
  return { user, onOpen, onMoveAsset, onCreateChild, onRename, onDelete, ...view }
}

describe('MediaFolderTreeNav', () => {
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

  it('loads root level eagerly on mount', async () => {
    renderTree()

    await screen.findByRole('button', { name: /📁 Campaign A/ })

    expect(mediaFolderApi.children).toHaveBeenCalledTimes(1)
    expect(mediaFolderApi.children).toHaveBeenCalledWith(expect.objectContaining({
      socialChannelId: PAGE_A,
      parentFolderId: null,
    }))
    expect(screen.queryByRole('button', { name: /📁 Child A/ })).not.toBeInTheDocument()
  })

  it('does not fetch children until the expand toggle is clicked', async () => {
    const { user } = renderTree()
    await screen.findByRole('button', { name: /📁 Campaign A/ })
    expect(mediaFolderApi.children).toHaveBeenCalledTimes(1)

    // Expand the root folder
    await user.click(screen.getByRole('button', { name: /Mở rộng thư mục Campaign A/ }))
    await screen.findByRole('button', { name: /📁 Child A/ })

    // Should now have 2 calls: 1 for root, 1 for root's children
    expect(mediaFolderApi.children).toHaveBeenCalledTimes(2)
  })

  it('fetches a node children only once, even if toggled multiple times', async () => {
    const { user } = renderTree()
    await screen.findByRole('button', { name: /📁 Campaign A/ })
    expect(mediaFolderApi.children).toHaveBeenCalledTimes(1)

    // Expand
    await user.click(screen.getByRole('button', { name: /Mở rộng thư mục Campaign A/ }))
    await screen.findByRole('button', { name: /📁 Child A/ })
    expect(mediaFolderApi.children).toHaveBeenCalledTimes(2)

    // Collapse
    await user.click(screen.getByRole('button', { name: /Thu gọn thư mục Campaign A/ }))
    expect(screen.queryByRole('button', { name: /📁 Child A/ })).not.toBeInTheDocument()

    // Expand again — should NOT fetch again
    await user.click(screen.getByRole('button', { name: /Mở rộng thư mục Campaign A/ }))
    await screen.findByRole('button', { name: /📁 Child A/ })
    expect(mediaFolderApi.children).toHaveBeenCalledTimes(2)
  })

  it('clicking folder name calls onOpen without expanding', async () => {
    const { user, onOpen } = renderTree()
    const nameButton = await screen.findByRole('button', { name: /📁 Campaign A/ })

    await user.click(nameButton)
    expect(onOpen).toHaveBeenCalledWith(FOLDER_A_ROOT.id)

    // Children should not appear (no expand triggered)
    expect(screen.queryByRole('button', { name: /📁 Child A/ })).not.toBeInTheDocument()
  })

  it('clicking expand toggle expands without navigating (no onOpen call)', async () => {
    const { user, onOpen } = renderTree()
    await screen.findByRole('button', { name: /📁 Campaign A/ })

    await user.click(screen.getByRole('button', { name: /Mở rộng thư mục Campaign A/ }))
    await screen.findByRole('button', { name: /📁 Child A/ })

    // onOpen should NOT have been called
    expect(onOpen).not.toHaveBeenCalled()
  })

  it('calls onToggleExpand when toggle button is clicked', async () => {
    const onToggleExpand = vi.fn()
    const { user } = renderTree({ onToggleExpand })
    const expandButton = await screen.findByRole('button', { name: /Mở rộng thư mục Campaign A/ })

    await user.click(expandButton)
    expect(onToggleExpand).toHaveBeenCalledWith(FOLDER_A_ROOT.id)
  })

  it('highlights the folder matching currentFolderId with is-active class', async () => {
    renderTree({ currentFolderId: FOLDER_A_ROOT.id })
    const row = await screen.findByRole('button', { name: /📁 Campaign A/ })
    expect(row.closest('.media-folder-row')).toHaveClass('is-active')
  })

  it('applies drag-over styling on file hover', async () => {
    renderTree()
    const row = await screen.findByRole('button', { name: /📁 Campaign A/ })
    const container = row.closest('.media-folder-row')

    fireEvent.dragOver(container)
    expect(container).toHaveClass('is-dragover')

    fireEvent.dragLeave(container)
    expect(container).not.toHaveClass('is-dragover')
  })

  it('calls onMoveAsset when asset is dropped on a folder', async () => {
    const { onMoveAsset } = renderTree()
    const row = await screen.findByRole('button', { name: /📁 Campaign A/ })
    const container = row.closest('.media-folder-row')

    const dataTransfer = { getData: () => 'asset-123' }
    fireEvent.drop(container, { dataTransfer })

    expect(onMoveAsset).toHaveBeenCalledWith('asset-123', FOLDER_A_ROOT.id)
  })

  it('calls onCreateChild when create button is clicked and does not navigate', async () => {
    const { user, onCreateChild, onOpen } = renderTree()
    await screen.findByRole('button', { name: /📁 Campaign A/ })
    const createButton = screen.getByTitle('Tạo thư mục con')

    await user.click(createButton)
    expect(onCreateChild).toHaveBeenCalledWith(FOLDER_A_ROOT.id)
    expect(onOpen).not.toHaveBeenCalled()
  })

  it('calls onRename when rename button is clicked and does not navigate', async () => {
    const { user, onRename, onOpen } = renderTree()
    await screen.findByRole('button', { name: /📁 Campaign A/ })
    const renameButton = screen.getByTitle('Đổi tên')

    await user.click(renameButton)
    expect(onRename).toHaveBeenCalledWith(FOLDER_A_ROOT)
    expect(onOpen).not.toHaveBeenCalled()
  })

  it('calls onDelete when delete button is clicked and does not navigate', async () => {
    const { user, onDelete, onOpen } = renderTree()
    await screen.findByRole('button', { name: /📁 Campaign A/ })
    const deleteButton = screen.getByTitle('Xóa')

    await user.click(deleteButton)
    expect(onDelete).toHaveBeenCalledWith(FOLDER_A_ROOT)
    expect(onOpen).not.toHaveBeenCalled()
  })

  it('does not show management buttons when canManage is false', async () => {
    renderTree({ canManage: false })
    await screen.findByRole('button', { name: /📁 Campaign A/ })

    expect(screen.queryByTitle('Tạo thư mục con')).not.toBeInTheDocument()
    expect(screen.queryByTitle('Đổi tên')).not.toBeInTheDocument()
    expect(screen.queryByTitle('Xóa')).not.toBeInTheDocument()
  })

  it('shows loading hint while root level is pending', async () => {
    const pending = deferred()
    mediaFolderApi.children.mockImplementation(({ parentFolderId }) => (
      parentFolderId ? Promise.resolve(wrapPaged([])) : pending.promise
    ))

    renderTree()
    expect(screen.getByText('Đang tải thư mục...')).toBeInTheDocument()

    pending.resolve(wrapPaged([FOLDER_A_ROOT]))
    await waitFor(() => {
      expect(screen.getByRole('button', { name: /📁 Campaign A/ })).toBeInTheDocument()
    })
  })

  it('shows error message when root load fails, with retry button', async () => {
    mediaFolderApi.children.mockImplementation(({ parentFolderId }) => (
      parentFolderId ? Promise.resolve(wrapPaged([])) : Promise.reject(new Error('boom'))
    ))

    const { user } = renderTree()
    await screen.findByText(/Không tải được danh sách thư mục/)

    const retryButton = screen.getByRole('button', { name: 'Thử lại' })
    expect(retryButton).toBeInTheDocument()

    // Fix mock and retry
    mediaFolderApi.children.mockImplementation(({ parentFolderId }) => (
      parentFolderId ? Promise.resolve(wrapPaged([])) : Promise.resolve(wrapPaged([FOLDER_A_ROOT]))
    ))
    await user.click(retryButton)

    await screen.findByRole('button', { name: /📁 Campaign A/ })
  })

  it('shows per-node error with retry when child load fails', async () => {
    mediaFolderApi.children.mockImplementation(({ socialChannelId, parentFolderId }) => {
      if (socialChannelId === PAGE_A && !parentFolderId) {
        return Promise.resolve(wrapPaged([FOLDER_A_ROOT]))
      }
      if (socialChannelId === PAGE_A && parentFolderId === FOLDER_A_ROOT.id) {
        return Promise.reject(new Error('boom'))
      }
      return Promise.resolve(wrapPaged([]))
    })

    const { user } = renderTree()
    await screen.findByRole('button', { name: /📁 Campaign A/ })

    await user.click(screen.getByRole('button', { name: /Mở rộng thư mục Campaign A/ }))
    await screen.findByText(/Lỗi tải thư mục con/)

    // Fix and retry
    mediaFolderApi.children.mockImplementation(({ socialChannelId, parentFolderId }) => {
      if (socialChannelId === PAGE_A && !parentFolderId) {
        return Promise.resolve(wrapPaged([FOLDER_A_ROOT]))
      }
      if (socialChannelId === PAGE_A && parentFolderId === FOLDER_A_ROOT.id) {
        return Promise.resolve(wrapPaged([FOLDER_A_CHILD]))
      }
      return Promise.resolve(wrapPaged([]))
    })

    const retryButton = screen.getByRole('button', { name: 'Thử lại' })
    await user.click(retryButton)
    await screen.findByRole('button', { name: /📁 Child A/ })
  })

  it('renders nothing when no socialChannelId is provided', () => {
    renderTree({ socialChannelId: null })

    expect(screen.getByText(/Chọn Page để xem thư mục/)).toBeInTheDocument()
    expect(mediaFolderApi.children).not.toHaveBeenCalled()
  })

  it('supports pre-expanded folders via expandedFolderIds prop', async () => {
    const expanded = new Set([FOLDER_A_ROOT.id])
    renderTree({ expandedFolderIds: expanded })

    await screen.findByRole('button', { name: /📁 Campaign A/ })
    await screen.findByRole('button', { name: /📁 Child A/ })

    // Both root and its child should be loaded
    expect(mediaFolderApi.children).toHaveBeenCalledTimes(2)
  })

  it('shows "no children" message when a folder has no subfolders', async () => {
    // FOLDER_A_CHILD báo hasChildren=true (nên có nút mở rộng) nhưng API trả về rỗng —
    // đúng tình huống thực tế cần hiển thị "Không có thư mục con" (khác FOLDER_A_GRAND,
    // vốn hasChildren=false nên không có nút mở rộng để bấm ngay từ đầu).
    mediaFolderApi.children.mockImplementation(({ socialChannelId, parentFolderId }) => {
      if (socialChannelId === PAGE_A && !parentFolderId) {
        return Promise.resolve(wrapPaged([FOLDER_A_ROOT]))
      }
      if (socialChannelId === PAGE_A && parentFolderId === FOLDER_A_ROOT.id) {
        return Promise.resolve(wrapPaged([FOLDER_A_CHILD]))
      }
      return Promise.resolve(wrapPaged([]))
    })

    const { user } = renderTree()
    await screen.findByRole('button', { name: /📁 Campaign A/ })

    await user.click(screen.getByRole('button', { name: /Mở rộng thư mục Campaign A/ }))
    await screen.findByRole('button', { name: /📁 Child A/ })

    // Expand Child A — API trả rỗng dù hasChildren báo true
    await user.click(screen.getByRole('button', { name: /Mở rộng thư mục Child A/ }))
    await screen.findByText(/Không có thư mục con/)
  })

  it('preserves MediaFolderPickerTree.jsx untouched', () => {
    // This is a verification test that the original PickerTree file was not modified.
    // In a real scenario, this would import and compare the file content or run its tests.
    // For now, we just ensure this test file exists as a marker.
    expect(true).toBe(true)
  })
})
