import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { act, fireEvent, render, renderHook, screen, waitFor } from '@testing-library/react'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import MediaCaptionJobPage from '../pages/MediaCaptionJobPage'
import MediaCaptionJobListPage from '../pages/MediaCaptionJobListPage'
import { useMediaCaptionJob } from '../hooks/useMediaCaptionJobs'
import { mediaCaptionJobApi } from '../services/mediaCaptionJobApi'
import { toast } from '@/shared/stores/toastStore'

vi.mock('@/shared/stores/toastStore', () => ({
  toast: { success: vi.fn(), error: vi.fn(), warning: vi.fn() },
}))

vi.mock('../services/mediaCaptionJobApi', async (importOriginal) => {
  const actual = await importOriginal()
  return {
    ...actual,
    mediaCaptionJobApi: { create: vi.fn(), list: vi.fn(), get: vi.fn(), retryFailed: vi.fn(), retryItem: vi.fn() },
  }
})

const JOB_ID = 'j0000000-0000-4000-8000-000000000001'
const FOLDER_ID = 'f0000000-0000-4000-8000-000000000001'

const item = (id, status, extra = {}) => ({
  id, mediaAssetId: `asset-${id}`, fileName: `${id}.png`, status, error: null, attempts: 1,
  previewUrl: `/preview/${id}`, ...extra,
})

const wrapJob = (overrides = {}) => ({
  data: {
    success: true,
    data: {
      id: JOB_ID, folderId: FOLDER_ID, folderName: 'Drive Folder', status: 'Running',
      total: 4, succeeded: 1, failed: 1, skipped: 0, createdAt: '2026-10-01T00:00:00Z',
      items: [
        item('i1', 'Succeeded'),
        item('i2', 'Failed', { error: 'AI không trả đúng 5 dòng caption' }),
        item('i3', 'Running'),
        item('i4', 'Pending'),
      ],
      ...overrides,
    },
  },
})

const newClient = () => new QueryClient({
  defaultOptions: { queries: { retry: false, refetchOnWindowFocus: false } },
})

function renderJobPage() {
  return render(
    <QueryClientProvider client={newClient()}>
      <MemoryRouter initialEntries={[`/media/caption-jobs/${JOB_ID}`]}>
        <Routes>
          <Route path="/media/caption-jobs/:jobId" element={<MediaCaptionJobPage />} />
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>,
  )
}

describe('MEDIA-CAPTION-02 job page (AC 07f817bc c)', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    mediaCaptionJobApi.get.mockResolvedValue(wrapJob())
    mediaCaptionJobApi.retryItem.mockResolvedValue({ data: { success: true, data: true } })
    mediaCaptionJobApi.retryFailed.mockResolvedValue(wrapJob())
  })

  it('renders folder name, back link, progress counts and each item status; Running is highlighted', async () => {
    renderJobPage()

    expect(await screen.findByText('Sinh caption: Drive Folder')).toBeInTheDocument()
    expect(screen.getByRole('link', { name: '← Về thư mục' })).toHaveAttribute('href', `/media?folder=${FOLDER_ID}`)
    expect(document.querySelector('.media-caption-job-counts').textContent).toMatch(/Xong 1 · Lỗi 1 · Bỏ qua 0 · Tổng 4/)
    expect(screen.getByText('AI không trả đúng 5 dòng caption')).toBeInTheDocument()
    expect(screen.getByAltText('i1.png')).toHaveAttribute('src', '/preview/i1')

    const rows = screen.getAllByRole('row')
    const byStatus = (status) => rows.find((r) => r.dataset.status === status)
    expect(byStatus('Running')).toHaveClass('media-caption-job-row-running')
    expect(byStatus('Pending')).not.toHaveClass('media-caption-job-row-running')
    expect(screen.getByRole('progressbar')).toHaveAttribute('aria-valuenow', '50')
  })

  it('Retry on a Failed item calls retryItem; only Failed items get the button', async () => {
    renderJobPage()
    await screen.findByText('Sinh caption: Drive Folder')

    const retryButtons = screen.getAllByRole('button', { name: 'Retry' })
    expect(retryButtons).toHaveLength(1)
    fireEvent.click(retryButtons[0])

    await waitFor(() => expect(mediaCaptionJobApi.retryItem).toHaveBeenCalledWith('i2'))
  })

  it('Retry tất cả ảnh lỗi calls retryFailed with the job id', async () => {
    renderJobPage()
    await screen.findByText('Sinh caption: Drive Folder')

    fireEvent.click(screen.getByRole('button', { name: 'Retry tất cả ảnh lỗi' }))

    await waitFor(() => expect(mediaCaptionJobApi.retryFailed).toHaveBeenCalledWith(JOB_ID))
  })

  it('surfaces a retry error as a toast', async () => {
    mediaCaptionJobApi.retryItem.mockRejectedValue({ response: { data: { message: 'Chỉ retry được item lỗi' } } })
    renderJobPage()
    await screen.findByText('Sinh caption: Drive Folder')

    fireEvent.click(screen.getByRole('button', { name: 'Retry' }))

    await waitFor(() => expect(toast.error).toHaveBeenCalledWith('Chỉ retry được item lỗi'))
  })
})

