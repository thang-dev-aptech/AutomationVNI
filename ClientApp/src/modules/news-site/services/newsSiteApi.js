import axiosInstance from '@/api/axiosInstance'

export const newsSiteApi = {
  /** Bài đã lên website. category rỗng = tất cả chuyên mục. */
  list: (params) => axiosInstance.get('/api/NewsSite', { params }),

  /** Thư mục xuất bản có ghi được không — kiểm trước khi đi tìm lỗi ở chỗ khác. */
  status: () => axiosInstance.get('/api/NewsSite/status'),

  /** CỬA 2 — đăng một bài đã lên web sang các fanpage đã chọn. */
  toFanpage: (id, payload) => axiosInstance.post(`/api/NewsSite/${id}/fanpage`, payload),

  /** Gỡ bài khỏi web. Giữ file trên đĩa nên link đã chia sẻ không chết. */
  unpublish: (id) => axiosInstance.delete(`/api/NewsSite/${id}`),

  /** Dựng lại toàn bộ HTML tĩnh. Dùng sau khi sửa giao diện hoặc khi nghi trang lệch. */
  build: () => axiosInstance.post('/api/NewsSite/build', {}),

  /** Danh sách người đăng ký nhận tin (cả đã huỷ, để admin nhìn được toàn cảnh). */
  listSubscribers: (params) => axiosInstance.get('/api/NewsSite/subscribers', { params }),

  /** Bật/tắt 1 người đăng ký theo Id — khác luồng độc giả tự huỷ bằng token trong email. */
  setSubscriberActive: (id, isActive) =>
    axiosInstance.patch(`/api/NewsSite/subscribers/${id}`, { isActive }),
}

export const newsSiteQueryKeys = {
  all: ['news-site'],
  list: (params) => ['news-site', 'list', params],
  status: () => ['news-site', 'status'],
  subscribers: (params) => ['news-site', 'subscribers', params],
}
