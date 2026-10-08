import axiosInstance from '@/api/axiosInstance'

export const inboxApi = {
  filter: (params) => axiosInstance.post('/api/Inbox/filter', params),
  summary: () => axiosInstance.get('/api/Inbox/summary'),
  getProfile: (kind, id) => axiosInstance.get(`/api/Inbox/${kind}/${id}/profile`),
  suggestReply: (kind, id) => axiosInstance.post(`/api/Inbox/${kind}/${id}/suggest-reply`),
}

export const inboxQueryKeys = {
  all: ['inbox'],
  list: (params) => ['inbox', 'list', params],
  summary: ['inbox', 'summary'],
  profile: (kind, id) => ['inbox', 'profile', kind, id],
}
