import axiosInstance from '@/api/axiosInstance'

export const socialConnectionApi = {
  getAll: () => axiosInstance.get('/api/SocialConnection'),
  getById: (id) => axiosInstance.get(`/api/SocialConnection/${id}`),
  disconnect: (id) => axiosInstance.delete(`/api/SocialConnection/${id}`),
}

export const socialConnectionQueryKeys = {
  all: ['social-connections'],
  detail: (id) => ['social-connections', 'detail', id],
}
