import axiosInstance from '@/api/axiosInstance'

export const socialChannelApi = {
  getAll: () => axiosInstance.get('/api/SocialChannel'),
  getById: (id) => axiosInstance.get(`/api/SocialChannel/${id}`),
  filter: (params) => axiosInstance.post('/api/SocialChannel/filter', params),
  create: (payload) => axiosInstance.post('/api/SocialChannel', payload),
  update: (id, payload) => axiosInstance.put(`/api/SocialChannel/${id}`, payload),
  softDelete: (id) => axiosInstance.delete(`/api/SocialChannel/${id}`),
}

export const socialChannelQueryKeys = {
  all: ['social-channels'],
  list: (params) => ['social-channels', 'list', params],
  detail: (id) => ['social-channels', 'detail', id],
}
