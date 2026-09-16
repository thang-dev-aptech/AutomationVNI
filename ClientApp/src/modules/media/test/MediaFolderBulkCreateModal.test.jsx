import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import MediaFolderBulkCreateModal from '../components/MediaFolderBulkCreateModal'
import { mediaFolderApi } from '../services/mediaFolderApi'
import { PAGE_A, wrapApiData } from './mediaFolderExplorerFixtures'

vi.mock('../services/mediaFolderApi', async (importOriginal) => {
  const actual = await importOriginal()
  return {
    ...actual,
    mediaFolderApi: {
      ...actual.mediaFolderApi,
      bulkCreate: vi.fn(),
    },
  }
})

function bulkResponse({ totalCreated = 1, totalSkipped = 0, folders = [] } = {}) {
  return wrapApiData({
    success: true,
    validateOnly: false,
    totalRequested: folders.length,
    totalCreated,
    totalSkipped,
    folders,
    errors: [],
  })
}

function createQueryClient() {
  return new QueryClient({
    defaultOptions: { queries: { retry: false, refetchOnWindowFocus: false, staleTime: 0 } },
  })
}

function renderModal(props = {}) {
  const queryClient = createQueryClient()
  const invalidateSpy = vi.spyOn(queryClient, 'invalidateQueries')
  const user = userEvent.setup()
  const onClose = props.onClose ?? vi.fn()
  const onSuccess = props.onSuccess ?? vi.fn()
  const view = render(
    <QueryClientProvider client={queryClient}>
      <MediaFolderBulkCreateModal
        open
        socialChannelId={PAGE_A}
        parentFolderId={null}
        onClose={onClose}
        onSuccess={onSuccess}
        {...props}
      />
    </QueryClientProvider>,
  )
  return { user, onClose, onSuccess, invalidateSpy, queryClient, ...view }
}

