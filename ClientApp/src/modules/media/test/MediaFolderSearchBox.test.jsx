import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import MediaFolderSearchBox from '../components/MediaFolderSearchBox'
import { mediaFolderApi } from '../services/mediaFolderApi'
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

const RESULT_A1 = { id: 'r-a1', name: 'Campaign A', fullPath: 'Campaign A', socialChannelId: PAGE_A, pageName: 'Page A' }
const RESULT_A2 = { id: 'r-a2', name: 'Campaign A', fullPath: 'Archive / Campaign A', socialChannelId: PAGE_A, pageName: 'Page A' }
const RESULT_B1 = { id: 'r-b1', name: 'Campaign A', fullPath: 'Campaign A', socialChannelId: PAGE_B, pageName: 'Page B' }

function createQueryClient() {
  return new QueryClient({
    defaultOptions: { queries: { retry: false, refetchOnWindowFocus: false, staleTime: 0 } },
  })
}

function renderBox(props = {}) {
  const queryClient = createQueryClient()
  const user = userEvent.setup()
  const onOpenFolder = props.onOpenFolder ?? vi.fn()
  const view = render(
    <QueryClientProvider client={queryClient}>
      <MediaFolderSearchBox onOpenFolder={onOpenFolder} {...props} />
    </QueryClientProvider>,
  )
  return { user, onOpenFolder, queryClient, ...view }
}

describe('search-global: Global folder search box', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('does not call search until a keyword is typed', () => {
    renderBox()
    expect(mediaFolderApi.searchGlobal).not.toHaveBeenCalled()
  })

  it('searches globally and shows results with page name and fullPath', async () => {
    mediaFolderApi.searchGlobal.mockResolvedValue(wrapPaged([RESULT_A1, RESULT_A2, RESULT_B1]))
    const { user } = renderBox()

    await user.type(screen.getByLabelText('Tìm thư mục'), 'Campaign')

    await waitFor(() => expect(mediaFolderApi.searchGlobal).toHaveBeenCalledWith(
      expect.objectContaining({ keyword: 'Campaign' }),
    ))
    expect(await screen.findByText('Archive / Campaign A')).toBeInTheDocument()
    expect(screen.getAllByText('📁 Campaign A')).toHaveLength(3)
    expect(screen.getAllByText('Page A')).toHaveLength(2)
    expect(screen.getByText('Page B')).toBeInTheDocument()
  })

  it('selecting a result calls onOpenFolder with both folderId and socialChannelId', async () => {
    mediaFolderApi.searchGlobal.mockResolvedValue(wrapPaged([RESULT_A2]))
    const { user, onOpenFolder } = renderBox()

    await user.type(screen.getByLabelText('Tìm thư mục'), 'Campaign')
    await user.click(await screen.findByText('Archive / Campaign A'))

    expect(onOpenFolder).toHaveBeenCalledWith(RESULT_A2.id, RESULT_A2.socialChannelId)
    expect(screen.getByLabelText('Tìm thư mục')).toHaveValue('')
  })

  it('shows a not-found hint when the search returns no matches', async () => {
    mediaFolderApi.searchGlobal.mockResolvedValue(wrapPaged([]))
    const { user } = renderBox()

    await user.type(screen.getByLabelText('Tìm thư mục'), 'zzz')

    expect(await screen.findByText('Không tìm thấy thư mục nào khớp.')).toBeInTheDocument()
  })

  it('clears results and resets query state when keyword is cleared', async () => {
    mediaFolderApi.searchGlobal.mockResolvedValue(wrapPaged([RESULT_A2]))
    const { user } = renderBox()

    await user.type(screen.getByLabelText('Tìm thư mục'), 'Campaign')
    expect(await screen.findByText('Archive / Campaign A')).toBeInTheDocument()

    await user.clear(screen.getByLabelText('Tìm thư mục'))
    expect(screen.queryByText('Archive / Campaign A')).not.toBeInTheDocument()
  })

  it('tracks recently opened folders as quick-access chips with page names', async () => {
    mediaFolderApi.searchGlobal.mockResolvedValue(wrapPaged([RESULT_A2]))
    const { user, onOpenFolder } = renderBox()

    await user.type(screen.getByLabelText('Tìm thư mục'), 'Campaign')
    await user.click(await screen.findByText('Archive / Campaign A'))

    expect(onOpenFolder).toHaveBeenCalledWith(RESULT_A2.id, RESULT_A2.socialChannelId)
    expect(screen.getByRole('button', { name: '📁 Campaign A (Page A)' })).toBeInTheDocument()

    await user.click(screen.getByRole('button', { name: '📁 Campaign A (Page A)' }))
    expect(onOpenFolder).toHaveBeenLastCalledWith(RESULT_A2.id, RESULT_A2.socialChannelId)
  })

  it('shows loading state while searching', async () => {
    const pending = deferred()
    mediaFolderApi.searchGlobal.mockReturnValue(pending.promise)
    const { user } = renderBox()

    await user.type(screen.getByLabelText('Tìm thư mục'), 'Campaign')

    expect(screen.getByText('Đang tìm...')).toBeInTheDocument()

    pending.resolve(wrapPaged([RESULT_A1]))
    await waitFor(() => expect(screen.queryByText('Đang tìm...')).not.toBeInTheDocument())
    expect(screen.getByText('Campaign A')).toBeInTheDocument()
  })
})
