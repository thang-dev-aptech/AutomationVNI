import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { unwrapApiData } from '@/shared/utils/apiHelpers'
import { postApi, postQueryKeys } from '../services/postApi'

function invalidatePostQueries(queryClient, id) {
  queryClient.invalidateQueries({ queryKey: postQueryKeys.all })
  if (id) {
    queryClient.invalidateQueries({ queryKey: postQueryKeys.detail(id) })
    queryClient.invalidateQueries({ queryKey: postQueryKeys.generationStatus(id) })
    queryClient.invalidateQueries({ queryKey: postQueryKeys.timeline(id) })
  }
}

export function usePosts(params = { index: 1, size: 20 }) {
  return useQuery({
    queryKey: postQueryKeys.list(params),
    queryFn: async () => unwrapApiData(await postApi.filter(params)),
  })
}

export function usePostDetail(id) {
  return useQuery({
    queryKey: postQueryKeys.detail(id),
    queryFn: async () => unwrapApiData(await postApi.getById(id)),
    enabled: Boolean(id),
  })
}

export function usePostGenerationStatus(id, postStatus) {
  const shouldPoll = postStatus === 2 || postStatus === 3

  return useQuery({
    queryKey: postQueryKeys.generationStatus(id),
    queryFn: async () => unwrapApiData(await postApi.getGenerationStatus(id)),
    enabled: Boolean(id),
    refetchInterval: shouldPoll ? 5000 : false,
    retry: false,
  })
}

export function usePostTimeline(id) {
  return useQuery({
    queryKey: postQueryKeys.timeline(id),
    queryFn: async () => unwrapApiData(await postApi.getTimeline(id)),
    enabled: Boolean(id),
    retry: false,
  })
}

export function useCreatePost() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async (payload) => unwrapApiData(await postApi.create(payload)),
    onSuccess: () => invalidatePostQueries(queryClient),
  })
}

export function useUpdatePost() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async ({ id, payload }) =>
      unwrapApiData(await postApi.update(id, payload)),
    onSuccess: (_, variables) => invalidatePostQueries(queryClient, variables.id),
  })
}

export function useDeletePost() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (id) => postApi.softDelete(id),
    onSuccess: () => invalidatePostQueries(queryClient),
  })
}

function useWorkflowMutation(mutationFn, getId = (v) => v) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn,
    onSuccess: (_, variables) => invalidatePostQueries(queryClient, getId(variables)),
  })
}

export function useSubmitPostReview() {
  return useWorkflowMutation(async (id) => unwrapApiData(await postApi.submitReview(id)))
}

export function useApprovePost() {
  return useWorkflowMutation(async (id) => unwrapApiData(await postApi.approve(id)))
}

export function useRejectPost() {
  return useWorkflowMutation(
    async ({ id, reason }) => unwrapApiData(await postApi.reject(id, { reason })),
    (v) => v.id,
  )
}

export function useSchedulePost() {
  return useWorkflowMutation(
    async ({ id, scheduledAt, timezone }) =>
      unwrapApiData(await postApi.schedule(id, { scheduledAt, timezone })),
    (v) => v.id,
  )
}

export function useCancelSchedulePost() {
  return useWorkflowMutation(async (id) => unwrapApiData(await postApi.cancelSchedule(id)))
}

export function usePublishNowPost() {
  return useWorkflowMutation(async (id) => unwrapApiData(await postApi.publishNow(id)))
}
