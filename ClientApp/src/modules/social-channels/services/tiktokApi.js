import axiosInstance from '@/api/axiosInstance'

export const tiktokApi = {
  getConnectUrl: () => axiosInstance.get('/api/tiktok/connect-url'),
}
