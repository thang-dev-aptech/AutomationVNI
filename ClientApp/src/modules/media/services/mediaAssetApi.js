import axiosInstance from '@/api/axiosInstance'

export const mediaAssetApi = {
  getAll: () => axiosInstance.get('/api/MediaAsset'),
  getById: (id) => axiosInstance.get(`/api/MediaAsset/${id}`),
  filter: (params) => axiosInstance.post('/api/MediaAsset/filter', params),
  create: (payload) => axiosInstance.post('/api/MediaAsset', payload),
  update: (id, payload) => axiosInstance.put(`/api/MediaAsset/${id}`, payload),
  softDelete: (id) => axiosInstance.delete(`/api/MediaAsset/${id}`),
}

export const mediaAssetQueryKeys = {
  all: ['media-assets'],
  list: (params) => ['media-assets', 'list', params],
  detail: (id) => ['media-assets', 'detail', id],
}
