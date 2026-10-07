import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { unwrapApiData } from '@/shared/utils/apiHelpers'
import { channelGroupApi, channelGroupQueryKeys } from '../services/channelGroupApi'

function invalidateChannelGroups(queryClient) {
  queryClient.invalidateQueries({ queryKey: channelGroupQueryKeys.all })
}

export function useChannelGroupAll({ enabled = true } = {}) {
  return useQuery({
    queryKey: channelGroupQueryKeys.all,
    queryFn: async () => unwrapApiData(await channelGroupApi.getAll()),
    enabled,
  })
}

export function useCreateChannelGroup() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async (payload) => unwrapApiData(await channelGroupApi.create(payload)),
    onSuccess: () => invalidateChannelGroups(queryClient),
  })
}

export function useUpdateChannelGroup() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async ({ id, payload }) =>
      unwrapApiData(await channelGroupApi.update(id, payload)),
    onSuccess: () => invalidateChannelGroups(queryClient),
  })
}

export function useDeleteChannelGroup() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (id) => channelGroupApi.softDelete(id),
    onSuccess: () => invalidateChannelGroups(queryClient),
  })
}

export function usePreviewChannelGroupImport() {
  return useMutation({
    mutationFn: async ({ file, mode }) =>
      unwrapApiData(await channelGroupApi.previewImport(file, mode)),
  })
}

export function useCommitChannelGroupImport() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async ({ file, mode }) =>
      unwrapApiData(await channelGroupApi.commitImport(file, mode)),
    onSuccess: () => invalidateChannelGroups(queryClient),
  })
}
