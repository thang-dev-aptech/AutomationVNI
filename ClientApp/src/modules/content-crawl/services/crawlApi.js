import axiosInstance from '@/api/axiosInstance'

export const crawlApi = {
  // Nguồn cào
  getSources: (onlyActive = false) =>
    axiosInstance.get('/api/ContentCrawl/sources', { params: { onlyActive } }),
  createSource: (payload) => axiosInstance.post('/api/ContentCrawl/sources', payload),
  updateSource: (id, payload) => axiosInstance.put(`/api/ContentCrawl/sources/${id}`, payload),
  deleteSource: (id) => axiosInstance.delete(`/api/ContentCrawl/sources/${id}`),
  /** Đọc thử feed, KHÔNG ghi gì vào DB — dùng để debug một nguồn trước khi thêm. */
  testSource: (payload) => axiosInstance.post('/api/ContentCrawl/sources/test', payload),
  crawlNow: (id) => axiosInstance.post(`/api/ContentCrawl/sources/${id}/crawl-now`, {}),
  facebookStatus: () => axiosInstance.get('/api/ContentCrawl/openclaw/facebook-status'),

  // Lượt cào
  getRuns: (sourceId, take = 30) =>
    axiosInstance.get('/api/ContentCrawl/runs', { params: { sourceId, take } }),

  // Tin đã cào
  filterArticles: (params) => axiosInstance.post('/api/ContentCrawl/articles/filter', params),
  getArticle: (id) => axiosInstance.get(`/api/ContentCrawl/articles/${id}`),
  getSummary: () => axiosInstance.get('/api/ContentCrawl/articles/summary'),
  approve: (id, payload) => axiosInstance.post(`/api/ContentCrawl/articles/${id}/approve`, payload),
  reject: (id, payload) => axiosInstance.post(`/api/ContentCrawl/articles/${id}/reject`, payload),
  notDuplicate: (id) => axiosInstance.post(`/api/ContentCrawl/articles/${id}/not-duplicate`),
  rededup: (id) => axiosInstance.post(`/api/ContentCrawl/articles/${id}/rededup`),
}

export const crawlQueryKeys = {
  all: ['content-crawl'],
  sources: (onlyActive) => ['content-crawl', 'sources', onlyActive],
  runs: (sourceId) => ['content-crawl', 'runs', sourceId],
  articles: (params) => ['content-crawl', 'articles', params],
  article: (id) => ['content-crawl', 'article', id],
  summary: () => ['content-crawl', 'summary'],
  facebookStatus: () => ['content-crawl', 'facebook-status'],
}
