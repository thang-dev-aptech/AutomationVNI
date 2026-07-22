import axiosInstance from '@/api/axiosInstance'

export const mediaFolderApi = {
  tree: () => axiosInstance.get('/api/MediaFolder/tree'),
  create: (payload) => axiosInstance.post('/api/MediaFolder', payload),
  update: (id, payload) => axiosInstance.put(`/api/MediaFolder/${id}`, payload),
  softDelete: (id) => axiosInstance.delete(`/api/MediaFolder/${id}`),
}

export const mediaFolderQueryKeys = {
  all: ['media-folders'],
  tree: ['media-folders', 'tree'],
}
