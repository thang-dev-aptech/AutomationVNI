import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { unwrapApiData } from '@/shared/utils/apiHelpers'
import { mediaAssetApi, mediaAssetQueryKeys } from '../services/mediaAssetApi'

export function useMediaAssets(params = { index: 1, size: 48 }) {
  return useQuery({
    queryKey: mediaAssetQueryKeys.list(params),
    queryFn: async () => unwrapApiData(await mediaAssetApi.filter(params)),
  })
}

export function useMediaAssetAll() {
  return useQuery({
    queryKey: mediaAssetQueryKeys.all,
    queryFn: async () => unwrapApiData(await mediaAssetApi.getAll()),
  })
}

export function useMediaAssetDetail(id) {
  return useQuery({
    queryKey: mediaAssetQueryKeys.detail(id),
    queryFn: async () => unwrapApiData(await mediaAssetApi.getById(id)),
    enabled: Boolean(id),
  })
}

export function useCreateMediaAsset() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async (payload) => unwrapApiData(await mediaAssetApi.create(payload)),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: mediaAssetQueryKeys.all })
    },
  })
}

export function useUpdateMediaAsset() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async ({ id, payload }) =>
      unwrapApiData(await mediaAssetApi.update(id, payload)),
    onSuccess: (_, variables) => {
      queryClient.invalidateQueries({ queryKey: mediaAssetQueryKeys.all })
      queryClient.invalidateQueries({ queryKey: mediaAssetQueryKeys.detail(variables.id) })
    },
  })
}

export function useDeleteMediaAsset() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (id) => mediaAssetApi.softDelete(id),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: mediaAssetQueryKeys.all })
    },
  })
}
