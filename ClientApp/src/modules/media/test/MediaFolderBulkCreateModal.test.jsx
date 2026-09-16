import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import MediaFolderBulkCreateModal from '../components/MediaFolderBulkCreateModal'
import { mediaFolderApi } from '../services/mediaFolderApi'
import { CHANNELS, PAGE_A, PAGE_B, wrapApiData } from './mediaFolderExplorerFixtures'

vi.mock('../services/mediaFolderApi', async (importOriginal) => {
  const actual = await importOriginal()
  return {
    ...actual,
    mediaFolderApi: {
      ...actual.mediaFolderApi,
      createAcrossPages: vi.fn(),
    },
  }
})

vi.mock('@/modules/social-channels/hooks/useSocialChannels', () => ({
  useSocialChannelAll: () => ({ data: CHANNELS }),
}))

function acrossPagesResponse({ succeeded = [], failed = [] } = {}) {
  const results = [
    ...succeeded.map((id) => ({ socialChannelId: id, success: true, folderId: `folder-${id}`, errorMessage: null })),
    ...failed.map(({ id, message }) => ({ socialChannelId: id, success: false, folderId: null, errorMessage: message })),
  ]
  return wrapApiData({
    totalRequested: results.length,
    totalSucceeded: succeeded.length,
    totalFailed: failed.length,
    results,
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
      <MediaFolderBulkCreateModal open onClose={onClose} onSuccess={onSuccess} {...props} />
    </QueryClientProvider>,
  )
  return { user, onClose, onSuccess, invalidateSpy, ...view }
}

describe('MEDIA-06 MediaFolderBulkCreateModal (create-across-pages)', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('disables submit until a name is entered and at least one Page is selected', async () => {
    const { user } = renderModal()

    expect(screen.getByRole('button', { name: /Tạo trong/ })).toBeDisabled()

    await user.type(screen.getByLabelText('Tên thư mục'), 'Campaign X')
    expect(screen.getByRole('button', { name: /Tạo trong 0 Page/ })).toBeDisabled()

    await user.click(screen.getByLabelText(CHANNELS[0].pageName))
    expect(screen.getByRole('button', { name: /Tạo trong 1 Page/ })).toBeEnabled()
  })

  it('"Chọn tất cả" toggles every Page, and toggles back to "Bỏ chọn tất cả"', async () => {
    const { user } = renderModal()

    await user.click(screen.getByRole('button', { name: 'Chọn tất cả' }))
    expect(screen.getByLabelText(CHANNELS[0].pageName)).toBeChecked()
    expect(screen.getByLabelText(CHANNELS[1].pageName)).toBeChecked()
    expect(screen.getByText(`Chọn Page (${CHANNELS.length}/${CHANNELS.length})`)).toBeInTheDocument()

    await user.click(screen.getByRole('button', { name: 'Bỏ chọn tất cả' }))
    expect(screen.getByLabelText(CHANNELS[0].pageName)).not.toBeChecked()
  })

  it('submits name + selected Page ids to createAcrossPages', async () => {
    mediaFolderApi.createAcrossPages.mockResolvedValue(acrossPagesResponse({ succeeded: [PAGE_A] }))
    const { user } = renderModal()

    await user.type(screen.getByLabelText('Tên thư mục'), 'Campaign X')
    await user.click(screen.getByLabelText(CHANNELS[0].pageName))
    await user.click(screen.getByRole('button', { name: /Tạo trong 1 Page/ }))

    await waitFor(() => expect(mediaFolderApi.createAcrossPages).toHaveBeenCalledWith({
      name: 'Campaign X',
      description: null,
      socialChannelIds: [PAGE_A],
    }))
  })

  it('shows a per-Page success/failure report after submit, without auto-closing', async () => {
    mediaFolderApi.createAcrossPages.mockResolvedValue(acrossPagesResponse({
      succeeded: [PAGE_A],
      failed: [{ id: PAGE_B, message: 'Page/Kênh không tồn tại.' }],
    }))
    const { user, onClose, onSuccess } = renderModal()

    await user.type(screen.getByLabelText('Tên thư mục'), 'Campaign X')
    await user.click(screen.getByRole('button', { name: 'Chọn tất cả' }))
    await user.click(screen.getByRole('button', { name: /Tạo trong 2 Page/ }))

    expect(await screen.findByText(/Đã tạo 1\/2 thư mục, 1 Page lỗi/)).toBeInTheDocument()
    expect(screen.getByText(new RegExp(`✅ ${CHANNELS[0].pageName}`))).toBeInTheDocument()
    expect(screen.getByText(new RegExp(`❌ ${CHANNELS[1].pageName} — Page/Kênh không tồn tại\\.`))).toBeInTheDocument()
    expect(onClose).not.toHaveBeenCalled()
    expect(onSuccess).toHaveBeenCalledTimes(1)

    await user.click(screen.getByRole('button', { name: 'Đóng' }))
    expect(onClose).toHaveBeenCalledTimes(1)
  })

  it('invalidates the folder cache after a run so other Pages still refresh', async () => {
    mediaFolderApi.createAcrossPages.mockResolvedValue(acrossPagesResponse({ succeeded: [PAGE_A] }))
    const { user, invalidateSpy } = renderModal()

    await user.type(screen.getByLabelText('Tên thư mục'), 'Campaign X')
    await user.click(screen.getByLabelText(CHANNELS[0].pageName))
    await user.click(screen.getByRole('button', { name: /Tạo trong 1 Page/ }))

    await waitFor(() => expect(invalidateSpy).toHaveBeenCalledWith(expect.objectContaining({ queryKey: ['media-folders'] })))
  })

  it('on total failure (e.g. validation error), shows the error and keeps the form for retry', async () => {
    mediaFolderApi.createAcrossPages.mockRejectedValue({ response: { data: { message: 'Tên thư mục không được để trống.' } } })
    const { user, onSuccess } = renderModal()

    await user.type(screen.getByLabelText('Tên thư mục'), 'Campaign X')
    await user.click(screen.getByLabelText(CHANNELS[0].pageName))
    await user.click(screen.getByRole('button', { name: /Tạo trong 1 Page/ }))

    expect(await screen.findByText('Tên thư mục không được để trống.')).toBeInTheDocument()
    expect(onSuccess).not.toHaveBeenCalled()
    expect(screen.getByLabelText('Tên thư mục')).toHaveValue('Campaign X')
  })

  it('resets all fields every time the modal is reopened', async () => {
    mediaFolderApi.createAcrossPages.mockResolvedValue(acrossPagesResponse({ succeeded: [PAGE_A] }))
    const queryClient = createQueryClient()
    const user = userEvent.setup()
    const { rerender } = render(
      <QueryClientProvider client={queryClient}>
        <MediaFolderBulkCreateModal open onClose={vi.fn()} />
      </QueryClientProvider>,
    )

    await user.type(screen.getByLabelText('Tên thư mục'), 'Sẽ bị xóa')
    await user.click(screen.getByLabelText(CHANNELS[0].pageName))
    await user.click(screen.getByRole('button', { name: /Tạo trong 1 Page/ }))
    await screen.findByText(/Đã tạo/)

    rerender(
      <QueryClientProvider client={queryClient}>
        <MediaFolderBulkCreateModal open={false} onClose={vi.fn()} />
      </QueryClientProvider>,
    )
    rerender(
      <QueryClientProvider client={queryClient}>
        <MediaFolderBulkCreateModal open onClose={vi.fn()} />
      </QueryClientProvider>,
    )

    expect(screen.getByLabelText('Tên thư mục')).toHaveValue('')
    expect(screen.queryByText(/Đã tạo/)).not.toBeInTheDocument()
    expect(screen.getByLabelText(CHANNELS[0].pageName)).not.toBeChecked()
  })
})
