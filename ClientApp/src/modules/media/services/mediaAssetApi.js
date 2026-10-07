import axiosInstance from '@/api/axiosInstance'

export const mediaAssetApi = {
  getAll: () => axiosInstance.get('/api/MediaAsset'),
  getById: (id) => axiosInstance.get(`/api/MediaAsset/${id}`),
  filter: (params) => axiosInstance.post('/api/MediaAsset/filter', params),
  create: (payload) => axiosInstance.post('/api/MediaAsset', payload),
  update: (id, payload) => axiosInstance.put(`/api/MediaAsset/${id}`, payload),
  softDelete: (id) => axiosInstance.delete(`/api/MediaAsset/${id}`),
  upload: (formData) => axiosInstance.post('/api/MediaAsset/upload', formData, {
    headers: { 'Content-Type': 'multipart/form-data' },
  }),
  uploadBatch: (formData) => axiosInstance.post('/api/MediaAsset/upload-batch', formData, {
    headers: { 'Content-Type': 'multipart/form-data' },
  }),
  move: (ids, folderId) =>
    axiosInstance.post('/api/MediaAsset/move', { ids, folderId: folderId ?? null }),
  analyze: (id) => axiosInstance.post(`/api/MediaAsset/${id}/analyze`),
  analyzeAll: (force = false) =>
    axiosInstance.post(`/api/MediaAsset/analyze-all?force=${force}`),
  analyzeLayoutFolder: (folderId) => axiosInstance.post(`/api/MediaAsset/analyze-layout-folder/${folderId}`),
  analyzeLayout: (id) => axiosInstance.post(`/api/MediaAsset/${id}/analyze-layout`),
  // socialChannelId (tuỳ chọn, query string): Page có PageContext dùng cho bài viết. Không truyền ⇒ backend dùng Page của thư mục.
  generateCaption: (id, socialChannelId) => axiosInstance.post(
    `/api/MediaAsset/${id}/generate-caption`,
    null,
    socialChannelId ? { params: { socialChannelId } } : undefined,
  ),
  recommend: (payload) => axiosInstance.post('/api/MediaAsset/recommend', payload),
}

export const mediaAssetQueryKeys = {
  all: ['media-assets'],
  list: (params) => ['media-assets', 'list', params],
  detail: (id) => ['media-assets', 'detail', id],
  recommend: (payload) => ['media-assets', 'recommend', payload],
}
