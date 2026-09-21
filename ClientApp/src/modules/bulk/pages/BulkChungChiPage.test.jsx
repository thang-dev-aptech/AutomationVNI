import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import BulkChungChiPage, { CHUNG_CHI_MODE } from './BulkChungChiPage'
import { bulkApi } from '../services/bulkApi'

const PAGE_A = 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa'
const PAGE_B = 'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb'
const CAT_ID = 'cccccccc-cccc-cccc-cccc-cccccccccccc'
const navigate = vi.fn()

vi.mock('react-router-dom', async (importOriginal) => {
  const actual = await importOriginal()
  return { ...actual, useNavigate: () => navigate }
})

vi.mock('@/modules/social-channels/hooks/useSocialChannels', () => ({
  useSocialChannelAll: () => ({
    data: [
      { id: PAGE_A, pageName: 'Page A' },
      { id: PAGE_B, pageName: 'Page B' },
    ],
    isLoading: false,
  }),
}))

vi.mock('@/modules/categories/hooks/useCategories', () => ({
  useCategoryList: () => ({ data: { items: [{ id: CAT_ID, name: 'Chứng chỉ' }] } }),
}))

vi.mock('@/shared/stores/toastStore', () => ({
  toast: { success: vi.fn(), error: vi.fn(), warning: vi.fn() },
}))

vi.mock('../services/bulkApi', () => ({
  bulkApi: {
    createChungChi: vi.fn(),
  },
}))

function renderPage() {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  })
  return render(
    <QueryClientProvider client={queryClient}>
      <MemoryRouter>
        <BulkChungChiPage />
      </MemoryRouter>
    </QueryClientProvider>,
  )
}

describe('BulkChungChiPage', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    bulkApi.createChungChi.mockResolvedValue({
      data: { success: true, data: { batchId: 'batch-cc-1', created: 2 } },
    })
  })

  it('shows a count input for Random and hides it for All', () => {
    renderPage()
    expect(screen.getByLabelText('Số ảnh mỗi bài')).toBeInTheDocument()

    fireEvent.click(screen.getByRole('radio', { name: 'Tất cả' }))
    expect(screen.queryByLabelText('Số ảnh mỗi bài')).not.toBeInTheDocument()

    fireEvent.click(screen.getByRole('radio', { name: 'Ngẫu nhiên' }))
    expect(screen.getByLabelText('Số ảnh mỗi bài')).toBeInTheDocument()
  })

  it('submits POST payload matching BulkCreateChungChiRequest then navigates to the batch', async () => {
    renderPage()

    fireEvent.change(screen.getByLabelText('Ý tưởng 1'), { target: { value: 'Khai giảng khóa mới' } })
    fireEvent.change(screen.getByLabelText('Loại bài (tuỳ chọn)'), { target: { value: CAT_ID } })
    fireEvent.change(screen.getByLabelText('Số ảnh mỗi bài'), { target: { value: '3' } })

    fireEvent.click(screen.getByRole('button', { name: 'Chọn page' }))
    fireEvent.click(screen.getByRole('button', { name: 'Chọn hết kết quả' }))

    fireEvent.click(screen.getByRole('button', { name: /Tạo 2 bài/ }))

    await waitFor(() => {
      expect(bulkApi.createChungChi).toHaveBeenCalledTimes(1)
    })
    expect(bulkApi.createChungChi).toHaveBeenCalledWith({
      items: [{ idea: 'Khai giảng khóa mới', categoryId: CAT_ID }],
      channelIds: [PAGE_A, PAGE_B],
      mode: CHUNG_CHI_MODE.Random,
      randomCount: 3,
    })
    expect(navigate).toHaveBeenCalledWith('/bulk/batch-cc-1')
  })

  it('omits randomCount in All mode', async () => {
    renderPage()

    fireEvent.change(screen.getByLabelText('Ý tưởng 1'), { target: { value: 'Tất cả ảnh' } })
    fireEvent.click(screen.getByRole('radio', { name: 'Tất cả' }))
    fireEvent.click(screen.getByRole('button', { name: 'Chọn page' }))
    fireEvent.click(screen.getByRole('button', { name: 'Chọn hết kết quả' }))
    fireEvent.click(screen.getByRole('button', { name: /Tạo 2 bài/ }))

    await waitFor(() => {
      expect(bulkApi.createChungChi).toHaveBeenCalledWith({
        items: [{ idea: 'Tất cả ảnh' }],
        channelIds: [PAGE_A, PAGE_B],
        mode: CHUNG_CHI_MODE.All,
      })
    })
    expect(bulkApi.createChungChi.mock.calls[0][0]).not.toHaveProperty('randomCount')
  })
})
