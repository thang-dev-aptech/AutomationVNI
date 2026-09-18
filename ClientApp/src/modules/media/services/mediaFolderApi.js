import axiosInstance from '@/api/axiosInstance'

const ROOT_PARENT = 'root'

export const mediaFolderApi = {
  tree: () => axiosInstance.get('/api/MediaFolder/tree'),
  pageRoots: ({ index = 1, size = 20 } = {}) =>
    axiosInstance.get('/api/MediaFolder/page-roots', {
      params: { index, size },
    }),
  children: ({
    socialChannelId,
    parentFolderId = null,
    index = 1,
    size = 20,
    sortBy = 'sortOrder',
    sortDirection = 'asc',
  }) =>
    axiosInstance.get('/api/MediaFolder/children', {
      params: {
        socialChannelId,
        index,
        size,
        sortBy,
        sortDirection,
        ...(parentFolderId ? { parentFolderId } : {}),
      },
    }),
  breadcrumb: ({ socialChannelId, folderId }) =>
    axiosInstance.get('/api/MediaFolder/breadcrumb', {
      params: { socialChannelId, folderId },
    }),
  search: ({
    socialChannelId,
    keyword,
    index = 1,
    size = 20,
    sortBy = 'name',
    sortDirection = 'asc',
  }) =>
    axiosInstance.get('/api/MediaFolder/search', {
      params: { socialChannelId, keyword, index, size, sortBy, sortDirection },
    }),
  filter: (params) => axiosInstance.post('/api/MediaFolder/filter', params),
  writablePages: () => axiosInstance.get('/api/MediaFolder/writable-pages'),
  bulkCreate: (payload) => axiosInstance.post('/api/MediaFolder/bulk', payload),
  createAcrossPages: (payload) => axiosInstance.post('/api/MediaFolder/create-across-pages', payload),
  create: (payload) => axiosInstance.post('/api/MediaFolder', payload),
  update: (id, payload) => axiosInstance.put(`/api/MediaFolder/${id}`, payload),
  softDelete: (id) => axiosInstance.delete(`/api/MediaFolder/${id}`),
}

export const mediaFolderQueryKeys = {
  all: ['media-folders'],
  tree: ['media-folders', 'tree'],
  writablePages: ['media-folders', 'writable-pages'],
  pageRoots: (index = 1, size = 20) => [
    'media-folders',
    'page-roots',
    index,
    size,
  ],
  children: (socialChannelId, parentFolderId, index = 1, size = 20) => [
    'media-folders',
    'children',
    socialChannelId ?? 'none',
    parentFolderId ?? ROOT_PARENT,
    index,
    size,
  ],
  breadcrumb: (socialChannelId, folderId) => [
    'media-folders',
    'breadcrumb',
    socialChannelId ?? 'none',
    folderId ?? 'none',
  ],
  search: (socialChannelId, keyword, index = 1, size = 20) => [
    'media-folders',
    'search',
    socialChannelId ?? 'none',
    keyword ?? '',
    index,
    size,
  ],
}

export function isExplorerCacheKeyForOtherPage(queryKey, socialChannelId) {
  if (!Array.isArray(queryKey) || queryKey[0] !== 'media-folders') return false
  if (queryKey[1] !== 'children' && queryKey[1] !== 'breadcrumb') return false
  return queryKey[2] !== (socialChannelId ?? 'none') && queryKey[2] !== socialChannelId
}
