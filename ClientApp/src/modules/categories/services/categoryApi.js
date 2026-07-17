import axiosInstance from '@/api/axiosInstance'

export const categoryApi = {
  getAll: () => axiosInstance.get('/api/Category'),
  getById: (id) => axiosInstance.get(`/api/Category/${id}`),
  filter: (params) => axiosInstance.post('/api/Category/filter', params),
  create: (payload) => axiosInstance.post('/api/Category', payload),
  update: (id, payload) => axiosInstance.put(`/api/Category/${id}`, payload),
  softDelete: (id) => axiosInstance.delete(`/api/Category/${id}`),
}

export const categoryQueryKeys = {
  all: ['categories'],
  list: (params) => ['categories', 'list', params],
  detail: (id) => ['categories', 'detail', id],
}
