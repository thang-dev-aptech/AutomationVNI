import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { unwrapApiData } from '@/shared/utils/apiHelpers'
import { pageMessageApi, pageMessageKeys } from '../services/pageMessageApi'

export function usePageMessageList(params) {
  return useQuery({
    queryKey: pageMessageKeys.list(params),
    queryFn: async () => unwrapApiData(await pageMessageApi.filter(params)),
    refetchInterval: 30_000,
  })
}

export function usePageMessageSummary() {
  return useQuery({
    queryKey: pageMessageKeys.summary,
    queryFn: async () => unwrapApiData(await pageMessageApi.summary()),
    refetchInterval: 30_000,
  })
}

export function usePageConversation(id) {
  return useQuery({
    queryKey: pageMessageKeys.detail(id),
    queryFn: async () => unwrapApiData(await pageMessageApi.get(id)),
    enabled: Boolean(id),
    refetchInterval: id ? 20_000 : false,
  })
}

function useInvalidateMessages() {
  const queryClient = useQueryClient()
  return () => queryClient.invalidateQueries({ queryKey: pageMessageKeys.all })
}

export function useSyncPageMessages() {
  const invalidate = useInvalidateMessages()
  return useMutation({
    mutationFn: async (payload) => unwrapApiData(await pageMessageApi.sync(payload)),
    onSuccess: invalidate,
  })
}

export function useSubscribePageMessages() {
  return useMutation({
    mutationFn: async () => unwrapApiData(await pageMessageApi.subscribeFacebook()),
  })
}

export function useSendPageMessage() {
  const invalidate = useInvalidateMessages()
  return useMutation({
    mutationFn: async ({ id, text }) => unwrapApiData(await pageMessageApi.send(id, text)),
    onSuccess: invalidate,
  })
}

export function usePageMessageWorkflow() {
  const invalidate = useInvalidateMessages()
  return useMutation({
    mutationFn: async ({ id, action, value }) => {
      if (action === 'status') return unwrapApiData(await pageMessageApi.setStatus(id, value))
      if (action === 'assign') return unwrapApiData(await pageMessageApi.assign(id, value))
      if (action === 'note') return unwrapApiData(await pageMessageApi.note(id, value))
      throw new Error(`Unknown message action: ${action}`)
    },
    onSuccess: invalidate,
  })
}
