import axiosInstance from '@/api/axiosInstance'

export const postApi = {
  getAll: () => axiosInstance.get('/api/Post'),
  getById: (id) => axiosInstance.get(`/api/Post/${id}`),
  filter: (params) => axiosInstance.post('/api/Post/filter', params),
  create: (payload) => axiosInstance.post('/api/Post', payload),
  update: (id, payload) => axiosInstance.put(`/api/Post/${id}`, payload),
  softDelete: (id) => axiosInstance.delete(`/api/Post/${id}`),

  submitReview: (id) => axiosInstance.post(`/api/Post/${id}/submit-review`),
  approve: (id) => axiosInstance.post(`/api/Post/${id}/approve`),
  reject: (id, payload) => axiosInstance.post(`/api/Post/${id}/reject`, payload),
  schedule: (id, payload) => axiosInstance.post(`/api/Post/${id}/schedule`, payload),
  cancelSchedule: (id) => axiosInstance.post(`/api/Post/${id}/cancel-schedule`),
  publishNow: (id) => axiosInstance.post(`/api/Post/${id}/publish-now`),

  queueTextGeneration: (id) => axiosInstance.post(`/api/Post/${id}/queue-text-generation`),
  queueImageGeneration: (id) => axiosInstance.post(`/api/Post/${id}/queue-image-generation`),
  queueImageRender: (id) => axiosInstance.post(`/api/Post/${id}/queue-image-render`),

  createAndGenerate: (payload) => axiosInstance.post('/api/Post/create-and-generate', payload),
  regenerateText: (id) => axiosInstance.post(`/api/Post/${id}/regenerate-text`),
  regenerateImage: (id) => axiosInstance.post(`/api/Post/${id}/regenerate-image`),

  getGenerationStatus: (id) => axiosInstance.get(`/api/Post/${id}/generation-status`),
  getTimeline: (id) => axiosInstance.get(`/api/Post/${id}/timeline`),
}

export const postQueryKeys = {
  all: ['posts'],
  list: (params) => ['posts', 'list', params],
  detail: (id) => ['posts', 'detail', id],
  generationStatus: (id) => ['posts', 'generation-status', id],
  timeline: (id) => ['posts', 'timeline', id],
}
