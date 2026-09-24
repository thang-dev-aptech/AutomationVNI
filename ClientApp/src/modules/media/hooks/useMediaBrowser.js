import { useEffect, useMemo, useState } from 'react'
import {
  useMediaFolderPageRoots,
  useMediaFolderChildren,
  useMediaFolderBreadcrumb,
} from './useMediaFolders'

export const BROWSER_PAGE_SIZE = 20
const DEFAULT_SELECTION = 'all'

/**
 * Cross-Page folder browser: currentFolderId is primary state (null = cross-Page top level),
 * and Page is DERIVED from whichever folder you're inside via the folder's socialChannelId.
 *
 * Top level (currentFolderId === null):
 *   - Loads page-roots endpoint showing root folder for each writable Page
 *
 * Inside a folder (currentFolderId !== null):
 *   - Fetches breadcrumb & children for that folder using its derived Page ID
 *   - Page ID comes from the folder object's socialChannelId, not as a prop
 *
 * Query keys include Page ID and parent folder ID per convention, providing natural
 * cache separation. This eliminates the need for manual cache purges — distinct keys
 * prevent stale cross-folder responses from overwriting the current view.
 */
export function useMediaBrowser({ pageSize = BROWSER_PAGE_SIZE } = {}) {
  // Primary state: current folder object (includes id and socialChannelId).
  // Both currentFolderId and derivedPageId are derived from this.
  const [currentFolder, setCurrentFolder] = useState(null)
  const [selection, setSelection] = useState(DEFAULT_SELECTION)
  const [pageIndex, setPageIndex] = useState(1)
  const [expandedFolderIds, setExpandedFolderIds] = useState(new Set())

  const currentFolderId = currentFolder?.id ?? null
  const derivedPageId = currentFolder?.socialChannelId ?? null

  // Top-level (cross-Page): load roots when currentFolderId === null
  const pageRootsQuery = useMediaFolderPageRoots({
    index: pageIndex,
    size: pageSize,
    enabled: currentFolderId === null,
  })

  // Inside folder (Page-scoped): load children & breadcrumb with derived Page ID
  const childrenQuery = useMediaFolderChildren({
    socialChannelId: derivedPageId,
    parentFolderId: currentFolderId,
    index: pageIndex,
    size: pageSize,
    enabled: currentFolderId !== null,
  })

  const breadcrumbQuery = useMediaFolderBreadcrumb({
    socialChannelId: derivedPageId,
    folderId: currentFolderId,
    enabled: currentFolderId !== null,
  })

  // Auto-expand ancestors when breadcrumb loads
  const ancestors = breadcrumbQuery.data?.ancestors ?? []
  const ancestorIdsKey = ancestors.map((a) => a.id).join(',')

  useEffect(() => {
    if (!ancestorIdsKey) return
    setExpandedFolderIds((prev) => {
      let changed = false
      const next = new Set(prev)
      ancestors.forEach((item) => {
        if (!next.has(item.id)) {
          next.add(item.id)
          changed = true
        }
      })
      return changed ? next : prev
    })
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [ancestorIdsKey])

  // Compute display data based on current level
  const items = currentFolderId === null
    ? pageRootsQuery.data?.items ?? []
    : childrenQuery.data?.items ?? []
  const total = currentFolderId === null
    ? pageRootsQuery.data?.total ?? 0
    : childrenQuery.data?.total ?? 0
  const size = currentFolderId === null
    ? pageRootsQuery.data?.size ?? pageSize
    : childrenQuery.data?.size ?? pageSize
  const totalPages = Math.max(1, Math.ceil((total || 0) / (size || pageSize)))

  // Folder options for pickers: ancestors + current level folders
  const folderOptions = useMemo(() => {
    const map = new Map()
    const ancestorItems = breadcrumbQuery.data?.ancestors ?? []
    const childItems = currentFolderId === null
      ? pageRootsQuery.data?.items ?? []
      : childrenQuery.data?.items ?? []
    ancestorItems.forEach((item) => {
      map.set(item.id, { id: item.id, name: item.name, parentFolderId: null })
    })
    childItems.forEach((item) => map.set(item.id, item))
    return [...map.values()]
  }, [breadcrumbQuery.data, pageRootsQuery.data, childrenQuery.data, currentFolderId])

  const openRoot = () => {
    setCurrentFolder(null)
    setSelection(DEFAULT_SELECTION)
    setPageIndex(1)
  }

  const openFolder = (folder) => {
    if (!folder) {
      openRoot()
      return
    }
    setCurrentFolder(folder)
    setSelection(folder.id)
    setPageIndex(1)
  }

  const isLoading = currentFolderId === null
    ? pageRootsQuery.isLoading
    : childrenQuery.isLoading

  const isError = currentFolderId === null
    ? pageRootsQuery.isError
    : childrenQuery.isError

  const error = currentFolderId === null
    ? pageRootsQuery.error
    : childrenQuery.error

  const isFetching = currentFolderId === null
    ? pageRootsQuery.isFetching
    : childrenQuery.isFetching

  const refetch = currentFolderId === null
    ? pageRootsQuery.refetch
    : childrenQuery.refetch

  return {
    currentFolderId,
    derivedPageId,
    selection,
    pageIndex,
    pageSize,
    items,
    total,
    totalPages,
    ancestors,
    folderOptions,
    isLoading,
    isError,
    error,
    isFetching,
    refetch,
    openFolder,
    openRoot,
    // Ancestor items từ breadcrumb chỉ có {id, name} (MediaFolderBreadcrumbItem), không có
    // socialChannelId — nhưng breadcrumb luôn nằm trong CÙNG 1 Page với folder đang mở, nên
    // dùng lại derivedPageId hiện tại thay vì đọc từ chính ancestor item.
    openBreadcrumb: (ancestor) => openFolder({ id: ancestor.id, socialChannelId: derivedPageId }),
    selectAll: () => setSelection('all'),
    selectUnassigned: () => setSelection('unassigned'),
    goToPage: (nextIndex) => setPageIndex(nextIndex),
    resetToRoot: openRoot,
    expandedFolderIds,
    toggleFolderExpanded: (folderId) => {
      setExpandedFolderIds((prev) => {
        const next = new Set(prev)
        if (next.has(folderId)) {
          next.delete(folderId)
        } else {
          next.add(folderId)
        }
        return next
      })
    },
  }
}
