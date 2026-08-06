import axiosInstance from '@/api/axiosInstance'

export const notificationApi = {
  getFeed: (size = 30) => axiosInstance.get('/api/Notification', { params: { size } }),
  markAllRead: () => axiosInstance.post('/api/Notification/read-all', {}),
}

export const notificationQueryKeys = {
  all: ['notifications'],
  feed: (size) => ['notifications', 'feed', size],
}
