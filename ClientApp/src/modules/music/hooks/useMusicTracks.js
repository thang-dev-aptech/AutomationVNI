import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { unwrapApiData } from '@/shared/utils/apiHelpers'
import { musicTrackApi, musicTrackQueryKeys } from '../services/musicTrackApi'

export function useMusicTrackAll() {
  return useQuery({
    queryKey: musicTrackQueryKeys.all,
    queryFn: async () => unwrapApiData(await musicTrackApi.getAll()),
  })
}

export function useUploadMusicTrack() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async (formData) => unwrapApiData(await musicTrackApi.upload(formData)),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: musicTrackQueryKeys.all })
    },
  })
}

export function useDeleteMusicTrack() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (id) => musicTrackApi.softDelete(id),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: musicTrackQueryKeys.all })
    },
  })
}
