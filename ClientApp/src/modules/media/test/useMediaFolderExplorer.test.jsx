import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { act, renderHook, waitFor } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { useMediaFolderExplorer } from '../hooks/useMediaFolderExplorer'
import { mediaFolderApi, mediaFolderQueryKeys } from '../services/mediaFolderApi'
import {
  FOLDER_A_ROOT,
  FOLDER_B_ROOT,
  PAGE_A,
  PAGE_B,
  deferred,
  wrapBreadcrumb,
  wrapPaged,
} from './mediaFolderExplorerFixtures'

vi.mock('../services/mediaFolderApi', async (importOriginal) => {
  const actual = await importOriginal()
  return {
    ...actual,
    mediaFolderApi: {
      ...actual.mediaFolderApi,
      tree: vi.fn(),
      children: vi.fn(),
      breadcrumb: vi.fn(),
    },
  }
})

function wrapper() {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false, refetchOnWindowFocus: false } },
  })
  return function Wrapper({ children }) {
    return <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>
  }
}

describe('MEDIA-04 query keys and Page reset', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    mediaFolderApi.tree.mockResolvedValue(wrapPaged([]))
    mediaFolderApi.breadcrumb.mockResolvedValue(wrapBreadcrumb([]))
  })

  it('scopes children and breadcrumb keys by Page and parent', () => {
    expect(mediaFolderQueryKeys.children(PAGE_A, null, 1, 20)).toEqual([
      'media-folders', 'children', PAGE_A, 'root', 1, 20,
    ])
    expect(mediaFolderQueryKeys.children(PAGE_B, FOLDER_B_ROOT.id, 2, 20)).toEqual([
      'media-folders', 'children', PAGE_B, FOLDER_B_ROOT.id, 2, 20,
    ])
    expect(mediaFolderQueryKeys.breadcrumb(PAGE_A, FOLDER_A_ROOT.id)).toEqual([
      'media-folders', 'breadcrumb', PAGE_A, FOLDER_A_ROOT.id,
    ])
    expect(mediaFolderQueryKeys.children(PAGE_A, null, 1, 20))
      .not.toEqual(mediaFolderQueryKeys.children(PAGE_B, null, 1, 20))
  })

  it('MEDIA-04-AC2: late Page A payload does not replace Page B explorer state', async () => {
    const pendingA = deferred()
    mediaFolderApi.children.mockImplementation(({ socialChannelId }) => {
      if (socialChannelId === PAGE_A) return pendingA.promise
      return Promise.resolve(wrapPaged([FOLDER_B_ROOT]))
    })

    const { result, rerender } = renderHook(
      ({ socialChannelId }) => useMediaFolderExplorer({ socialChannelId }),
      { wrapper: wrapper(), initialProps: { socialChannelId: PAGE_A } },
    )

    expect(result.current.currentFolderId).toBeNull()
    act(() => {
      result.current.openFolder(FOLDER_A_ROOT.id)
    })
    act(() => {
      result.current.goToPage(2)
    })
    act(() => {
      result.current.selectUnassigned()
    })
    await waitFor(() => {
      expect(result.current.selection).toBe('unassigned')
      expect(result.current.pageIndex).toBe(2)
      expect(result.current.currentFolderId).toBe(FOLDER_A_ROOT.id)
    })

    rerender({ socialChannelId: PAGE_B })

    await waitFor(() => {
      expect(result.current.socialChannelId).toBe(PAGE_B)
      expect(result.current.currentFolderId).toBeNull()
      expect(result.current.selection).toBe('all')
      expect(result.current.pageIndex).toBe(1)
      expect(result.current.items.map((item) => item.id)).toEqual([FOLDER_B_ROOT.id])
    })

    pendingA.resolve(wrapPaged([FOLDER_A_ROOT], { index: 2, total: 40 }))
    await waitFor(() => {
      expect(result.current.items.map((item) => item.id)).toEqual([FOLDER_B_ROOT.id])
    })
    expect(result.current.currentFolderId).toBeNull()
    expect(result.current.selection).toBe('all')
    expect(result.current.pageIndex).toBe(1)
    expect(mediaFolderApi.tree).not.toHaveBeenCalled()
  })
})
