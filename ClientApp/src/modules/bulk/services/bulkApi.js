import axiosInstance from '@/api/axiosInstance'

export const bulkApi = {
  create: (payload) => axiosInstance.post('/api/Post/bulk-create', payload),
  approve: (payload) => axiosInstance.post('/api/Post/bulk-approve', payload),
  schedule: (payload) => axiosInstance.post('/api/Post/bulk-schedule', payload),
  getBatch: (batchId) => axiosInstance.get(`/api/Post/batch/${batchId}`),
  suggestIdeas: (payload) => axiosInstance.post('/api/ai/suggest-ideas', payload),
}

export const bulkQueryKeys = {
  batch: (batchId) => ['bulk', 'batch', batchId],
}
