import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { unwrapApiData } from '@/shared/utils/apiHelpers'
import { socialChannelApi, socialChannelQueryKeys } from '../services/socialChannelApi'

export function useSocialChannels(params = { index: 1, size: 50 }) {
  return useQuery({
    queryKey: socialChannelQueryKeys.list(params),
    queryFn: async () => unwrapApiData(await socialChannelApi.filter(params)),
  })
}

export function useSocialChannelAll() {
  return useQuery({
    queryKey: socialChannelQueryKeys.all,
    queryFn: async () => unwrapApiData(await socialChannelApi.getAll()),
  })
}

export function useCreateSocialChannel() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async (payload) => unwrapApiData(await socialChannelApi.create(payload)),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: socialChannelQueryKeys.all })
    },
  })
}

export function useUpdateSocialChannel() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async ({ id, payload }) =>
      unwrapApiData(await socialChannelApi.update(id, payload)),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: socialChannelQueryKeys.all })
    },
  })
}

export function useDeleteSocialChannel() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (id) => socialChannelApi.softDelete(id),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: socialChannelQueryKeys.all })
    },
  })
}
