import axiosInstance from '@/api/axiosInstance'

export const channelGroupApi = {
  getAll: () => axiosInstance.get('/api/ChannelGroup'),
  getById: (id) => axiosInstance.get(`/api/ChannelGroup/${id}`),
  filter: (params) => axiosInstance.post('/api/ChannelGroup/filter', params),
  create: (payload) => axiosInstance.post('/api/ChannelGroup', payload),
  update: (id, payload) => axiosInstance.put(`/api/ChannelGroup/${id}`, payload),
  softDelete: (id) => axiosInstance.delete(`/api/ChannelGroup/${id}`),
}

export const channelGroupQueryKeys = {
  all: ['channel-groups'],
  list: (params) => ['channel-groups', 'list', params],
  detail: (id) => ['channel-groups', 'detail', id],
}
