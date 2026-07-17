import { useQuery } from '@tanstack/react-query'
import { unwrapApiData } from '@/shared/utils/apiHelpers'
import { publishLogApi, publishLogQueryKeys } from '../services/publishLogApi'

const REFETCH_INTERVAL_MS = 10_000

export function usePublishLogs(params = { index: 1, size: 50 }, options = {}) {
  return useQuery({
    queryKey: publishLogQueryKeys.list(params),
    queryFn: async () => unwrapApiData(await publishLogApi.filter(params)),
    refetchInterval: options.refetchInterval ?? REFETCH_INTERVAL_MS,
  })
}

export function usePublishLogsByPost(postId) {
  return useQuery({
    queryKey: publishLogQueryKeys.byPost(postId),
    queryFn: async () => unwrapApiData(await publishLogApi.getByPost(postId)),
    enabled: Boolean(postId),
  })
}