describe('MEDIA-CAPTION-02 polling (AC 07f817bc d)', () => {
  const wrapper = ({ children }) => (
    <QueryClientProvider client={newClient()}>{children}</QueryClientProvider>
  )

  beforeEach(() => {
    vi.clearAllMocks()
    vi.useFakeTimers({ shouldAdvanceTime: true })
  })

  afterEach(() => vi.useRealTimers())

  it.each(['Queued', 'Running'])('refetches every ~3s while the job is %s', async (status) => {
    mediaCaptionJobApi.get.mockResolvedValue(wrapJob({ status }))
    renderHook(() => useMediaCaptionJob(JOB_ID), { wrapper })

    await waitFor(() => expect(mediaCaptionJobApi.get).toHaveBeenCalledTimes(1))
    await act(async () => { await vi.advanceTimersByTimeAsync(3100) })
    await waitFor(() => expect(mediaCaptionJobApi.get.mock.calls.length).toBeGreaterThanOrEqual(2))
  })

  it('stops polling once the job is Completed', async () => {
    mediaCaptionJobApi.get.mockResolvedValue(wrapJob({ status: 'Completed' }))
    renderHook(() => useMediaCaptionJob(JOB_ID), { wrapper })

    await waitFor(() => expect(mediaCaptionJobApi.get).toHaveBeenCalledTimes(1))
    await act(async () => { await vi.advanceTimersByTimeAsync(10000) })
    expect(mediaCaptionJobApi.get).toHaveBeenCalledTimes(1)
  })
})

describe('MEDIA-CAPTION-02 job list (AC 07f817bc e)', () => {
  it('lists recent jobs with link to each job page', async () => {
    mediaCaptionJobApi.list.mockResolvedValue({
      data: {
        success: true,
        data: [
          { id: 'ja', folderId: FOLDER_ID, folderName: 'Folder A', status: 'Completed', total: 3, succeeded: 3, failed: 0, skipped: 0, createdAt: '2026-10-01T00:00:00Z', items: [] },
          { id: 'jb', folderId: FOLDER_ID, folderName: 'Folder B', status: 'Running', total: 5, succeeded: 1, failed: 1, skipped: 2, createdAt: '2026-10-01T01:00:00Z', items: [] },
        ],
      },
    })
    render(
      <QueryClientProvider client={newClient()}>
        <MemoryRouter><MediaCaptionJobListPage /></MemoryRouter>
      </QueryClientProvider>,
    )

    expect(await screen.findByRole('link', { name: 'Folder A' })).toHaveAttribute('href', '/media/caption-jobs/ja')
    expect(screen.getByRole('link', { name: 'Folder B' })).toHaveAttribute('href', '/media/caption-jobs/jb')
    expect(screen.getByText('1 / 1 / 2 / 5')).toBeInTheDocument()
  })

  it('shows an empty state when there are no jobs', async () => {
    mediaCaptionJobApi.list.mockResolvedValue({ data: { success: true, data: [] } })
    render(
      <QueryClientProvider client={newClient()}>
        <MemoryRouter><MediaCaptionJobListPage /></MemoryRouter>
      </QueryClientProvider>,
    )
    expect(await screen.findByText('Chưa có job nào.')).toBeInTheDocument()
  })
})
