import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { unwrapApiData } from '@/shared/utils/apiHelpers'
import { postQueryKeys } from '@/modules/posts/services/postApi'
import { newsSiteApi, newsSiteQueryKeys } from '../services/newsSiteApi'

export function useNewsArticles(params) {
  return useQuery({
    queryKey: newsSiteQueryKeys.list(params),
    queryFn: async () => unwrapApiData(await newsSiteApi.list(params)),
    // AI viết mất ~40 giây ở nền. Không hỏi lại thì bài viết xong vẫn không hiện, người dùng
    // phải tự bấm F5 mà không biết là phải bấm.
    refetchInterval: 15_000,
  })
}

export function useNewsSiteStatus() {
  return useQuery({
    queryKey: newsSiteQueryKeys.status(),
    queryFn: async () => unwrapApiData(await newsSiteApi.status()),
    staleTime: 60_000,
  })
}

export function usePublishToFanpage() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async ({ id, payload }) => unwrapApiData(await newsSiteApi.toFanpage(id, payload)),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: newsSiteQueryKeys.all })
      // Bài fanpage vừa tạo nằm ở module Bài đăng. Không nạp lại thì người dùng sang đó
      // thấy danh sách cũ và tưởng chưa tạo được gì.
      queryClient.invalidateQueries({ queryKey: postQueryKeys.all })
    },
  })
}

export function useUnpublish() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async (id) => unwrapApiData(await newsSiteApi.unpublish(id)),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: newsSiteQueryKeys.all }),
  })
}

export function useRebuildSite() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async () => unwrapApiData(await newsSiteApi.build()),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: newsSiteQueryKeys.all }),
  })
}
