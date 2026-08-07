import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { unwrapApiData } from '@/shared/utils/apiHelpers'
import { postQueryKeys } from '@/modules/posts/services/postApi'
import { newsSiteApi, newsSiteQueryKeys } from '../services/newsSiteApi'

export function useNewsArticles(params) {
  return useQuery({
    queryKey: newsSiteQueryKeys.list(params),
    queryFn: async () => unwrapApiData(await newsSiteApi.list(params)),
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

export function useRebuildSite() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async () => unwrapApiData(await newsSiteApi.build()),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: newsSiteQueryKeys.all }),
  })
}
