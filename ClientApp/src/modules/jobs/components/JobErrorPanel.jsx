import Modal from '@/shared/components/Modal'
import { truncateText } from '../constants/jobConstants'

export default function JobErrorPanel({ open, title, errorCode, errorMessage, onClose }) {
  if (!open) return null

  return (
    <Modal
      open={open}
      title={title || 'Chi tiết lỗi'}
      onClose={onClose}
      footer={(
        <button type="button" className="btn btn-secondary" onClick={onClose}>
          Đóng
        </button>
      )}
    >
      {errorCode && (
        <p style={{ margin: '0 0 8px' }}>
          <strong>Error code:</strong> {errorCode}
        </p>
      )}
      <p style={{ margin: 0, whiteSpace: 'pre-wrap', wordBreak: 'break-word' }}>
        {truncateText(errorMessage, 2000) || 'Không có thông tin lỗi.'}
      </p>
    </Modal>
  )
}
