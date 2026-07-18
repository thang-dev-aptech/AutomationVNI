import { useMutation, useQuery } from '@tanstack/react-query'
import { unwrapApiData } from '@/shared/utils/apiHelpers'
import { bulkApi, bulkQueryKeys } from '../services/bulkApi'

export function useBulkCreate() {
  return useMutation({
    mutationFn: async (payload) => unwrapApiData(await bulkApi.create(payload)),
  })
}

export function useBulkApprove() {
  return useMutation({
    mutationFn: async (payload) => unwrapApiData(await bulkApi.approve(payload)),
  })
}

export function useBulkSchedule() {
  return useMutation({
    mutationFn: async (payload) => unwrapApiData(await bulkApi.schedule(payload)),
  })
}

export function useSuggestIdeas() {
  return useMutation({
    mutationFn: async (payload) => unwrapApiData(await bulkApi.suggestIdeas(payload)),
  })
}

/**
 * Theo dõi tiến độ batch. Tự refetch mỗi 3s khi còn bài đang sinh (Queued/Generating...).
 */
export function useBatch(batchId, { enabled = true } = {}) {
  return useQuery({
    queryKey: bulkQueryKeys.batch(batchId),
    queryFn: async () => unwrapApiData(await bulkApi.getBatch(batchId)),
    enabled: Boolean(batchId) && enabled,
    refetchInterval: (query) => {
      const data = query.state.data
      if (!data) return 3000
      const pending =
        (data.byStatus?.Queued ?? 0) +
        (data.byStatus?.Generating ?? 0) +
        (data.byStatus?.GeneratingMedia ?? 0) +
        (data.byStatus?.RenderingTemplate ?? 0)
      return pending > 0 ? 3000 : false
    },
  })
}
