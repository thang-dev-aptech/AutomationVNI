import axiosInstance from '@/api/axiosInstance'

export const mediaCaptionJobQueryKeys = {
  all: ['media-caption-jobs'],
  list: ['media-caption-jobs', 'list'],
  detail: (id) => ['media-caption-jobs', 'detail', id],
}

export const mediaCaptionJobApi = {
  create: (folderId) => axiosInstance.post('/api/MediaCaptionJob', { folderId }),
  list: () => axiosInstance.get('/api/MediaCaptionJob'),
  get: (id) => axiosInstance.get(`/api/MediaCaptionJob/${id}`),
  retryFailed: (id) => axiosInstance.post(`/api/MediaCaptionJob/${id}/retry-failed`),
  retryItem: (itemId) => axiosInstance.post(`/api/MediaCaptionJob/items/${itemId}/retry`),
}
