import axiosInstance from '@/api/axiosInstance'

export const channelGroupApi = {
  getAll: () => axiosInstance.get('/api/ChannelGroup'),
  getById: (id) => axiosInstance.get(`/api/ChannelGroup/${id}`),
  filter: (params) => axiosInstance.post('/api/ChannelGroup/filter', params),
  create: (payload) => axiosInstance.post('/api/ChannelGroup', payload),
  update: (id, payload) => axiosInstance.put(`/api/ChannelGroup/${id}`, payload),
  softDelete: (id) => axiosInstance.delete(`/api/ChannelGroup/${id}`),

  downloadTemplate: () =>
    axiosInstance.get('/api/ChannelGroup/import/template', { responseType: 'blob' }),

  previewImport: (file, mode) => {
    const formData = new FormData()
    formData.append('file', file)
    formData.append('mode', mode)
    return axiosInstance.post('/api/ChannelGroup/import/preview', formData)
  },

  commitImport: (file, mode) => {
    const formData = new FormData()
    formData.append('file', file)
    formData.append('mode', mode)
    return axiosInstance.post('/api/ChannelGroup/import/commit', formData)
  },
}

/** Tải CSV mẫu (UTF-8 BOM) từ backend; trả blob để test/assert. */
export async function downloadChannelGroupImportTemplate() {
  const { data } = await channelGroupApi.downloadTemplate()
  const blob = data instanceof Blob
    ? data
    : new Blob([data], { type: 'text/csv;charset=utf-8' })
  const url = URL.createObjectURL(blob)
  const anchor = document.createElement('a')
  anchor.href = url
  anchor.download = 'nhom-kenh-mau.csv'
  document.body.appendChild(anchor)
  anchor.click()
  anchor.remove()
  URL.revokeObjectURL(url)
  return blob
}

export const channelGroupQueryKeys = {
  all: ['channel-groups'],
  list: (params) => ['channel-groups', 'list', params],
  detail: (id) => ['channel-groups', 'detail', id],
}
