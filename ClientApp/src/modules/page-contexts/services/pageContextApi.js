import axiosInstance from '@/api/axiosInstance'

export const pageContextApi = {
  getAll: () => axiosInstance.get('/api/PageContext'),
  getById: (id) => axiosInstance.get(`/api/PageContext/${id}`),
  getByChannel: (channelId) => axiosInstance.get(`/api/PageContext/by-channel/${channelId}`),
  filter: (params) => axiosInstance.post('/api/PageContext/filter', params),
  create: (payload) => axiosInstance.post('/api/PageContext', payload),
  update: (id, payload) => axiosInstance.put(`/api/PageContext/${id}`, payload),
  softDelete: (id) => axiosInstance.delete(`/api/PageContext/${id}`),
  import: (payload) => axiosInstance.post('/api/PageContext/import', payload),
}

export const pageContextQueryKeys = {
  all: ['page-contexts'],
  list: (params) => ['page-contexts', 'list', params],
  detail: (id) => ['page-contexts', 'detail', id],
  byChannel: (channelId) => ['page-contexts', 'by-channel', channelId],
}
