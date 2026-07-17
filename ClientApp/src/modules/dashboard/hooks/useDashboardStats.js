import { useQuery } from '@tanstack/react-query'
import { usePermissions } from '@/shared/hooks/usePermissions'
import { dashboardApi, dashboardQueryKeys } from '../services/dashboardApi'

export function useDashboardStats() {
  const { roles, currentUser } = usePermissions()

  return useQuery({
    queryKey: dashboardQueryKeys.stats(currentUser?.id, roles),
    queryFn: () =>
      dashboardApi.fetchStats({
        userId: currentUser?.id,
        roles,
      }),
    staleTime: 30_000,
    refetchInterval: 60_000,
  })
}
