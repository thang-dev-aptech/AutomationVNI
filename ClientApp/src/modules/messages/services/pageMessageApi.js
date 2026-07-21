import axiosInstance from '@/api/axiosInstance'

export const pageMessageApi = {
  filter: (params) => axiosInstance.post('/api/PageMessage/filter', params),
  summary: () => axiosInstance.get('/api/PageMessage/summary'),
  get: (id) => axiosInstance.get(`/api/PageMessage/${id}`),
  sync: (payload) => axiosInstance.post('/api/PageMessage/sync', payload),
  subscribeFacebook: () => axiosInstance.post('/api/PageMessage/subscribe-facebook'),
  send: (id, text) => axiosInstance.post(`/api/PageMessage/${id}/send`, { text }),
  setStatus: (id, status) => axiosInstance.post(`/api/PageMessage/${id}/status`, { status }),
  assign: (id, assignedTo) => axiosInstance.post(`/api/PageMessage/${id}/assign`, { assignedTo }),
  note: (id, note) => axiosInstance.post(`/api/PageMessage/${id}/note`, { note }),
}

export const pageMessageKeys = {
  all: ['page-messages'],
  list: (params) => ['page-messages', 'list', params],
  summary: ['page-messages', 'summary'],
  detail: (id) => ['page-messages', 'detail', id],
}
