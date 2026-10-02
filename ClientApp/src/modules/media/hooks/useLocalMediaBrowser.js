import { useCallback, useEffect, useState } from 'react'
import { BROWSER_PAGE_SIZE } from './useMediaBrowser'
import {
  useMediaFolderBreadcrumb,
  useMediaFolderChildren,
  useMediaFolderPageRoots,
} from './useMediaFolders'

const GUID_PATTERN = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i
const isGuid = (value) => typeof value === 'string' && GUID_PATTERN.test(value)
const isDeniedOrMissing = (error) => [400, 403, 404].includes(error?.response?.status)

/**
 * Cùng children/breadcrumb/page-roots với useMediaBrowser, nhưng folder đang mở nằm trong
 * useState. Không đọc hay ghi URL — dùng cho popup chọn ảnh trên trang tạo bài.
 */
export function useLocalMediaBrowser({ pageSize = BROWSER_PAGE_SIZE, enabled = true } = {}) {
  const [currentFolder, setCurrentFolder] = useState(null)
  const [pageIndex, setPageIndex] = useState(1)
  const currentFolderId = currentFolder?.id ?? null
  const derivedPageId = currentFolder?.socialChannelId || null

  const pageRootsQuery = useMediaFolderPageRoots({
    index: pageIndex,
    size: pageSize,
    enabled: enabled && currentFolderId === null,
  })
  const childrenQuery = useMediaFolderChildren({
    socialChannelId: derivedPageId,
    parentFolderId: currentFolderId,
    index: pageIndex,
    size: pageSize,
    enabled: enabled && currentFolderId !== null,
  })
  const breadcrumbQuery = useMediaFolderBreadcrumb({
    socialChannelId: derivedPageId,
    folderId: currentFolderId,
    enabled: enabled && currentFolderId !== null,
  })

  const folderDenied = currentFolderId !== null
    && (isDeniedOrMissing(childrenQuery.error) || isDeniedOrMissing(breadcrumbQuery.error))
  useEffect(() => {
    if (folderDenied) {
      setCurrentFolder(null)
      setPageIndex(1)
    }
  }, [folderDenied])

  const openRoot = useCallback(() => {
    setCurrentFolder(null)
    setPageIndex(1)
  }, [])

  const openFolder = useCallback((folder) => {
    if (!folder?.id || !isGuid(folder.id)) {
      openRoot()
      return
    }
    setCurrentFolder({
      id: folder.id,
      socialChannelId: folder.socialChannelId || null,
    })
    setPageIndex(1)
  }, [openRoot])

  const items = currentFolderId === null
    ? pageRootsQuery.data?.items ?? []
    : childrenQuery.data?.items ?? []
  const isLoading = currentFolderId === null ? pageRootsQuery.isLoading : childrenQuery.isLoading
  const isError = currentFolderId === null ? pageRootsQuery.isError : childrenQuery.isError
  const error = currentFolderId === null ? pageRootsQuery.error : childrenQuery.error

  return {
    currentFolderId,
    derivedPageId,
    pageIndex,
    items,
    ancestors: breadcrumbQuery.data?.ancestors ?? [],
    isLoading,
    isError,
    error,
    refetch: currentFolderId === null ? pageRootsQuery.refetch : childrenQuery.refetch,
    openFolder,
    openRoot,
    openBreadcrumb: (ancestor) => openFolder({
      id: ancestor.id,
      socialChannelId: derivedPageId,
    }),
  }
}
