import axiosInstance from '@/api/axiosInstance'

export const generationJobApi = {
  getAll: () => axiosInstance.get('/api/GenerationJob'),
  getById: (id) => axiosInstance.get(`/api/GenerationJob/${id}`),
  filter: (params) => axiosInstance.post('/api/GenerationJob/filter', params),
  getByPost: (postId) => axiosInstance.get(`/api/GenerationJob/by-post/${postId}`),
  getPending: (batchSize = 20) =>
    axiosInstance.get('/api/GenerationJob/pending', { params: { batchSize } }),
  process: (id) => axiosInstance.post(`/api/GenerationJob/${id}/process`),
  retry: (id) => axiosInstance.post(`/api/GenerationJob/${id}/retry`),
  cancel: (id) => axiosInstance.post(`/api/GenerationJob/${id}/cancel`),
}

export const generationJobQueryKeys = {
  all: ['generation-jobs'],
  list: (params) => ['generation-jobs', 'list', params],
  detail: (id) => ['generation-jobs', 'detail', id],
  pending: (batchSize) => ['generation-jobs', 'pending', batchSize],
}
