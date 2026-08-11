import axiosInstance from '@/api/axiosInstance'
import { unwrapApiData } from '@/shared/utils/apiHelpers'

export const pageMetricsQueryKeys = {
  overview: (days) => ['page-metrics', 'overview', days],
}

export const pageMetricsApi = {
  async fetchOverview(days = 30) {
    const response = await axiosInstance.get('/api/PageMetrics/overview', { params: { days } })
    return unwrapApiData(response)
  },

  async syncNow() {
    const response = await axiosInstance.post('/api/PageMetrics/sync')
    return unwrapApiData(response)
  },
}
