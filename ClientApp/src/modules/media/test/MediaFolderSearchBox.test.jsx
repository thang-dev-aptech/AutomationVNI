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
      search: vi.fn(),
    },
  }
})

const RESULT_A1 = { id: 'r-a1', name: 'Campaign A', fullPath: 'Campaign A' }
const RESULT_A2 = { id: 'r-a2', name: 'Campaign A', fullPath: 'Archive / Campaign A' }
const RESULT_B1 = { id: 'r-b1', name: 'Campaign A', fullPath: 'Campaign A' } // same name, different Page

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
      <MediaFolderSearchBox socialChannelId={PAGE_A} onOpenFolder={onOpenFolder} {...props} />
    </QueryClientProvider>,
  )
  return { user, onOpenFolder, queryClient, ...view }
}

describe('MEDIA-05 MediaFolderSearchBox', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('renders nothing without a socialChannelId and never calls search', () => {
    const { container } = renderBox({ socialChannelId: null })
    expect(container).toBeEmptyDOMElement()
    expect(mediaFolderApi.search).not.toHaveBeenCalled()
  })

  it('does not call search until a keyword is typed', () => {
    renderBox()
    expect(mediaFolderApi.search).not.toHaveBeenCalled()
  })

  it('MEDIA-05-AC1: searches scoped to the current Page and shows name + fullPath', async () => {
    mediaFolderApi.search.mockResolvedValue(wrapPaged([RESULT_A1, RESULT_A2]))
    const { user } = renderBox()

    await user.type(screen.getByLabelText('Tìm thư mục trong Page'), 'Campaign')

    await waitFor(() => expect(mediaFolderApi.search).toHaveBeenCalledWith(
      expect.objectContaining({ socialChannelId: PAGE_A, keyword: 'Campaign' }),
    ))
    expect(await screen.findByText('Archive / Campaign A')).toBeInTheDocument()
    expect(screen.getAllByText('📁 Campaign A')).toHaveLength(2)
  })

  it('MEDIA-05-AC1: selecting a result opens that exact folder and clears the search', async () => {
    mediaFolderApi.search.mockResolvedValue(wrapPaged([RESULT_A2]))
    const { user, onOpenFolder } = renderBox()

    await user.type(screen.getByLabelText('Tìm thư mục trong Page'), 'Campaign')
    await user.click(await screen.findByText('Archive / Campaign A'))

    expect(onOpenFolder).toHaveBeenCalledWith(RESULT_A2.id)
    expect(screen.getByLabelText('Tìm thư mục trong Page')).toHaveValue('')
  })

  it('shows a not-found hint when the search returns no matches', async () => {
    mediaFolderApi.search.mockResolvedValue(wrapPaged([]))
    const { user } = renderBox()

    await user.type(screen.getByLabelText('Tìm thư mục trong Page'), 'zzz')

    expect(await screen.findByText('Không tìm thấy thư mục nào khớp.')).toBeInTheDocument()
  })

  it('MEDIA-05-AC2: switching Page while a search is pending clears the keyword and ignores the late Page A response', async () => {
    const pendingA = deferred()
    mediaFolderApi.search.mockImplementation(({ socialChannelId }) => (
      socialChannelId === PAGE_A ? pendingA.promise : Promise.resolve(wrapPaged([RESULT_B1]))
    ))

    const queryClient = createQueryClient()
    const user = userEvent.setup()
    const onOpenFolder = vi.fn()
    const { rerender } = render(
      <QueryClientProvider client={queryClient}>
        <MediaFolderSearchBox socialChannelId={PAGE_A} onOpenFolder={onOpenFolder} />
      </QueryClientProvider>,
    )

    await user.type(screen.getByLabelText('Tìm thư mục trong Page'), 'Campaign')
    expect(screen.getByText('Đang tìm...')).toBeInTheDocument()

    rerender(
      <QueryClientProvider client={queryClient}>
        <MediaFolderSearchBox socialChannelId={PAGE_B} onOpenFolder={onOpenFolder} />
      </QueryClientProvider>,
    )

    expect(screen.getByLabelText('Tìm thư mục trong Page')).toHaveValue('')
    expect(screen.queryByText('Đang tìm...')).not.toBeInTheDocument()
    expect(screen.queryByText('Archive / Campaign A')).not.toBeInTheDocument()

    pendingA.resolve(wrapPaged([RESULT_A2]))
    await waitFor(() => expect(mediaFolderApi.search).toHaveBeenCalled())
    expect(screen.queryByText('Archive / Campaign A')).not.toBeInTheDocument()
  })

  it('tracks recently opened folders as quick-access chips, scoped per Page', async () => {
    mediaFolderApi.search.mockResolvedValue(wrapPaged([RESULT_A2]))
    const { user, onOpenFolder } = renderBox()

    await user.type(screen.getByLabelText('Tìm thư mục trong Page'), 'Campaign')
    await user.click(await screen.findByText('Archive / Campaign A'))

    expect(screen.getByRole('button', { name: '📁 Campaign A' })).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: '📁 Campaign A' }))
    expect(onOpenFolder).toHaveBeenLastCalledWith(RESULT_A2.id)
  })
})
