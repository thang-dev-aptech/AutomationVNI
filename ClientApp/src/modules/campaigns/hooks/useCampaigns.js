import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { unwrapApiData } from '@/shared/utils/apiHelpers'
import { campaignApi, campaignQueryKeys } from '../services/campaignApi'

function invalidateCampaigns(queryClient) {
  queryClient.invalidateQueries({ queryKey: campaignQueryKeys.all })
}

export function useCampaignSummaries() {
  return useQuery({
    queryKey: campaignQueryKeys.summaries,
    queryFn: async () => unwrapApiData(await campaignApi.getSummaries()),
  })
}

export function useCampaignDetail(id) {
  return useQuery({
    queryKey: campaignQueryKeys.detail(id),
    queryFn: async () => unwrapApiData(await campaignApi.getDetail(id)),
    enabled: Boolean(id),
  })
}

export function useCreateCampaign() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async (payload) => unwrapApiData(await campaignApi.create(payload)),
    onSuccess: () => invalidateCampaigns(queryClient),
  })
}

export function useUpdateCampaign() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async ({ id, payload }) =>
      unwrapApiData(await campaignApi.update(id, payload)),
    onSuccess: (_data, vars) => {
      invalidateCampaigns(queryClient)
      if (vars?.id) {
        queryClient.invalidateQueries({ queryKey: campaignQueryKeys.detail(vars.id) })
      }
    },
  })
}

function useCampaignLifecycleMutation(fn) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async (id) => unwrapApiData(await fn(id)),
    onSuccess: (_data, id) => {
      invalidateCampaigns(queryClient)
      if (id) {
        queryClient.invalidateQueries({ queryKey: campaignQueryKeys.detail(id) })
      }
    },
  })
}

export function usePauseCampaign() {
  return useCampaignLifecycleMutation(campaignApi.pause)
}

export function useResumeCampaign() {
  return useCampaignLifecycleMutation(campaignApi.resume)
}

export function useEndCampaign() {
  return useCampaignLifecycleMutation(campaignApi.end)
}

export function useDeleteCampaign() {
  return useCampaignLifecycleMutation(campaignApi.softDelete)
}
