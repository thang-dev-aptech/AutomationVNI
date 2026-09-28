import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { unwrapApiData } from '@/shared/utils/apiHelpers'
import { googleDriveApi, googleDriveQueryKeys } from '../services/googleDriveApi'

export function useGoogleDrivePipelineState(enabled = true) {
  return useQuery({
    queryKey: googleDriveQueryKeys.pipelineState(),
    queryFn: async () => unwrapApiData(await googleDriveApi.getPipelineState()),
    enabled: Boolean(enabled),
  })
}

export function useSetGoogleDrivePipelineEnabled() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async (enabled) => unwrapApiData(await googleDriveApi.setPipelineEnabled(enabled)),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: googleDriveQueryKeys.pipelineState() }),
  })
}
