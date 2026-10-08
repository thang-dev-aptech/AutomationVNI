import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { unwrapApiData } from '@/shared/utils/apiHelpers'
import axiosInstance from '@/api/axiosInstance'
import { inboxApi, inboxQueryKeys } from '../services/inboxApi'

export function useInboxList(params, options = {}) {
  return useQuery({
    queryKey: inboxQueryKeys.list(params),
    queryFn: async () => unwrapApiData(await inboxApi.filter(params)),
    refetchInterval: 30_000,
    ...options,
  })
}

export function useInboxSummary(options = {}) {
  return useQuery({
    queryKey: inboxQueryKeys.summary,
    queryFn: async () => unwrapApiData(await inboxApi.summary()),
    refetchInterval: 30_000,
    ...options,
  })
}

export function useInboxProfile(kind, id, options = {}) {
  return useQuery({
    queryKey: inboxQueryKeys.profile(kind, id),
    queryFn: async () => unwrapApiData(await inboxApi.getProfile(kind, id)),
    enabled: Boolean(kind && id),
    ...options,
  })
}

export function useSuggestReply() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async ({ kind, id }) => {
      const res = await inboxApi.suggestReply(kind, id)
      const unwrapped = unwrapApiData(res)
      return unwrapped || res?.data || res
    },
    onSuccess: (_, { kind, id }) => {
      queryClient.invalidateQueries({ queryKey: inboxQueryKeys.profile(kind, id) })
    },
  })
}

export function useActiveUsers(options = {}) {
  return useQuery({
    queryKey: ['users', 'active'],
    queryFn: async () => {
      try {
        const res = await axiosInstance.get('/api/Users')
        return unwrapApiData(res) || []
      } catch {
        return []
      }
    },
    staleTime: 5 * 60_000,
    ...options,
  })
}
