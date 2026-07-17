import axiosInstance from '@/api/axiosInstance'

export const postMediaApi = {
  getByPost: (postId) => axiosInstance.get(`/api/PostMedia/by-post/${postId}`),
  create: (payload) => axiosInstance.post('/api/PostMedia', payload),
  update: (id, payload) => axiosInstance.put(`/api/PostMedia/${id}`, payload),
  softDelete: (id) => axiosInstance.delete(`/api/PostMedia/${id}`),
  filter: (params) => axiosInstance.post('/api/PostMedia/filter', params),
}

export const postMediaQueryKeys = {
  all: ['post-media'],
  byPost: (postId) => ['post-media', 'by-post', postId],
}
