import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { unwrapApiData } from '@/shared/utils/apiHelpers'
import { metaApi } from '../services/metaApi'
import { socialChannelApi, socialChannelQueryKeys } from '../services/socialChannelApi'
import {
  socialConnectionApi,
  socialConnectionQueryKeys,
} from '../services/socialConnectionApi'

function invalidateChannelQueries(queryClient) {
  queryClient.invalidateQueries({ queryKey: socialChannelQueryKeys.all })
  queryClient.invalidateQueries({ queryKey: socialConnectionQueryKeys.all })
}

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

export function useSocialConnections() {
  return useQuery({
    queryKey: socialConnectionQueryKeys.all,
    queryFn: async () => unwrapApiData(await socialConnectionApi.getAll()),
  })
}

export function useCreateSocialChannel() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async (payload) => unwrapApiData(await socialChannelApi.create(payload)),
    onSuccess: () => invalidateChannelQueries(queryClient),
  })
}

export function useUpdateSocialChannel() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async ({ id, payload }) =>
      unwrapApiData(await socialChannelApi.update(id, payload)),
    onSuccess: () => invalidateChannelQueries(queryClient),
  })
}

export function useDeleteSocialChannel() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (id) => socialChannelApi.softDelete(id),
    onSuccess: () => invalidateChannelQueries(queryClient),
  })
}

export function useDisconnectSocialConnection() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (id) => socialConnectionApi.disconnect(id),
    onSuccess: () => invalidateChannelQueries(queryClient),
  })
}

export function useMetaConnectUrl() {
  return useMutation({
    mutationFn: async () => unwrapApiData(await metaApi.getConnectUrl()),
  })
}
