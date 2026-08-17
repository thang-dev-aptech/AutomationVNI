import axiosInstance from '@/api/axiosInstance'

export const musicTrackApi = {
  getAll: () => axiosInstance.get('/api/MusicTrack'),
  upload: (formData) => axiosInstance.post('/api/MusicTrack/upload', formData, {
    headers: { 'Content-Type': 'multipart/form-data' },
  }),
  softDelete: (id) => axiosInstance.delete(`/api/MusicTrack/${id}`),
}

export const musicTrackQueryKeys = {
  all: ['music-tracks'],
}
