import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { unwrapApiData } from '@/shared/utils/apiHelpers'
import { googleDriveApi, googleDriveQueryKeys } from '../services/googleDriveApi'
import { mediaAssetQueryKeys } from '../services/mediaAssetApi'

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

/** GDRIVE-03: nút Quét ngay — sau scan làm mới pipeline-state + danh sách media đang xem. */
export function useScanGoogleDriveNow() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async () => unwrapApiData(await googleDriveApi.scanNow()),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: googleDriveQueryKeys.pipelineState() })
      queryClient.invalidateQueries({ queryKey: mediaAssetQueryKeys.all })
    },
  })
}
