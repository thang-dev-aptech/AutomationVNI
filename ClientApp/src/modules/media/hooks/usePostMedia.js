import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { unwrapApiData } from '@/shared/utils/apiHelpers'
import { postQueryKeys } from '@/modules/posts/services/postApi'
import { mediaAssetQueryKeys } from '../services/mediaAssetApi'
import { postMediaApi, postMediaQueryKeys } from '../services/postMediaApi'

function invalidatePostMediaQueries(queryClient, postId) {
  queryClient.invalidateQueries({ queryKey: postMediaQueryKeys.all })
  if (postId) {
    queryClient.invalidateQueries({ queryKey: postMediaQueryKeys.byPost(postId) })
    queryClient.invalidateQueries({ queryKey: postQueryKeys.detail(postId) })
    queryClient.invalidateQueries({ queryKey: postQueryKeys.generationStatus(postId) })
  }
  queryClient.invalidateQueries({ queryKey: mediaAssetQueryKeys.all })
}

export function usePostMediaByPost(postId, { refetchInterval = false } = {}) {
  return useQuery({
    queryKey: postMediaQueryKeys.byPost(postId),
    queryFn: async () => unwrapApiData(await postMediaApi.getByPost(postId)),
    enabled: Boolean(postId),
    refetchInterval,
  })
}

export function useCreatePostMedia() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async (payload) => unwrapApiData(await postMediaApi.create(payload)),
    onSuccess: (_, variables) => invalidatePostMediaQueries(queryClient, variables.postId),
  })
}

export function useDeletePostMedia() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({ id }) => postMediaApi.softDelete(id),
    onSuccess: (_, variables) => invalidatePostMediaQueries(queryClient, variables.postId),
  })
}

/**
 * Gắn nhiều ảnh từ kho vào bài. Nếu bài chưa có cover thì ảnh đầu làm cover (role 4),
 * còn lại là attachment (role 3), nối tiếp sortOrder hiện có.
 */
export function useAttachPostMedia() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async ({ postId, mediaIds, hasCover, nextSortOrder = 1 }) => {
      let sortOrder = nextSortOrder
      let coverAssigned = hasCover
      for (const mediaId of mediaIds) {
        const isCover = !coverAssigned
        coverAssigned = true
        // eslint-disable-next-line no-await-in-loop
        await postMediaApi.create({
          postId,
          mediaId,
          mediaRole: isCover ? 4 : 3,
          sortOrder: isCover ? 0 : sortOrder++,
        })
      }
    },
    onSuccess: (_, variables) => invalidatePostMediaQueries(queryClient, variables.postId),
  })
}

/** Đưa 1 ảnh attachment lên làm cover, cover cũ (nếu có) hạ xuống attachment. */
export function useSwapPostCover() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async ({ postId, linkId, currentCoverLinkId, nextSortOrder = 1 }) => {
      if (currentCoverLinkId) {
        await postMediaApi.update(currentCoverLinkId, { mediaRole: 3, sortOrder: nextSortOrder })
      }
      return unwrapApiData(await postMediaApi.update(linkId, { mediaRole: 4, sortOrder: 0 }))
    },
    onSuccess: (_, variables) => invalidatePostMediaQueries(queryClient, variables.postId),
  })
}
