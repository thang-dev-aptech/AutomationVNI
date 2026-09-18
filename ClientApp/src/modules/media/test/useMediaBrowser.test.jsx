import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { act, renderHook, waitFor } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { useMediaBrowser, BROWSER_PAGE_SIZE } from '../hooks/useMediaBrowser'
import { mediaFolderApi, mediaFolderQueryKeys } from '../services/mediaFolderApi'

vi.mock('../services/mediaFolderApi', async (importOriginal) => {
  const actual = await importOriginal()
  return {
    ...actual,
    mediaFolderApi: {
      ...actual.mediaFolderApi,
      pageRoots: vi.fn(),
      children: vi.fn(),
      breadcrumb: vi.fn(),
    },
  }
})

const wrapPaged = (items, { index = 1, size = 20, total = items.length } = {}) => ({
  data: {
    items,
    index,
    size,
    total,
    totalPages: Math.ceil(total / size),
  },
})

const wrapBreadcrumb = (ancestors = []) => ({
  data: {
    ancestors,
  },
})

const deferred = () => {
  let resolve, reject
  const promise = new Promise((res, rej) => {
    resolve = res
    reject = rej
  })
  return { promise, resolve, reject }
}

const PAGE_1 = 'page-1'
const PAGE_2 = 'page-2'
const FOLDER_ROOT_1 = { id: 'root-1', name: 'Root 1', socialChannelId: PAGE_1, parentFolderId: null }
const FOLDER_ROOT_2 = { id: 'root-2', name: 'Root 2', socialChannelId: PAGE_2, parentFolderId: null }
const FOLDER_CHILD_1 = { id: 'child-1', name: 'Child 1', socialChannelId: PAGE_1, parentFolderId: 'root-1' }

function wrapper() {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false, refetchOnWindowFocus: false } },
  })
  return function Wrapper({ children }) {
    return <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>
  }
}

