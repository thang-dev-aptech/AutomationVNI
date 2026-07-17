import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { unwrapApiData } from '@/shared/utils/apiHelpers'
import { generationJobApi, generationJobQueryKeys } from '../services/generationJobApi'

const REFETCH_INTERVAL_MS = 10_000

export function useGenerationJobs(params = { index: 1, size: 50 }, options = {}) {
  return useQuery({
    queryKey: generationJobQueryKeys.list(params),
    queryFn: async () => unwrapApiData(await generationJobApi.filter(params)),
    refetchInterval: options.refetchInterval ?? REFETCH_INTERVAL_MS,
  })
}

export function usePendingGenerationJobs(batchSize = 20) {
  return useQuery({
    queryKey: generationJobQueryKeys.pending(batchSize),
    queryFn: async () => unwrapApiData(await generationJobApi.getPending(batchSize)),
    refetchInterval: REFETCH_INTERVAL_MS,
  })
}

function useJobActionMutation(mutationFn) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: generationJobQueryKeys.all })
    },
  })
}

export function useProcessGenerationJob() {
  return useJobActionMutation(async (id) => unwrapApiData(await generationJobApi.process(id)))
}

export function useRetryGenerationJob() {
  return useJobActionMutation(async (id) => unwrapApiData(await generationJobApi.retry(id)))
}

export function useCancelGenerationJob() {
  return useJobActionMutation(async (id) => unwrapApiData(await generationJobApi.cancel(id)))
}
