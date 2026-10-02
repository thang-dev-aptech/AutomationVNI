import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import PostCreatePage from '../pages/PostCreatePage'

const navigate = vi.fn()
const createFromMedia = vi.fn()
const createAndGenerate = vi.fn()
const generateCaption = vi.fn()

vi.mock('react-router-dom', async (importOriginal) => {
  const actual = await importOriginal()
  return { ...actual, useNavigate: () => navigate }
})

vi.mock('@/shared/stores/toastStore', () => ({
  toast: { success: vi.fn(), error: vi.fn(), warning: vi.fn() },
}))

vi.mock('@/modules/social-channels/hooks/useSocialChannels', () => ({
  useSocialChannelAll: () => ({
    data: [{ id: 'social-decoy', pageName: 'Kênh không dùng' }],
    isLoading: false,
    isError: false,
    refetch: vi.fn(),
  }),
}))

vi.mock('@/modules/prompt-templates/hooks/usePromptTemplates', () => ({
  usePromptTemplateList: () => ({ data: { items: [] }, isLoading: false }),
}))

vi.mock('@/modules/page-contexts/hooks/usePageContexts', () => ({
  usePageContextList: () => ({ data: { items: [] }, isLoading: false }),
}))

vi.mock('@/modules/categories/hooks/useCategories', () => ({
  useCategoryList: () => ({
    data: { items: [{ id: 'cat-1', name: 'Tin tức' }] },
    isLoading: false,
  }),
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
  useGenerateCaption: () => ({ mutateAsync: generateCaption, isPending: false }),
}))

vi.mock('../hooks/usePosts', () => ({
  useCreateAndGeneratePost: () => ({ mutateAsync: createAndGenerate, isPending: false }),
  useCreatePostFromMedia: () => ({ mutateAsync: createFromMedia, isPending: false }),
}))

vi.mock('../components/PostCreateForm', () => ({
  default: ({ flow, onSubmit }) => (
    <button type="button" onClick={() => onSubmit({ flow, idea: 'ý tưởng cũ' })}>
      Gửi AI
    </button>
  ),
}))

let pickerAssets = []
vi.mock('@/modules/media/components/MediaFolderPickerModal', () => ({
  default: ({ open, onConfirm, onClose }) => (open ? (
    <div>
      <button type="button" onClick={() => onConfirm(pickerAssets)}>Xác nhận ảnh trong popup</button>
      <button type="button" onClick={onClose}>Đóng popup</button>
    </div>
  ) : null),
}))

const PAGE_1 = 'page-1'
const PAGE_2 = 'page-2'
const IMG = {
  id: 'img-1',
  publicUrl: '/a.jpg',
  originalFileName: 'a.jpg',
  mimeType: 'image/jpeg',
  caption: 'caption có sẵn',
}
const IMG_EMPTY = { ...IMG, id: 'img-empty', caption: '' }

async function openMediaFlow(user) {
  await user.click(screen.getByRole('button', { name: /Dùng ảnh có sẵn trong Media/ }))
  await user.click(screen.getByRole('button', { name: 'Tiếp tục' }))
}