describe('useMediaBrowser', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    mediaFolderApi.pageRoots.mockResolvedValue(wrapPaged([]))
    mediaFolderApi.breadcrumb.mockResolvedValue(wrapBreadcrumb([]))
  })

  describe('top-level (currentFolderId === null)', () => {
    it('loads page-roots endpoint at top level', async () => {
      mediaFolderApi.pageRoots.mockResolvedValue(wrapPaged([FOLDER_ROOT_1, FOLDER_ROOT_2]))
      mediaFolderApi.children.mockResolvedValue(wrapPaged([]))

      const { result } = renderHook(() => useMediaBrowser(), { wrapper: wrapper() })

      await waitFor(() => {
        expect(result.current.currentFolderId).toBeNull()
        expect(result.current.derivedPageId).toBeNull()
        expect(result.current.items.length).toBe(2)
      })

      expect(mediaFolderApi.pageRoots).toHaveBeenCalled()
    })
  })

  describe('inside folder (currentFolderId !== null)', () => {
    it('switches to Page-scoped queries when opening a folder', async () => {
      mediaFolderApi.pageRoots.mockResolvedValue(wrapPaged([FOLDER_ROOT_1]))
      mediaFolderApi.breadcrumb.mockResolvedValue(wrapBreadcrumb([FOLDER_ROOT_1]))
      mediaFolderApi.children.mockResolvedValue(wrapPaged([FOLDER_CHILD_1]))

      const { result } = renderHook(() => useMediaBrowser(), { wrapper: wrapper() })

      act(() => {
        result.current.openFolder(FOLDER_ROOT_1)
      })

      await waitFor(() => {
        expect(result.current.currentFolderId).toBe(FOLDER_ROOT_1.id)
        expect(result.current.derivedPageId).toBe(PAGE_1)
      })

      expect(mediaFolderApi.children).toHaveBeenCalledWith(
        expect.objectContaining({
          socialChannelId: PAGE_1,
          parentFolderId: FOLDER_ROOT_1.id,
        })
      )
      expect(mediaFolderApi.breadcrumb).toHaveBeenCalledWith(
        expect.objectContaining({
          socialChannelId: PAGE_1,
          folderId: FOLDER_ROOT_1.id,
        })
      )
    })

    it('derives page ID from folder object', async () => {
      mediaFolderApi.pageRoots.mockResolvedValue(wrapPaged([]))
      mediaFolderApi.breadcrumb.mockResolvedValue(wrapBreadcrumb([FOLDER_CHILD_1]))
      mediaFolderApi.children.mockResolvedValue(wrapPaged([]))

      const { result } = renderHook(() => useMediaBrowser(), { wrapper: wrapper() })

      act(() => {
        result.current.openFolder(FOLDER_CHILD_1)
      })

      await waitFor(() => {
        expect(result.current.derivedPageId).toBe(FOLDER_CHILD_1.socialChannelId)
      })
    })
  })

  describe('cache separation — late responses cannot overwrite current folder', () => {
    it('uses distinct query keys for different folders', async () => {
      const pendingRoot1 = deferred()
      mediaFolderApi.pageRoots.mockResolvedValue(
        wrapPaged([FOLDER_ROOT_1, FOLDER_ROOT_2])
      )
      mediaFolderApi.breadcrumb.mockImplementation(({ folderId }) => {
        if (folderId === FOLDER_ROOT_1.id) return Promise.resolve(wrapBreadcrumb([FOLDER_ROOT_1]))
        if (folderId === FOLDER_ROOT_2.id) return Promise.resolve(wrapBreadcrumb([FOLDER_ROOT_2]))
        return Promise.resolve(wrapBreadcrumb([]))
      })
      mediaFolderApi.children.mockImplementation(({ socialChannelId }) => {
        if (socialChannelId === PAGE_1) return pendingRoot1.promise
        return Promise.resolve(wrapPaged([]))
      })

      const { result } = renderHook(() => useMediaBrowser(), { wrapper: wrapper() })

      // Open folder from page 1
      act(() => {
        result.current.openFolder(FOLDER_ROOT_1)
      })

      await waitFor(() => {
        expect(result.current.currentFolderId).toBe(FOLDER_ROOT_1.id)
        expect(result.current.derivedPageId).toBe(PAGE_1)
      })

      // Switch to folder from page 2 before page 1's response arrives
      act(() => {
        result.current.openFolder(FOLDER_ROOT_2)
      })

      await waitFor(() => {
        expect(result.current.currentFolderId).toBe(FOLDER_ROOT_2.id)
        expect(result.current.derivedPageId).toBe(PAGE_2)
      })

      // Now resolve the pending response for page 1
      pendingRoot1.resolve(wrapPaged([]))

      // Verify that page 1's late response does not overwrite page 2's current view
      await waitFor(() => {
        expect(result.current.currentFolderId).toBe(FOLDER_ROOT_2.id)
        expect(result.current.derivedPageId).toBe(PAGE_2)
        expect(result.current.items).toEqual([])
      })

      // The cache keys are distinct for each folder:
      // page 1: [media-folders, children, page-1, root-1, ...]
      // page 2: [media-folders, children, page-2, root-2, ...]
      // So late responses for page 1 land in a different cache entry
    })
  })

  describe('navigation', () => {
    it('resets state when opening root', async () => {
      mediaFolderApi.pageRoots.mockResolvedValue(wrapPaged([FOLDER_ROOT_1]))
      mediaFolderApi.children.mockResolvedValue(wrapPaged([]))
      mediaFolderApi.breadcrumb.mockResolvedValue(wrapBreadcrumb([FOLDER_ROOT_1]))

      const { result } = renderHook(() => useMediaBrowser(), { wrapper: wrapper() })

      act(() => {
        result.current.openFolder(FOLDER_ROOT_1)
      })

      await waitFor(() => {
        expect(result.current.currentFolderId).toBe(FOLDER_ROOT_1.id)
        expect(result.current.selection).toBe(FOLDER_ROOT_1.id)
      })

      act(() => {
        result.current.openRoot()
      })

      expect(result.current.currentFolderId).toBeNull()
      expect(result.current.derivedPageId).toBeNull()
      expect(result.current.selection).toBe('all')
    })

    it('resets pageIndex when opening folder', async () => {
      mediaFolderApi.pageRoots.mockResolvedValue(wrapPaged([FOLDER_ROOT_1]))
      mediaFolderApi.children.mockResolvedValue(wrapPaged([]))
      mediaFolderApi.breadcrumb.mockResolvedValue(wrapBreadcrumb([FOLDER_ROOT_1]))

      const { result } = renderHook(() => useMediaBrowser(), { wrapper: wrapper() })

      act(() => {
        result.current.goToPage(3)
      })

      expect(result.current.pageIndex).toBe(3)

      act(() => {
        result.current.openFolder(FOLDER_ROOT_1)
      })

      await waitFor(() => {
        expect(result.current.pageIndex).toBe(1)
      })
    })
  })

  describe('selection', () => {
    it('initializes with all selection', () => {
      mediaFolderApi.pageRoots.mockResolvedValue(wrapPaged([]))

      const { result } = renderHook(() => useMediaBrowser(), { wrapper: wrapper() })

      expect(result.current.selection).toBe('all')
    })

    it('updates selection when opening folder', async () => {
      mediaFolderApi.pageRoots.mockResolvedValue(wrapPaged([FOLDER_ROOT_1]))
      mediaFolderApi.children.mockResolvedValue(wrapPaged([]))
      mediaFolderApi.breadcrumb.mockResolvedValue(wrapBreadcrumb([FOLDER_ROOT_1]))

      const { result } = renderHook(() => useMediaBrowser(), { wrapper: wrapper() })

      act(() => {
        result.current.openFolder(FOLDER_ROOT_1)
      })

      await waitFor(() => {
        expect(result.current.selection).toBe(FOLDER_ROOT_1.id)
      })
    })

    it('allows selectAll and selectUnassigned', () => {
      mediaFolderApi.pageRoots.mockResolvedValue(wrapPaged([]))

      const { result } = renderHook(() => useMediaBrowser(), { wrapper: wrapper() })

      act(() => {
        result.current.selectUnassigned()
      })

      expect(result.current.selection).toBe('unassigned')

      act(() => {
        result.current.selectAll()
      })

      expect(result.current.selection).toBe('all')
    })
  })

  describe('expanded folders', () => {
    it('toggles folder expansion', () => {
      mediaFolderApi.pageRoots.mockResolvedValue(wrapPaged([]))

      const { result } = renderHook(() => useMediaBrowser(), { wrapper: wrapper() })

      expect(result.current.expandedFolderIds.has('folder-1')).toBe(false)

      act(() => {
        result.current.toggleFolderExpanded('folder-1')
      })

      expect(result.current.expandedFolderIds.has('folder-1')).toBe(true)

      act(() => {
        result.current.toggleFolderExpanded('folder-1')
      })

      expect(result.current.expandedFolderIds.has('folder-1')).toBe(false)
    })
  })
})
