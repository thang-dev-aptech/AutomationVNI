import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { unwrapApiData } from '@/shared/utils/apiHelpers'
import { commentApi, commentQueryKeys } from '../services/commentApi'

export function useCommentInbox(params) {
  return useQuery({
    queryKey: commentQueryKeys.list(params),
    queryFn: async () => unwrapApiData(await commentApi.filter(params)),
  })
}

export function useCommentSummary() {
  return useQuery({
    queryKey: commentQueryKeys.summary,
    queryFn: async () => unwrapApiData(await commentApi.summary()),
    refetchInterval: 60_000,
  })
}

export function useCommentThread(id) {
  return useQuery({
    queryKey: commentQueryKeys.thread(id),
    queryFn: async () => unwrapApiData(await commentApi.getThread(id)),
    enabled: Boolean(id),
  })
}

export function useCommentActions(id) {
  return useQuery({
    queryKey: commentQueryKeys.actions(id),
    queryFn: async () => unwrapApiData(await commentApi.actions(id)),
    enabled: Boolean(id),
  })
}

function useInvalidateComments() {
  const queryClient = useQueryClient()
  return () => queryClient.invalidateQueries({ queryKey: commentQueryKeys.all })
}

export function useSyncComments() {
  const invalidate = useInvalidateComments()
  return useMutation({
    mutationFn: async (payload) => unwrapApiData(await commentApi.sync(payload)),
    onSuccess: invalidate,
  })
}

export function useReplyComment() {
  const invalidate = useInvalidateComments()
  return useMutation({
    mutationFn: async ({ id, message }) => unwrapApiData(await commentApi.reply(id, message)),
    onSuccess: invalidate,
  })
}

export function useCommentModeration() {
  const invalidate = useInvalidateComments()
  return useMutation({
    mutationFn: async ({ id, action, approve }) => {
      if (action === 'hide') return unwrapApiData(await commentApi.hide(id))
      if (action === 'unhide') return unwrapApiData(await commentApi.unhide(id))
      if (action === 'delete') return unwrapApiData(await commentApi.remove(id))
      if (action === 'pending') return unwrapApiData(await commentApi.pending(id, approve))
      throw new Error(`Unknown action: ${action}`)
    },
    onSuccess: invalidate,
  })
}

export function useSetCommentStatus() {
  const invalidate = useInvalidateComments()
  return useMutation({
    mutationFn: async ({ id, status }) => unwrapApiData(await commentApi.setStatus(id, status)),
    onSuccess: invalidate,
  })
}

export function useAssignComment() {
  const invalidate = useInvalidateComments()
  return useMutation({
    mutationFn: async ({ id, assignedTo }) => unwrapApiData(await commentApi.assign(id, assignedTo)),
    onSuccess: invalidate,
  })
}

export function useCommentNote() {
  const invalidate = useInvalidateComments()
  return useMutation({
    mutationFn: async ({ id, note }) => unwrapApiData(await commentApi.note(id, note)),
    onSuccess: invalidate,
  })
}