describe('PostCreatePage media flow', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    pickerAssets = [IMG]
    createFromMedia.mockResolvedValue({ id: 'post-9' })
    generateCaption.mockResolvedValue({ caption: 'caption gợi ý' })
  })

  it('adds the media method and keeps the two existing AI flows', async () => {
    const user = userEvent.setup()
    render(<MemoryRouter><PostCreatePage /></MemoryRouter>)

    expect(screen.getByRole('button', { name: /Sinh toàn bộ bằng AI/ })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /AI sinh text, ghép vào ảnh mẫu/ })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /Dùng ảnh có sẵn trong Media/ })).toBeInTheDocument()

    await user.click(screen.getByRole('button', { name: 'Tiếp tục' }))
    await user.click(screen.getByRole('button', { name: 'Gửi AI' }))
    expect(createAndGenerate).toHaveBeenCalledWith({ flow: 'fullai', idea: 'ý tưởng cũ' })
    expect(screen.queryByRole('button', { name: 'Chọn ảnh từ Media' })).not.toBeInTheDocument()
  })

  it('opens the picker, shows the cover thumbnail and prefills caption', async () => {
    const user = userEvent.setup()
    render(<MemoryRouter><PostCreatePage /></MemoryRouter>)
    await openMediaFlow(user)

    expect(screen.queryByText('Xác nhận ảnh trong popup')).not.toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'Chọn ảnh từ Media' }))
    await user.click(screen.getByRole('button', { name: 'Xác nhận ảnh trong popup' }))

    expect(screen.getByRole('img', { name: 'a.jpg' })).toBeInTheDocument()
    expect(screen.getByText('Ảnh bìa')).toBeInTheDocument()
    expect(screen.getByLabelText('Caption')).toHaveValue('caption có sẵn')
    expect(screen.getByLabelText('Page Một')).toBeInTheDocument()
    expect(screen.getByLabelText('Page Hai')).toBeInTheDocument()
    expect(screen.queryByLabelText('Kênh không dùng')).not.toBeInTheDocument()
  })

  it('leaves caption empty when the image has none and does not overwrite typed text', async () => {
    pickerAssets = [IMG_EMPTY]
    const user = userEvent.setup()
    render(<MemoryRouter><PostCreatePage /></MemoryRouter>)
    await openMediaFlow(user)
    await user.click(screen.getByRole('button', { name: 'Chọn ảnh từ Media' }))
    await user.click(screen.getByRole('button', { name: 'Xác nhận ảnh trong popup' }))
    expect(screen.getByLabelText('Caption')).toHaveValue('')

    await user.type(screen.getByLabelText('Caption'), 'đang gõ')
    pickerAssets = [IMG]
    await user.click(screen.getByRole('button', { name: 'Chọn ảnh từ Media' }))
    await user.click(screen.getByRole('button', { name: 'Xác nhận ảnh trong popup' }))
    expect(screen.getByLabelText('Caption')).toHaveValue('đang gõ')
  })

  it('fills a suggested caption and keeps typed text when suggestion fails', async () => {
    const { toast } = await import('@/shared/stores/toastStore')
    const user = userEvent.setup()
    render(<MemoryRouter><PostCreatePage /></MemoryRouter>)
    await openMediaFlow(user)
    await user.click(screen.getByRole('button', { name: 'Chọn ảnh từ Media' }))
    await user.click(screen.getByRole('button', { name: 'Xác nhận ảnh trong popup' }))
    await user.click(screen.getByRole('button', { name: '✨ Gợi ý caption' }))
    expect(generateCaption).toHaveBeenCalledWith('img-1')
    expect(screen.getByLabelText('Caption')).toHaveValue('caption gợi ý')

    generateCaption.mockRejectedValueOnce(Object.assign(new Error('conflict'), {
      response: { status: 409, data: { message: 'Ảnh đang chờ caption' } },
    }))
    await user.clear(screen.getByLabelText('Caption'))
    await user.type(screen.getByLabelText('Caption'), 'giữ lại')
    await user.click(screen.getByRole('button', { name: '✨ Gợi ý caption' }))
    expect(screen.getByLabelText('Caption')).toHaveValue('giữ lại')
    expect(toast.error).toHaveBeenCalledWith('Ảnh đang chờ caption')
  })

  it('disables create until an image, caption and page are set, then navigates', async () => {
    const user = userEvent.setup()
    render(<MemoryRouter><PostCreatePage /></MemoryRouter>)
    await openMediaFlow(user)
    const submit = screen.getByRole('button', { name: 'Tạo bài' })
    expect(submit).toBeDisabled()

    await user.click(screen.getByRole('button', { name: 'Chọn ảnh từ Media' }))
    await user.click(screen.getByRole('button', { name: 'Xác nhận ảnh trong popup' }))
    await user.click(screen.getByLabelText('Page Một'))
    await user.selectOptions(screen.getByLabelText('Danh mục'), 'cat-1')
    expect(submit).toBeEnabled()

    await user.click(submit)
    expect(createFromMedia).toHaveBeenCalledWith({
      mediaIds: ['img-1'],
      socialChannelIds: [PAGE_1],
      content: 'caption có sẵn',
      categoryId: 'cat-1',
    })
    expect(navigate).toHaveBeenCalledWith('/posts/post-9')

    createFromMedia.mockResolvedValueOnce({ batchId: 'batch-7', created: 2, postIds: ['p1', 'p2'] })
    await user.click(screen.getByLabelText('Page Hai'))
    await user.click(submit)
    expect(createFromMedia).toHaveBeenLastCalledWith(expect.objectContaining({
      socialChannelIds: [PAGE_1, PAGE_2],
    }))
    expect(navigate).toHaveBeenCalledWith('/bulk/batch-7')
  })
})
