import axiosInstance from '@/api/axiosInstance'

export const newsSiteApi = {
  /** Bài đã lên website. category rỗng = tất cả chuyên mục. */
  list: (params) => axiosInstance.get('/api/NewsSite', { params }),

  /** Thư mục xuất bản có ghi được không — kiểm trước khi đi tìm lỗi ở chỗ khác. */
  status: () => axiosInstance.get('/api/NewsSite/status'),

  /** CỬA 2 — đăng một bài đã lên web sang các fanpage đã chọn. */
  toFanpage: (id, payload) => axiosInstance.post(`/api/NewsSite/${id}/fanpage`, payload),

  /** Dựng lại toàn bộ HTML tĩnh. Dùng sau khi sửa giao diện hoặc khi nghi trang lệch. */
  build: () => axiosInstance.post('/api/NewsSite/build', {}),
}

export const newsSiteQueryKeys = {
  all: ['news-site'],
  list: (params) => ['news-site', 'list', params],
  status: () => ['news-site', 'status'],
}
