import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { unwrapApiData } from '@/shared/utils/apiHelpers'
import { postQueryKeys } from '@/modules/posts/services/postApi'
import { crawlApi, crawlQueryKeys } from '../services/crawlApi'
import { IN_FLIGHT_STATUSES } from '../constants/crawlConstants'

function invalidateCrawl(queryClient) {
  queryClient.invalidateQueries({ queryKey: crawlQueryKeys.all })
}

export function useCrawlSources(onlyActive = false) {
  return useQuery({
    queryKey: crawlQueryKeys.sources(onlyActive),
    queryFn: async () => unwrapApiData(await crawlApi.getSources(onlyActive)),
  })
}

export function useCrawlRuns(sourceId) {
  return useQuery({
    queryKey: crawlQueryKeys.runs(sourceId ?? null),
    queryFn: async () => unwrapApiData(await crawlApi.getRuns(sourceId)),
  })
}

export function useCrawlSummary() {
  return useQuery({
    queryKey: crawlQueryKeys.summary(),
    queryFn: async () => unwrapApiData(await crawlApi.getSummary()),
    refetchInterval: 15000,
  })
}

export function useCrawledArticles(params) {
  return useQuery({
    queryKey: crawlQueryKeys.articles(params),
    queryFn: async () => unwrapApiData(await crawlApi.filterArticles(params)),
    // Còn tin đang chấm trùng / xào nháp thì tự làm mới, xong thì dừng hẳn.
    refetchInterval: (query) => {
      const items = query.state.data?.items ?? []
      return items.some((a) => IN_FLIGHT_STATUSES.includes(Number(a.status))) ? 5000 : false
    },
  })
}

export function useCreateCrawlSource() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async (payload) => unwrapApiData(await crawlApi.createSource(payload)),
    onSuccess: () => invalidateCrawl(queryClient),
  })
}

export function useUpdateCrawlSource() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async ({ id, payload }) => unwrapApiData(await crawlApi.updateSource(id, payload)),
    onSuccess: () => invalidateCrawl(queryClient),
  })
}

export function useDeleteCrawlSource() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async (id) => unwrapApiData(await crawlApi.deleteSource(id)),
    onSuccess: () => invalidateCrawl(queryClient),
  })
}

export function useTestCrawlSource() {
  return useMutation({
    mutationFn: async (payload) => unwrapApiData(await crawlApi.testSource(payload)),
  })
}

/**
 * Trạng thái đăng nhập Facebook của trình duyệt OpenClaw.
 * Mỗi lần gọi là một lượt điều hướng trình duyệt sang facebook.com, nên chỉ bật khi
 * người dùng thực sự đang chọn loại nguồn fanpage.
 */
export function useFacebookStatus(enabled) {
  return useQuery({
    queryKey: crawlQueryKeys.facebookStatus(),
    queryFn: async () => unwrapApiData(await crawlApi.facebookStatus()),
    enabled: Boolean(enabled),
    staleTime: 60_000,
    retry: false,
  })
}

export function useCrawlNow() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async (id) => unwrapApiData(await crawlApi.crawlNow(id)),
    onSuccess: () => invalidateCrawl(queryClient),
  })
}

export function useApproveArticle() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async ({ id, payload }) => unwrapApiData(await crawlApi.approve(id, payload)),
    onSuccess: () => {
      invalidateCrawl(queryClient)
      // Duyệt xong sinh ra Post mới nên danh sách bài viết cũng phải làm mới.
      queryClient.invalidateQueries({ queryKey: postQueryKeys.all })
    },
  })
}

export function useRejectArticle() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async ({ id, reason }) => unwrapApiData(await crawlApi.reject(id, { reason })),
    onSuccess: () => invalidateCrawl(queryClient),
  })
}

export function useMarkNotDuplicate() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async (id) => unwrapApiData(await crawlApi.notDuplicate(id)),
    onSuccess: () => invalidateCrawl(queryClient),
  })
}

export function useRededupArticle() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async (id) => unwrapApiData(await crawlApi.rededup(id)),
    onSuccess: () => invalidateCrawl(queryClient),
  })
}