describe('MEDIA-06 MediaFolderBulkCreateModal', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('disables Preview while a row has an empty name, and disables Submit until a fresh preview exists', async () => {
    const { user } = renderModal()

    expect(screen.getByRole('button', { name: 'Xem trước' })).toBeDisabled()
    expect(screen.getByRole('button', { name: /Tạo \d+ thư mục/ })).toBeDisabled()

    await user.type(screen.getByPlaceholderText('Tên thư mục'), 'Chiến dịch A')
    expect(screen.getByRole('button', { name: 'Xem trước' })).toBeEnabled()

    mediaFolderApi.bulkCreate.mockResolvedValueOnce(bulkResponse({
      folders: [{ clientRef: 'n1', id: 'x', name: 'Chiến dịch A', parentFolderId: null, socialChannelId: PAGE_A, depth: 1, isSkipped: false }],
    }))
    await user.click(screen.getByRole('button', { name: 'Xem trước' }))

    await waitFor(() => expect(screen.getByRole('button', { name: /Tạo 1 thư mục/ })).toBeEnabled())
    expect(mediaFolderApi.bulkCreate).toHaveBeenCalledWith(expect.objectContaining({ validateOnly: true }))
  })

  it('invalidates a stale preview when a row is edited afterwards, disabling Submit again', async () => {
    const { user } = renderModal()
    await user.type(screen.getByPlaceholderText('Tên thư mục'), 'Chiến dịch A')

    mediaFolderApi.bulkCreate.mockResolvedValueOnce(bulkResponse({
      folders: [{ clientRef: 'n1', id: 'x', name: 'Chiến dịch A', parentFolderId: null, socialChannelId: PAGE_A, depth: 1, isSkipped: false }],
    }))
    await user.click(screen.getByRole('button', { name: 'Xem trước' }))
    await waitFor(() => expect(screen.getByRole('button', { name: /Tạo 1 thư mục/ })).toBeEnabled())

    await user.type(screen.getByPlaceholderText('Tên thư mục'), ' đổi')
    expect(screen.getByRole('button', { name: /Tạo \d+ thư mục/ })).toBeDisabled()
  })

  it('shows the per-node preview with depth and skip badges', async () => {
    const { user } = renderModal()
    await user.type(screen.getByPlaceholderText('Tên thư mục'), 'Marketing')

    mediaFolderApi.bulkCreate.mockResolvedValueOnce(bulkResponse({
      totalCreated: 1,
      totalSkipped: 1,
      folders: [
        { clientRef: 'n1', id: 'x1', name: 'Marketing', parentFolderId: null, socialChannelId: PAGE_A, depth: 1, isSkipped: false },
        { clientRef: 'n2', id: 'x2', name: 'Existing', parentFolderId: 'x1', socialChannelId: PAGE_A, depth: 2, isSkipped: true },
      ],
    }))
    await user.click(screen.getByRole('button', { name: 'Xem trước' }))

    expect(await screen.findByText(/Sẽ tạo 1 thư mục mới, dùng lại 1 thư mục có sẵn/)).toBeInTheDocument()
    expect(screen.getByText(/Existing/)).toBeInTheDocument()
    expect(screen.getByText('dùng lại')).toBeInTheDocument()
  })

  it('on submit failure, keeps the modal open, keeps row data, and shows the error without reporting partial success', async () => {
    const { user, onClose, onSuccess } = renderModal()
    await user.type(screen.getByPlaceholderText('Tên thư mục'), 'Trùng tên')

    mediaFolderApi.bulkCreate.mockResolvedValueOnce(bulkResponse({
      folders: [{ clientRef: 'n1', id: 'x', name: 'Trùng tên', parentFolderId: null, socialChannelId: PAGE_A, depth: 1, isSkipped: false }],
    }))
    await user.click(screen.getByRole('button', { name: 'Xem trước' }))
    await waitFor(() => expect(screen.getByRole('button', { name: /Tạo 1 thư mục/ })).toBeEnabled())

    mediaFolderApi.bulkCreate.mockRejectedValueOnce({ response: { data: { message: 'Thư mục đã tồn tại' } } })
    await user.click(screen.getByRole('button', { name: /Tạo 1 thư mục/ }))

    expect(await screen.findByText('Thư mục đã tồn tại')).toBeInTheDocument()
    expect(onClose).not.toHaveBeenCalled()
    expect(onSuccess).not.toHaveBeenCalled()
    expect(screen.getByPlaceholderText('Tên thư mục')).toHaveValue('Trùng tên')
  })

  it('on submit success, closes the modal, calls onSuccess, and invalidates the folder cache for refresh', async () => {
    const { user, onClose, onSuccess, invalidateSpy } = renderModal()
    await user.type(screen.getByPlaceholderText('Tên thư mục'), 'Chiến dịch B')

    mediaFolderApi.bulkCreate.mockResolvedValueOnce(bulkResponse({
      folders: [{ clientRef: 'n1', id: 'x', name: 'Chiến dịch B', parentFolderId: null, socialChannelId: PAGE_A, depth: 1, isSkipped: false }],
    }))
    await user.click(screen.getByRole('button', { name: 'Xem trước' }))
    await waitFor(() => expect(screen.getByRole('button', { name: /Tạo 1 thư mục/ })).toBeEnabled())

    mediaFolderApi.bulkCreate.mockResolvedValueOnce(bulkResponse({
      folders: [{ clientRef: 'n1', id: 'x', name: 'Chiến dịch B', parentFolderId: null, socialChannelId: PAGE_A, depth: 1, isSkipped: false }],
    }))
    await user.click(screen.getByRole('button', { name: /Tạo 1 thư mục/ }))

    await waitFor(() => expect(onSuccess).toHaveBeenCalledTimes(1))
    expect(onClose).toHaveBeenCalledTimes(1)
    expect(mediaFolderApi.bulkCreate).toHaveBeenLastCalledWith(expect.objectContaining({ validateOnly: false }))
    expect(invalidateSpy).toHaveBeenCalledWith(expect.objectContaining({ queryKey: ['media-folders'] }))
  })

  it('builds a multi-row hierarchy, excludes a row from its own parent options, and resets a child when its parent row is removed', async () => {
    const { user } = renderModal()
    await user.type(screen.getByPlaceholderText('Tên thư mục'), 'Root')
    await user.click(screen.getByRole('button', { name: '+ Thêm dòng' }))

    const nameInputs = screen.getAllByPlaceholderText('Tên thư mục')
    await user.type(nameInputs[1], 'Child')

    const parentSelects = screen.getAllByRole('combobox', { name: 'Thư mục cha trong batch' })
    expect(parentSelects[0].querySelectorAll('option')).toHaveLength(2) // root pseudo-option + the other row only
    await user.selectOptions(parentSelects[1], 'n1')

    mediaFolderApi.bulkCreate.mockResolvedValueOnce(bulkResponse({
      folders: [
        { clientRef: 'n1', id: 'x1', name: 'Root', parentFolderId: null, socialChannelId: PAGE_A, depth: 1, isSkipped: false },
        { clientRef: 'n2', id: 'x2', name: 'Child', parentFolderId: 'x1', socialChannelId: PAGE_A, depth: 2, isSkipped: false },
      ],
    }))
    await user.click(screen.getByRole('button', { name: 'Xem trước' }))
    await waitFor(() => expect(mediaFolderApi.bulkCreate).toHaveBeenCalledWith(expect.objectContaining({
      folders: [
        expect.objectContaining({ clientRef: 'n1', name: 'Root', parentRef: null }),
        expect.objectContaining({ clientRef: 'n2', name: 'Child', parentRef: 'n1' }),
      ],
    })))

    const removeButtons = screen.getAllByTitle('Xóa dòng')
    await user.click(removeButtons[0])

    expect(screen.getByRole('combobox', { name: 'Thư mục cha trong batch' })).toHaveValue('')
  })

  it('resets all fields (rows, policy, preview) every time the modal is reopened', async () => {
    const queryClient = createQueryClient()
    const user = userEvent.setup()
    const { rerender } = render(
      <QueryClientProvider client={queryClient}>
        <MediaFolderBulkCreateModal open socialChannelId={PAGE_A} parentFolderId={null} onClose={vi.fn()} />
      </QueryClientProvider>,
    )

    await user.type(screen.getByPlaceholderText('Tên thư mục'), 'Sẽ bị xóa')
    mediaFolderApi.bulkCreate.mockResolvedValueOnce(bulkResponse({
      folders: [{ clientRef: 'n1', id: 'x', name: 'Sẽ bị xóa', parentFolderId: null, socialChannelId: PAGE_A, depth: 1, isSkipped: false }],
    }))
    await user.click(screen.getByRole('button', { name: 'Xem trước' }))
    await waitFor(() => expect(screen.getByRole('button', { name: /Tạo 1 thư mục/ })).toBeEnabled())

    rerender(
      <QueryClientProvider client={queryClient}>
        <MediaFolderBulkCreateModal open={false} socialChannelId={PAGE_A} parentFolderId={null} onClose={vi.fn()} />
      </QueryClientProvider>,
    )
    rerender(
      <QueryClientProvider client={queryClient}>
        <MediaFolderBulkCreateModal open socialChannelId={PAGE_A} parentFolderId={null} onClose={vi.fn()} />
      </QueryClientProvider>,
    )

    expect(screen.getByPlaceholderText('Tên thư mục')).toHaveValue('')
    expect(screen.getByRole('button', { name: /Tạo \d+ thư mục/ })).toBeDisabled()
    expect(screen.queryByText(/Sẽ tạo/)).not.toBeInTheDocument()
  })
})
