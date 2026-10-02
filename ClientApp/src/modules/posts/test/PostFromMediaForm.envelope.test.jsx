import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { postApi } from '../services/postApi'
import PostFromMediaForm from '../components/PostFromMediaForm'

const navigate = vi.fn()

vi.mock('react-router-dom', async (importOriginal) => {
  const actual = await importOriginal()
  return { ...actual, useNavigate: () => navigate }
})

vi.mock('@/shared/stores/toastStore', () => ({
  toast: { success: vi.fn(), error: vi.fn(), warning: vi.fn() },
}))

vi.mock('@/modules/media/hooks/useMediaFolders', () => ({
  useWritableMediaFolderPages: () => ({
    data: [
      { id: 'page-1', pageName: 'Page Một' },
      { id: 'page-2', pageName: 'Page Hai' },
    ],
    isLoading: false,
    isError: false,
  }),
}))

vi.mock('@/modules/media/hooks/useMediaAssets', () => ({
  useGenerateCaption: () => ({ mutateAsync: vi.fn(), isPending: false }),
}))

vi.mock('../services/postApi', () => ({
  postApi: { createFromMedia: vi.fn() },
  postQueryKeys: {
    all: ['posts'],
    list: (params) => ['posts', 'list', params],
    detail: (id) => ['posts', 'detail', id],
    generationStatus: (id) => ['posts', 'generation-status', id],
    timeline: (id) => ['posts', 'timeline', id],
  },
}))

vi.mock('@/modules/media/components/MediaFolderPickerModal', () => ({
  default: ({ open, onConfirm }) => (open ? (
    <button type="button" onClick={() => onConfirm([{
      id: 'img-1',
      publicUrl: '/a.jpg',
      originalFileName: 'a.jpg',
      mimeType: 'image/jpeg',
      caption: 'caption có sẵn',
    }])}
    >
      Xác nhận ảnh
    </button>
  ) : null),
}))

function renderForm() {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  })
  return render(
    <QueryClientProvider client={queryClient}>
      <MemoryRouter>
        <PostFromMediaForm />
      </MemoryRouter>
    </QueryClientProvider>,
  )
}

describe('PostFromMediaForm API envelope', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('navigates from the real envelope: one page uses id, many pages use batchId', async () => {
    postApi.createFromMedia
      .mockResolvedValueOnce({ data: { success: true, data: { id: 'post-real' } } })
      .mockResolvedValueOnce({
        data: { success: true, data: { batchId: 'batch-real', created: 2, postIds: ['a', 'b'] } },
      })
    const user = userEvent.setup()
    renderForm()

    await user.click(screen.getByRole('button', { name: 'Chọn ảnh từ Media' }))
    await user.click(screen.getByRole('button', { name: 'Xác nhận ảnh' }))
    await user.click(screen.getByLabelText('Page Một'))
    await user.click(screen.getByRole('button', { name: 'Tạo bài' }))
    expect(navigate).toHaveBeenCalledWith('/posts/post-real')

    await user.click(screen.getByLabelText('Page Hai'))
    await user.click(screen.getByRole('button', { name: 'Tạo bài' }))
    expect(navigate).toHaveBeenCalledWith('/bulk/batch-real')
  })
})
