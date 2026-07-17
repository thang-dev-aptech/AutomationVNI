import axiosInstance from '@/api/axiosInstance'

export const publishLogApi = {
  getAll: () => axiosInstance.get('/api/PublishLog'),
  getById: (id) => axiosInstance.get(`/api/PublishLog/${id}`),
  filter: (params) => axiosInstance.post('/api/PublishLog/filter', params),
  getByPost: (postId) => axiosInstance.get(`/api/PublishLog/by-post/${postId}`),
}

export const publishLogQueryKeys = {
  all: ['publish-logs'],
  list: (params) => ['publish-logs', 'list', params],
  detail: (id) => ['publish-logs', 'detail', id],
  byPost: (postId) => ['publish-logs', 'by-post', postId],
}
