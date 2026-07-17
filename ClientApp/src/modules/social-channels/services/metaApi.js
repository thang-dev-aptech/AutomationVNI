import axiosInstance from '@/api/axiosInstance'

export const metaApi = {
  getConnectUrl: () => axiosInstance.get('/api/meta/connect-url'),
}
