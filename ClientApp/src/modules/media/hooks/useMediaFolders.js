import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { unwrapApiData } from '@/shared/utils/apiHelpers'
import { mediaFolderApi, mediaFolderQueryKeys } from '../services/mediaFolderApi'
import { mediaAssetQueryKeys } from '../services/mediaAssetApi'

export function useMediaFolderTree() {
  return useQuery({
    queryKey: mediaFolderQueryKeys.tree,
    queryFn: async () => unwrapApiData(await mediaFolderApi.tree()),
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
