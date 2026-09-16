import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { unwrapApiData } from '@/shared/utils/apiHelpers'
import { mediaFolderApi, mediaFolderQueryKeys } from '../services/mediaFolderApi'
import { mediaAssetQueryKeys } from '../services/mediaAssetApi'

/** Kept for PageContext logo picker (flat filter, not a hierarchy pick — out of MEDIA-07 scope). */
export function useMediaFolderTree() {
  return useQuery({
    queryKey: mediaFolderQueryKeys.tree,
    queryFn: async () => unwrapApiData(await mediaFolderApi.tree()),
  })
}

export function useMediaFolderChildren({
  socialChannelId,
  parentFolderId = null,
  index = 1,
  size = 20,
  sortBy = 'sortOrder',
  sortDirection = 'asc',
  enabled = true,
} = {}) {
  return useQuery({
    queryKey: mediaFolderQueryKeys.children(socialChannelId, parentFolderId, index, size),
    queryFn: async () =>
      unwrapApiData(
        await mediaFolderApi.children({
          socialChannelId,
          parentFolderId,
          index,
          size,
          sortBy,
          sortDirection,
        }),
      ),
    enabled: enabled && Boolean(socialChannelId),
    retry: false,
  })
}

export function useMediaFolderBreadcrumb({ socialChannelId, folderId } = {}) {
  return useQuery({
    queryKey: mediaFolderQueryKeys.breadcrumb(socialChannelId, folderId),
    queryFn: async () =>
      unwrapApiData(await mediaFolderApi.breadcrumb({ socialChannelId, folderId })),
    enabled: Boolean(socialChannelId) && Boolean(folderId),
    retry: false,
  })
}

export function useMediaFolderSearch({
  socialChannelId,
  keyword,
  index = 1,
  size = 20,
  enabled = true,
} = {}) {
  const trimmed = keyword?.trim() ?? ''
  return useQuery({
    queryKey: mediaFolderQueryKeys.search(socialChannelId, trimmed, index, size),
    queryFn: async () =>
      unwrapApiData(await mediaFolderApi.search({ socialChannelId, keyword: trimmed, index, size })),
    enabled: enabled && Boolean(socialChannelId) && Boolean(trimmed),
    retry: false,
  })
}

/**
 * MEDIA-03: tạo hierarchy folder (clientRef/parentRef) trong MỘT Page, nguyên tử cả batch.
 * Preview (validateOnly) và submit dùng chung mutation; chỉ invalidate cache khi submit
 * thật thành công. Không có UI nào gọi hook này hiện tại — MEDIA-06 (nút "Tạo hàng loạt")
 * dùng useCreateMediaFolderAcrossPages bên dưới thay vì hierarchy-trong-1-Page.
 */
export function useBulkCreateMediaFolder() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async (payload) => unwrapApiData(await mediaFolderApi.bulkCreate(payload)),
    onSuccess: (_data, variables) => {
      if (variables.validateOnly) return
      queryClient.invalidateQueries({ queryKey: mediaFolderQueryKeys.all })
    },
  })
}

/**
 * MEDIA-06: tạo 1 folder gốc cùng tên ở nhiều Page cùng lúc (best-effort — Page lỗi không
 * chặn Page khác). Luôn invalidate cache vì ngay cả khi có Page lỗi, các Page thành công
 * đã ghi dữ liệu thật.
 */
export function useCreateMediaFolderAcrossPages() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async (payload) => unwrapApiData(await mediaFolderApi.createAcrossPages(payload)),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: mediaFolderQueryKeys.all }),
  })
}

export function useCreateMediaFolder() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async (payload) => unwrapApiData(await mediaFolderApi.create(payload)),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: mediaFolderQueryKeys.all }),
  })
}

export function useUpdateMediaFolder() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async ({ id, payload }) => unwrapApiData(await mediaFolderApi.update(id, payload)),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: mediaFolderQueryKeys.all }),
  })
}

export function useDeleteMediaFolder() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (id) => mediaFolderApi.softDelete(id),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: mediaFolderQueryKeys.all })
      // Ảnh trong folder bị xóa được đưa về "Chưa phân loại" ở backend → refresh grid.
      queryClient.invalidateQueries({ queryKey: mediaAssetQueryKeys.all })
    },
  })
}
