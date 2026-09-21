import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import BulkChungChiPage, { CHUNG_CHI_MODE } from './BulkChungChiPage'
import { bulkApi } from '../services/bulkApi'
import { useChungChiEligiblePages } from '@/modules/media/hooks/useMediaFolders'

const PAGE_A = 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa'
const PAGE_B = 'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb'
const CAT_ID = 'cccccccc-cccc-cccc-cccc-cccccccccccc'
const navigate = vi.fn()

vi.mock('react-router-dom', async (importOriginal) => {
  const actual = await importOriginal()
  return { ...actual, useNavigate: () => navigate }
})

vi.mock('@/modules/media/hooks/useMediaFolders', () => ({
  useChungChiEligiblePages: vi.fn(),
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
    useChungChiEligiblePages.mockReturnValue({
      data: [
        { id: PAGE_A, pageName: 'Page A' },
        { id: PAGE_B, pageName: 'Page B' },
      ],
      isLoading: false,
      isError: false,
      refetch: vi.fn(),
    })
    bulkApi.createChungChi.mockResolvedValue({
      data: { success: true, data: { batchId: 'batch-cc-1', created: 2 } },
    })
  })

  it('shows an actionable empty state when no Page has a chung_chi image', () => {
    useChungChiEligiblePages.mockReturnValue({ data: [], isLoading: false, isError: false })

    renderPage()

    expect(screen.getByText('Chưa có Page nào có ảnh trong thư mục chung_chi.')).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Đến Thư mục Media' })).toHaveAttribute('href', '/media')
    expect(screen.queryByRole('button', { name: 'Chọn page' })).not.toBeInTheDocument()
  })

  it('does not pass malformed channel data to ChannelMultiSelect', () => {
    useChungChiEligiblePages.mockReturnValue({
      data: '<!doctype html>',
      isLoading: false,
      isError: false,
    })

    expect(() => renderPage()).not.toThrow()
    expect(screen.getByText('Chưa có Page nào có ảnh trong thư mục chung_chi.')).toBeInTheDocument()
  })

  it('shows a retryable error when the eligible Page response is invalid', () => {
    const refetch = vi.fn()
    useChungChiEligiblePages.mockReturnValue({
      isLoading: false,
      isError: true,
      error: new TypeError('Dữ liệu Page chứng chỉ không hợp lệ. Vui lòng tải lại trang.'),
      refetch,
    })

    renderPage()
    expect(screen.getByText('Dữ liệu Page chứng chỉ không hợp lệ. Vui lòng tải lại trang.')).toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: /Thử lại/i }))
    expect(refetch).toHaveBeenCalledTimes(1)
  })

  it('shows a count input for Random and hides it for All', () => {
    renderPage()
    expect(screen.getByLabelText('Số ảnh mỗi bài')).toBeInTheDocument()

    fireEvent.click(screen.getByRole('radio', { name: 'Tất cả' }))
    expect(screen.queryByLabelText('Số ảnh mỗi bài')).not.toBeInTheDocument()

    fireEvent.click(screen.getByRole('radio', { name: 'Ngẫu nhiên' }))
    expect(screen.getByLabelText('Số ảnh mỗi bài')).toBeInTheDocument()
  })

  it('does not show or require an idea section', () => {
    renderPage()

    expect(screen.queryByText(/Ý tưởng/i)).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: '+ Thêm dòng' })).not.toBeInTheDocument()
    expect(screen.getByRole('button', { name: /Tạo 0 bài/ })).toBeDisabled()

    fireEvent.click(screen.getByRole('button', { name: 'Chọn page' }))
    fireEvent.click(screen.getByRole('button', { name: 'Chọn hết kết quả' }))

    expect(screen.getByRole('button', { name: /Tạo 2 bài/ })).toBeEnabled()
  })

  it('submits POST payload matching BulkCreateChungChiRequest then navigates to the batch', async () => {
    renderPage()

    fireEvent.change(screen.getByLabelText('Loại bài (tuỳ chọn)'), { target: { value: CAT_ID } })
    fireEvent.change(screen.getByLabelText('Số ảnh mỗi bài'), { target: { value: '3' } })

    fireEvent.click(screen.getByRole('button', { name: 'Chọn page' }))
    fireEvent.click(screen.getByRole('button', { name: 'Chọn hết kết quả' }))

    fireEvent.click(screen.getByRole('button', { name: /Tạo 2 bài/ }))

    await waitFor(() => {
      expect(bulkApi.createChungChi).toHaveBeenCalledTimes(1)
    })
    expect(bulkApi.createChungChi).toHaveBeenCalledWith({
      items: [{ idea: 'Chứng chỉ', categoryId: CAT_ID }],
      channelIds: [PAGE_A, PAGE_B],
      mode: CHUNG_CHI_MODE.Random,
      randomCount: 3,
    })
    expect(navigate).toHaveBeenCalledWith('/bulk/batch-cc-1')
  })

  it('omits randomCount in All mode', async () => {
    renderPage()

    fireEvent.click(screen.getByRole('radio', { name: 'Tất cả' }))
    fireEvent.click(screen.getByRole('button', { name: 'Chọn page' }))
    fireEvent.click(screen.getByRole('button', { name: 'Chọn hết kết quả' }))
    fireEvent.click(screen.getByRole('button', { name: /Tạo 2 bài/ }))

    await waitFor(() => {
      expect(bulkApi.createChungChi).toHaveBeenCalledWith({
        items: [{ idea: 'Chứng chỉ' }],
        channelIds: [PAGE_A, PAGE_B],
        mode: CHUNG_CHI_MODE.All,
      })
    })
    expect(bulkApi.createChungChi.mock.calls[0][0]).not.toHaveProperty('randomCount')
  })
})
