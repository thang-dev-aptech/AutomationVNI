import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { unwrapApiData } from '@/shared/utils/apiHelpers'
import { mediaAssetQueryKeys } from '../services/mediaAssetApi'
import { mediaCaptionJobApi, mediaCaptionJobQueryKeys } from '../services/mediaCaptionJobApi'

export const CAPTION_JOB_POLL_MS = 3000
export const isCaptionJobActive = (status) => status === 'Queued' || status === 'Running'

export function useMediaCaptionJob(id) {
  return useQuery({
    queryKey: mediaCaptionJobQueryKeys.detail(id),
    queryFn: async () => unwrapApiData(await mediaCaptionJobApi.get(id)),
    enabled: Boolean(id),
    refetchInterval: (query) => (isCaptionJobActive(query.state.data?.status) ? CAPTION_JOB_POLL_MS : false),
  })
}

export function useMediaCaptionJobList() {
  return useQuery({
    queryKey: mediaCaptionJobQueryKeys.list,
    queryFn: async () => unwrapApiData(await mediaCaptionJobApi.list()),
  })
}

// Mutations đổi item Pending/Running nên cũng làm captionQueued của ảnh đổi → refetch cả danh sách media.
function useCaptionJobMutation(mutationFn) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async (arg) => unwrapApiData(await mutationFn(arg)),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: mediaCaptionJobQueryKeys.all })
      queryClient.invalidateQueries({ queryKey: mediaAssetQueryKeys.all })
    },
  })
}

export const useCreateMediaCaptionJob = () => useCaptionJobMutation(mediaCaptionJobApi.create)
export const useRetryCaptionItem = () => useCaptionJobMutation(mediaCaptionJobApi.retryItem)
export const useRetryFailedCaptionItems = () => useCaptionJobMutation(mediaCaptionJobApi.retryFailed)
