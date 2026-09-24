import { useState } from 'react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import MediaFolderSearchBox from '../components/MediaFolderSearchBox'
import { mediaFolderApi } from '../services/mediaFolderApi'
import { useMediaAssets } from '../hooks/useMediaAssets'
import { PAGE_A, PAGE_B, deferred, wrapPaged } from './mediaFolderExplorerFixtures'

vi.mock('../services/mediaFolderApi', async (importOriginal) => {
  const actual = await importOriginal()
  return {
    ...actual,
    mediaFolderApi: {
      ...actual.mediaFolderApi,
      searchGlobal: vi.fn(),
    },
  }
})

vi.mock('../hooks/useMediaAssets', () => ({
  useMediaAssets: vi.fn(),
}))

const RESULT_A1 = { id: 'r-a1', name: 'Campaign A', fullPath: 'Campaign A', socialChannelId: PAGE_A, pageName: 'Page A' }
const RESULT_A2 = { id: 'r-a2', name: 'Campaign A', fullPath: 'Archive / Campaign A', socialChannelId: PAGE_A, pageName: 'Page A' }
const RESULT_B1 = { id: 'r-b1', name: 'Campaign A', fullPath: 'Campaign A', socialChannelId: PAGE_B, pageName: 'Page B' }

const FILE_1 = { id: 'f-1', fileName: 'banner.jpg', originalFileName: 'banner.jpg', previewUrl: '/preview/f-1', folderId: 'folder-a', socialChannelId: PAGE_A }
const FILE_UNASSIGNED = { id: 'f-2', fileName: 'loose.png', originalFileName: 'loose.png', previewUrl: '/preview/f-2', folderId: null, socialChannelId: null }

function createQueryClient() {
  return new QueryClient({
    defaultOptions: { queries: { retry: false, refetchOnWindowFocus: false, staleTime: 0 } },
  })
}

// MediaFolderSearchBox is a controlled component (value/onChange from the parent, same as
// MediaPage.jsx) — a tiny stateful wrapper here mirrors that so typing behaves naturally.
function ControlledBox(props) {
  const [value, setValue] = useState(props.initialValue ?? '')
  return <MediaFolderSearchBox {...props} value={value} onChange={setValue} />
}

function renderBox(props = {}) {
  const queryClient = createQueryClient()
  const user = userEvent.setup()
  const onOpenFolder = props.onOpenFolder ?? vi.fn()
  const onOpenFile = props.onOpenFile ?? vi.fn()
  const view = render(
    <QueryClientProvider client={queryClient}>
      <ControlledBox onOpenFolder={onOpenFolder} onOpenFile={onOpenFile} {...props} />
    </QueryClientProvider>,
  )
  return { user, onOpenFolder, onOpenFile, queryClient, ...view }
}

