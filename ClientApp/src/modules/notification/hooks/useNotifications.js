import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { unwrapApiData } from '@/shared/utils/apiHelpers'
import { notificationApi, notificationQueryKeys } from '../services/notificationApi'

/**
 * Hỏi lại mỗi 20 giây. Đủ nhanh để bắt kịp thao tác của người khác (mục đích của cả tính
 * năng), đủ chậm để một tab mở cả ngày không thành 4.000 request.
 *
 * refetchIntervalInBackground để mặc định (false): tab ẩn thì ngừng hỏi, quay lại tab là
 * react-query tự nạp ngay nhờ refetchOnWindowFocus.
 */
export function useNotificationFeed(size = 30) {
  return useQuery({
    queryKey: notificationQueryKeys.feed(size),
    queryFn: async () => unwrapApiData(await notificationApi.getFeed(size)),
    refetchInterval: 20000,
  })
}

export function useMarkAllRead() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: () => notificationApi.markAllRead(),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: notificationQueryKeys.all }),
  })
}
