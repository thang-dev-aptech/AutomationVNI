import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { pageMetricsApi, pageMetricsQueryKeys } from '../services/pageMetricsApi'

export function usePageMetrics(days = 30) {
  return useQuery({
    queryKey: pageMetricsQueryKeys.overview(days),
    queryFn: () => pageMetricsApi.fetchOverview(days),
    // Số này chỉ đổi khi worker chạy — mặc định 6 tiếng một lần. Hỏi lại mỗi phút như dashboard
    // vận hành là tự đốt truy vấn để nhận về đúng con số cũ.
    staleTime: 5 * 60_000,
  })
}

export function useSyncPageMetrics() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: () => pageMetricsApi.syncNow(),
    onSuccess: () => {
      // Đồng bộ chạy nền và mất khoảng 75 giây cho 15 page. Chờ rồi mới nạp lại — nạp ngay thì
      // chắc chắn nhận về số cũ và người dùng tưởng nút không ăn.
      setTimeout(() => {
        queryClient.invalidateQueries({ queryKey: ['page-metrics'] })
      }, 80_000)
    },
  })
}
