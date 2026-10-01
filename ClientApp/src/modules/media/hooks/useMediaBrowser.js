import { useCallback, useEffect, useMemo, useState } from 'react'
import { useSearchParams } from 'react-router-dom'
import {
  useMediaFolderPageRoots,
  useMediaFolderChildren,
  useMediaFolderBreadcrumb,
} from './useMediaFolders'

export const BROWSER_PAGE_SIZE = 20
const DEFAULT_SELECTION = 'all'
const FOLDER_PARAM = 'folder'
const PAGE_PARAM = 'page'
const GUID_PATTERN = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i

const isGuid = (value) => typeof value === 'string' && GUID_PATTERN.test(value)
// 400: folder page-less (Drive) đã xoá/không tồn tại — backend coi nó như folder của Page rồi
// từ chối vì thiếu SocialChannelId (ArgumentException → 400). Cùng nghĩa với 403/404: về root.
const isDeniedOrMissing = (error) => [400, 403, 404].includes(error?.response?.status)

/**
 * Cross-Page folder browser: the open folder lives in the URL (?folder=<id>&page=<socialChannelId>,
 * null = cross-Page top level), and Page is DERIVED from the `page` param.
 * The URL is untrusted: a non-GUID folder/page falls back to root without any API call, and a
 * 403/404 from children/breadcrumb replaces the URL with root. Data only comes from the existing
 * permission-checked children/breadcrumb endpoints.
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
  const [searchParams, setSearchParams] = useSearchParams()
  const rawFolder = searchParams.get(FOLDER_PARAM)
  const rawPage = searchParams.get(PAGE_PARAM)
  const hasFolderParams = rawFolder !== null || rawPage !== null
  const urlIsValid = isGuid(rawFolder) && (rawPage === null || rawPage === '' || isGuid(rawPage))

  const currentFolder = urlIsValid ? { id: rawFolder, socialChannelId: rawPage || null } : null
  const currentFolderId = currentFolder?.id ?? null
  const derivedPageId = currentFolder?.socialChannelId ?? null

  // Selection / file page index are scoped to the open folder, so Back/Forward resets them
  // exactly like openFolder did when they were plain state.
  const [selectionState, setSelectionState] = useState({ folderId: null, value: DEFAULT_SELECTION })
  const [pageState, setPageState] = useState({ folderId: null, index: 1 })
  const selection = selectionState.folderId === currentFolderId
    ? selectionState.value
    : currentFolderId ?? DEFAULT_SELECTION
  const pageIndex = pageState.folderId === currentFolderId ? pageState.index : 1
  const [expandedFolderIds, setExpandedFolderIds] = useState(new Set())

  const clearFolderParams = useCallback((options) => {
    setSearchParams((prev) => {
      const next = new URLSearchParams(prev)
      next.delete(FOLDER_PARAM)
      next.delete(PAGE_PARAM)
      return next
    }, options)
  }, [setSearchParams])

  // Malformed folder/page in the URL: back to root, no API call was ever made.
  useEffect(() => {
    if (hasFolderParams && !urlIsValid) clearFolderParams({ replace: true })
  }, [hasFolderParams, urlIsValid, clearFolderParams])

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

  // Folder deleted / no permission / other Page: fall back to root instead of showing an error.
  const folderDenied = currentFolderId !== null
    && (isDeniedOrMissing(childrenQuery.error) || isDeniedOrMissing(breadcrumbQuery.error))
  useEffect(() => {
    if (folderDenied) clearFolderParams({ replace: true })
  }, [folderDenied, clearFolderParams])

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
    clearFolderParams()
  }

  const openFolder = (folder) => {
    if (!folder) {
      openRoot()
      return
    }
    setSearchParams((prev) => {
      const next = new URLSearchParams(prev)
      next.set(FOLDER_PARAM, folder.id)
      if (folder.socialChannelId) next.set(PAGE_PARAM, folder.socialChannelId)
      else next.delete(PAGE_PARAM)
      return next
    })
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
    selectAll: () => setSelectionState({ folderId: currentFolderId, value: 'all' }),
    selectUnassigned: () => setSelectionState({ folderId: currentFolderId, value: 'unassigned' }),
    goToPage: (nextIndex) => setPageState({ folderId: currentFolderId, index: nextIndex }),
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
