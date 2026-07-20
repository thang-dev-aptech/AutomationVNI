import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { unwrapApiData } from '@/shared/utils/apiHelpers'
import { generationJobApi } from '@/modules/jobs/services/generationJobApi'
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
  const shouldPoll = [2, 3, 12, 14].includes(Number(postStatus))

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

export function useDeleteAllPosts() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async () => unwrapApiData(await postApi.softDeleteAll()),
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
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async (id) => unwrapApiData(await postApi.publishNow(id)),
    // onSettled: refresh dù publish thành công hay lỗi (post đã đổi status ở server).
    onSettled: (_data, _error, id) => invalidatePostQueries(queryClient, id),
  })
}

/**
 * Sinh nội dung 1-click: queue job rồi process ngay (backend không có worker tự động).
 * Trả về kết quả queue; process chạy đồng bộ nên khi resolve là job đã xong.
 */
function useGenerationStep(queueFn) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async (postId) => {
      const queued = unwrapApiData(await queueFn(postId))
      if (queued?.jobId) {
        await generationJobApi.process(queued.jobId)
      }
      return queued
    },
    onSuccess: (_, postId) => {
      invalidatePostQueries(queryClient, postId)
      queryClient.invalidateQueries({ queryKey: ['generation-jobs'] })
    },
  })
}

export function useGenerateText() {
  return useGenerationStep((id) => postApi.queueTextGeneration(id))
}

export function useGenerateImage() {
  return useGenerationStep((id) => postApi.queueImageGeneration(id))
}

export function useRenderOverlay() {
  return useGenerationStep((id) => postApi.queueImageRender(id))
}

/** One-click: tạo bài + sinh text + sinh ảnh + set Approved (bỏ duyệt). Trả post đã có content + media. */
export function useCreateAndGeneratePost() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async (payload) => unwrapApiData(await postApi.createAndGenerate(payload)),
    onSuccess: () => invalidatePostQueries(queryClient),
  })
}

/** Regenerate ở màn preview: gọi endpoint trả về post (đã process + set lại Approved). */
function useRegenerate(apiFn) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async (id) => unwrapApiData(await apiFn(id)),
    onSettled: (_data, _error, id) => {
      invalidatePostQueries(queryClient, id)
      queryClient.invalidateQueries({ queryKey: ['generation-jobs'] })
    },
  })
}

export function useRegenerateText() {
  return useRegenerate((id) => postApi.regenerateText(id))
}

export function useRegenerateImage() {
  return useRegenerate((id) => postApi.regenerateImage(id))
}
