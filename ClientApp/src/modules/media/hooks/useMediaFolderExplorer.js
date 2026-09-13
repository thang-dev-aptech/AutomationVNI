import { useEffect, useMemo, useState } from 'react'
import { useQueryClient } from '@tanstack/react-query'
import { useMediaFolderBreadcrumb, useMediaFolderChildren } from './useMediaFolders'
import { isExplorerCacheKeyForOtherPage } from '../services/mediaFolderApi'

export const EXPLORER_PAGE_SIZE = 20
const DEFAULT_SELECTION = 'all'

export function useMediaFolderExplorer({ socialChannelId, pageSize = EXPLORER_PAGE_SIZE } = {}) {
  const queryClient = useQueryClient()
  const scopedPageId = socialChannelId || null

  const [boundPageId, setBoundPageId] = useState(scopedPageId)
  const [currentFolderId, setCurrentFolderId] = useState(null)
  const [selection, setSelection] = useState(DEFAULT_SELECTION)
  const [pageIndex, setPageIndex] = useState(1)

  const pageChanged = scopedPageId !== boundPageId
  if (pageChanged) {
    setBoundPageId(scopedPageId)
    setCurrentFolderId(null)
    setSelection(DEFAULT_SELECTION)
    setPageIndex(1)
  }

  const activeFolderId = pageChanged ? null : currentFolderId
  const activeSelection = pageChanged ? DEFAULT_SELECTION : selection
  const activeIndex = pageChanged ? 1 : pageIndex

  useEffect(() => {
    queryClient.cancelQueries({
      predicate: (query) => isExplorerCacheKeyForOtherPage(query.queryKey, scopedPageId),
    })
    queryClient.removeQueries({
      predicate: (query) => isExplorerCacheKeyForOtherPage(query.queryKey, scopedPageId),
    })
  }, [scopedPageId, queryClient])

  const childrenQuery = useMediaFolderChildren({
    socialChannelId: scopedPageId,
    parentFolderId: activeFolderId,
    index: activeIndex,
    size: pageSize,
  })

  const breadcrumbQuery = useMediaFolderBreadcrumb({
    socialChannelId: scopedPageId,
    folderId: activeFolderId,
  })

  const items = childrenQuery.data?.items ?? []
  const total = childrenQuery.data?.total ?? 0
  const size = childrenQuery.data?.size ?? pageSize
  const totalPages = Math.max(1, Math.ceil((total || 0) / (size || pageSize)))
  const ancestors = breadcrumbQuery.data?.ancestors ?? []

  const folderOptions = useMemo(() => {
    const map = new Map()
    const ancestorItems = breadcrumbQuery.data?.ancestors ?? []
    const childItems = childrenQuery.data?.items ?? []
    ancestorItems.forEach((item) => {
      map.set(item.id, { id: item.id, name: item.name, parentFolderId: null })
    })
    childItems.forEach((item) => map.set(item.id, item))
    return [...map.values()]
  }, [breadcrumbQuery.data, childrenQuery.data])

  const openRoot = () => {
    setCurrentFolderId(null)
    setSelection(DEFAULT_SELECTION)
    setPageIndex(1)
  }

  const openFolder = (folderId) => {
    if (!folderId) {
      openRoot()
      return
    }
    setCurrentFolderId(folderId)
    setSelection(folderId)
    setPageIndex(1)
  }

  return {
    socialChannelId: scopedPageId,
    currentFolderId: activeFolderId,
    selection: activeSelection,
    pageIndex: activeIndex,
    pageSize,
    items,
    total,
    totalPages,
    ancestors,
    folderOptions,
    isLoading: Boolean(scopedPageId) && childrenQuery.isLoading,
    isError: childrenQuery.isError,
    error: childrenQuery.error,
    isFetching: childrenQuery.isFetching,
    refetch: childrenQuery.refetch,
    openFolder,
    openRoot,
    openBreadcrumb: (folderId) => openFolder(folderId),
    selectAll: () => setSelection('all'),
    selectUnassigned: () => setSelection('unassigned'),
    goToPage: (nextIndex) => setPageIndex(nextIndex),
    resetToRoot: openRoot,
  }
}
