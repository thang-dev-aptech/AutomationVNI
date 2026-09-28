import axiosInstance from '@/api/axiosInstance'

export const googleDriveApi = {
  // Trạng thái bật/tắt nhập file từ Google Drive
  getPipelineState: () => axiosInstance.get('/api/GoogleDrive/pipeline-state'),
  setPipelineEnabled: (enabled) =>
    axiosInstance.post('/api/GoogleDrive/pipeline-state', { enabled }),
}

export const googleDriveQueryKeys = {
  all: ['google-drive'],
  pipelineState: () => ['google-drive', 'pipeline-state'],
}
