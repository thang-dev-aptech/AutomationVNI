import axiosInstance from '@/api/axiosInstance'

export const threadsApi = {
  getConnectUrl: () => axiosInstance.get('/api/threads/connect-url'),
}
