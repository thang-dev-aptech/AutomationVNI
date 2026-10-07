import axiosInstance from '@/api/axiosInstance'

export const campaignApi = {
  getSummaries: () => axiosInstance.get('/api/Campaign/summaries'),
  getById: (id) => axiosInstance.get(`/api/Campaign/${id}`),
  getDetail: (id) => axiosInstance.get(`/api/Campaign/${id}/detail`),
  listPagePosts: (id, channelId, params) =>
    axiosInstance.get(`/api/Campaign/${id}/pages/${channelId}/posts`, { params }),
  create: (payload) => axiosInstance.post('/api/Campaign', payload),
  update: (id, payload) => axiosInstance.put(`/api/Campaign/${id}`, payload),
  pause: (id) => axiosInstance.post(`/api/Campaign/${id}/pause`),
  resume: (id) => axiosInstance.post(`/api/Campaign/${id}/resume`),
  end: (id) => axiosInstance.post(`/api/Campaign/${id}/end`),
  softDelete: (id) => axiosInstance.delete(`/api/Campaign/${id}`),
}

export const campaignQueryKeys = {
  all: ['campaigns'],
  summaries: ['campaigns', 'summaries'],
  detail: (id) => ['campaigns', 'detail', id],
  pagePosts: (id, channelId, params) => ['campaigns', 'page-posts', id, channelId, params],
}
