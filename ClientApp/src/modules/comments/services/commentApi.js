import axiosInstance from '@/api/axiosInstance'

export const commentApi = {
  filter: (params) => axiosInstance.post('/api/SocialComment/filter', params),
  summary: () => axiosInstance.get('/api/SocialComment/summary'),
  getThread: (id) => axiosInstance.get(`/api/SocialComment/${id}`),
  actions: (id) => axiosInstance.get(`/api/SocialComment/${id}/actions`),
  sync: (payload) => axiosInstance.post('/api/SocialComment/sync', payload),
  subscribeFacebook: () => axiosInstance.post('/api/SocialComment/subscribe-facebook'),
  reply: (id, message) => axiosInstance.post(`/api/SocialComment/${id}/reply`, { message }),
  hide: (id) => axiosInstance.post(`/api/SocialComment/${id}/hide`),
  unhide: (id) => axiosInstance.post(`/api/SocialComment/${id}/unhide`),
  remove: (id) => axiosInstance.delete(`/api/SocialComment/${id}`),
  pending: (id, approve) => axiosInstance.post(`/api/SocialComment/${id}/pending?approve=${approve}`),
  setStatus: (id, status) => axiosInstance.post(`/api/SocialComment/${id}/status`, { status }),
  assign: (id, assignedTo) => axiosInstance.post(`/api/SocialComment/${id}/assign`, { assignedTo }),
  note: (id, note) => axiosInstance.post(`/api/SocialComment/${id}/note`, { note }),
}

export const commentQueryKeys = {
  all: ['social-comments'],
  list: (params) => ['social-comments', 'list', params],
  summary: ['social-comments', 'summary'],
  thread: (id) => ['social-comments', 'thread', id],
  actions: (id) => ['social-comments', 'actions', id],
}