describe('search-global: unified folder + file search box', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    useMediaAssets.mockReturnValue({ data: { items: [], total: 0 }, isLoading: false, isError: false, error: null })
  })

  it('does not call either search until a keyword is typed', () => {
    renderBox()
    expect(mediaFolderApi.searchGlobal).not.toHaveBeenCalled()
    expect(useMediaAssets).toHaveBeenCalledWith(
      expect.objectContaining({ keyword: '' }),
      expect.objectContaining({ enabled: false }),
    )
  })

  it('searches folders globally and shows results with page name and fullPath', async () => {
    mediaFolderApi.searchGlobal.mockResolvedValue(wrapPaged([RESULT_A1, RESULT_A2, RESULT_B1]))
    const { user } = renderBox()

    await user.type(screen.getByLabelText('Tìm thư mục hoặc tệp'), 'Campaign')

    await waitFor(() => expect(mediaFolderApi.searchGlobal).toHaveBeenCalledWith(
      expect.objectContaining({ keyword: 'Campaign' }),
    ))
    expect(await screen.findByText('Archive / Campaign A')).toBeInTheDocument()
    expect(screen.getAllByText('📁 Campaign A')).toHaveLength(3)
    expect(screen.getAllByText('Page A')).toHaveLength(2)
    expect(screen.getByText('Page B')).toBeInTheDocument()
  })

  it('selecting a folder result calls onOpenFolder and clears the input', async () => {
    mediaFolderApi.searchGlobal.mockResolvedValue(wrapPaged([RESULT_A2]))
    const { user, onOpenFolder } = renderBox()

    await user.type(screen.getByLabelText('Tìm thư mục hoặc tệp'), 'Campaign')
    await user.click(await screen.findByText('Archive / Campaign A'))

    expect(onOpenFolder).toHaveBeenCalledWith(RESULT_A2.id, RESULT_A2.socialChannelId)
    expect(screen.getByLabelText('Tìm thư mục hoặc tệp')).toHaveValue('')
  })

  it('also searches files by keyword once typed, enabled only then', async () => {
    mediaFolderApi.searchGlobal.mockResolvedValue(wrapPaged([]))
    useMediaAssets.mockReturnValue({ data: { items: [FILE_1], total: 1 }, isLoading: false, isError: false, error: null })
    const { user } = renderBox()

    await user.type(screen.getByLabelText('Tìm thư mục hoặc tệp'), 'banner')

    await waitFor(() => expect(useMediaAssets).toHaveBeenCalledWith(
      { keyword: 'banner', index: 1, size: 5 },
      { enabled: true },
    ))
    expect(await screen.findByText('banner.jpg')).toBeInTheDocument()
  })

  it('selecting a file result with a folder calls onOpenFile and clears the input', async () => {
    mediaFolderApi.searchGlobal.mockResolvedValue(wrapPaged([]))
    useMediaAssets.mockReturnValue({ data: { items: [FILE_1], total: 1 }, isLoading: false, isError: false, error: null })
    const { user, onOpenFile } = renderBox()

    await user.type(screen.getByLabelText('Tìm thư mục hoặc tệp'), 'banner')
    await user.click(await screen.findByText('banner.jpg'))

    expect(onOpenFile).toHaveBeenCalledWith(FILE_1)
    expect(screen.getByLabelText('Tìm thư mục hoặc tệp')).toHaveValue('')
  })

  it('shows "Chưa phân loại" for a matched file with no folder', async () => {
    mediaFolderApi.searchGlobal.mockResolvedValue(wrapPaged([]))
    useMediaAssets.mockReturnValue({ data: { items: [FILE_UNASSIGNED], total: 1 }, isLoading: false, isError: false, error: null })
    const { user } = renderBox()

    await user.type(screen.getByLabelText('Tìm thư mục hoặc tệp'), 'loose')

    expect(await screen.findByText('Chưa phân loại')).toBeInTheDocument()
  })

  it('shows not-found hints separately for folders and files with no matches', async () => {
    mediaFolderApi.searchGlobal.mockResolvedValue(wrapPaged([]))
    useMediaAssets.mockReturnValue({ data: { items: [], total: 0 }, isLoading: false, isError: false, error: null })
    const { user } = renderBox()

    await user.type(screen.getByLabelText('Tìm thư mục hoặc tệp'), 'zzz')

    expect(await screen.findByText('Không tìm thấy thư mục nào khớp.')).toBeInTheDocument()
    expect(await screen.findByText('Không tìm thấy tệp nào khớp.')).toBeInTheDocument()
  })

  it('hints at remaining file matches beyond the dropdown limit', async () => {
    mediaFolderApi.searchGlobal.mockResolvedValue(wrapPaged([]))
    useMediaAssets.mockReturnValue({ data: { items: [FILE_1], total: 12 }, isLoading: false, isError: false, error: null })
    const { user } = renderBox()

    await user.type(screen.getByLabelText('Tìm thư mục hoặc tệp'), 'banner')

    expect(await screen.findByText('+7 tệp khác khớp — xem lưới bên dưới.')).toBeInTheDocument()
  })

  it('clears results when keyword is cleared', async () => {
    mediaFolderApi.searchGlobal.mockResolvedValue(wrapPaged([RESULT_A2]))
    useMediaAssets.mockReturnValue({ data: { items: [FILE_1], total: 1 }, isLoading: false, isError: false, error: null })
    const { user } = renderBox()

    await user.type(screen.getByLabelText('Tìm thư mục hoặc tệp'), 'Campaign')
    expect(await screen.findByText('Archive / Campaign A')).toBeInTheDocument()

    await user.clear(screen.getByLabelText('Tìm thư mục hoặc tệp'))
    expect(screen.queryByText('Archive / Campaign A')).not.toBeInTheDocument()
    expect(screen.queryByText('banner.jpg')).not.toBeInTheDocument()
  })

  it('tracks recently opened folders as quick-access chips with page names', async () => {
    mediaFolderApi.searchGlobal.mockResolvedValue(wrapPaged([RESULT_A2]))
    const { user, onOpenFolder } = renderBox()

    await user.type(screen.getByLabelText('Tìm thư mục hoặc tệp'), 'Campaign')
    await user.click(await screen.findByText('Archive / Campaign A'))

    expect(onOpenFolder).toHaveBeenCalledWith(RESULT_A2.id, RESULT_A2.socialChannelId)
    expect(screen.getByRole('button', { name: '📁 Campaign A (Page A)' })).toBeInTheDocument()

    await user.click(screen.getByRole('button', { name: '📁 Campaign A (Page A)' }))
    expect(onOpenFolder).toHaveBeenLastCalledWith(RESULT_A2.id, RESULT_A2.socialChannelId)
  })

  it('shows loading state while folder search is pending', async () => {
    const pending = deferred()
    mediaFolderApi.searchGlobal.mockReturnValue(pending.promise)
    const { user } = renderBox()

    await user.type(screen.getByLabelText('Tìm thư mục hoặc tệp'), 'Campaign')

    expect(screen.getAllByText('Đang tìm...').length).toBeGreaterThan(0)

    pending.resolve(wrapPaged([RESULT_A1]))
    await waitFor(() => expect(screen.queryByText('Đang tìm...')).not.toBeInTheDocument())
    expect(screen.getByText('Campaign A')).toBeInTheDocument()
  })
})
