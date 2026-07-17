import axiosInstance from '@/api/axiosInstance'

export const authApi = {
  login: (payload) => axiosInstance.post('/api/Auth/login', payload),
  me: () => axiosInstance.get('/api/Auth/me'),
  logout: () => axiosInstance.post('/api/Auth/logout'),
}
