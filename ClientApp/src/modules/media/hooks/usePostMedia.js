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

export function usePostMediaByPost(postId) {
  return useQuery({
    queryKey: postMediaQueryKeys.byPost(postId),
    queryFn: async () => unwrapApiData(await postMediaApi.getByPost(postId)),
    enabled: Boolean(postId),
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

export function useSetPostCover() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async ({ postId, mediaId, existingCoverId }) => {
      if (existingCoverId) {
        await postMediaApi.softDelete(existingCoverId)
      }
      return unwrapApiData(
        await postMediaApi.create({
          postId,
          mediaId,
          mediaRole: 1,
          sortOrder: 0,
        }),
      )
    },
    onSuccess: (_, variables) => invalidatePostMediaQueries(queryClient, variables.postId),
  })
}
