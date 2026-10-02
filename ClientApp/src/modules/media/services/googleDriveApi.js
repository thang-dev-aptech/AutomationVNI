import axiosInstance from '@/api/axiosInstance'

export const googleDriveApi = {
  // Trạng thái bật/tắt nhập file từ Google Drive
  getPipelineState: () => axiosInstance.get('/api/GoogleDrive/pipeline-state'),
  setPipelineEnabled: (enabled) =>
    axiosInstance.post('/api/GoogleDrive/pipeline-state', { enabled }),
  // GDRIVE-03: quét thủ công ngay (tôn trọng IsEnabled phía server)
  scanNow: () => axiosInstance.post('/api/GoogleDrive/scan-now'),
}

export const googleDriveQueryKeys = {
  all: ['google-drive'],
  pipelineState: () => ['google-drive', 'pipeline-state'],
}
