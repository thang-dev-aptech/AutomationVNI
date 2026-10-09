import React, { useState, useEffect } from 'react'

export const LostReasonDialog = ({
  isOpen = false,
  opportunity = null,
  targetStage = null,
  onConfirm,
  onCancel,
}) => {
  const [reason, setReason] = useState('')
  const [error, setError] = useState(null)

  useEffect(() => {
    if (isOpen) {
      setReason('')
      setError(null)
    }
  }, [isOpen])

  if (!isOpen || !opportunity) return null

  const handleSubmit = (e) => {
    e.preventDefault()
    if (!reason.trim()) {
      setError('Vui lòng nhập lý do thất bại')
      return
    }
    onConfirm?.(reason.trim())
  }

  return (
    <div
      className="crm-opp-modal-backdrop"
      role="dialog"
      aria-modal="true"
      data-testid="lost-reason-dialog"
    >
      <div className="crm-opp-modal-content crm-opp-modal-content--sm">
        <div className="crm-opp-modal-header">
          <h3 className="crm-opp-modal-title">Lý do cơ hội thất bại</h3>
          <button
            type="button"
            className="crm-opp-modal-close"
            onClick={onCancel}
            data-testid="btn-close-lost-reason-dialog"
          >
            ✕
          </button>
        </div>

        <form onSubmit={handleSubmit}>
          <div className="crm-opp-modal-body">
            {error && (
              <div className="crm-opp-form-error" data-testid="lost-reason-error">
                ⚠️ {error}
              </div>
            )}

            <p className="crm-opp-dialog-desc">
              Cơ hội <strong>"{opportunity.title}"</strong> được chuyển sang giai đoạn{' '}
              <strong style={{ color: targetStage?.color || '#ef4444' }}>
                {targetStage?.name || 'Thất bại (Lost)'}
              </strong>
              . Vui lòng ghi lại nguyên nhân thất bại để cải thiện chất lượng tư vấn sau này.
            </p>

            <div className="crm-opp-form-group">
              <label className="crm-opp-form-label" htmlFor="lost-reason-text">
                Lý do thất bại <span className="crm-opp-req">*</span>
              </label>
              <textarea
                id="lost-reason-text"
                className="crm-opp-form-control"
                rows="3"
                placeholder="Ví dụ: Giá cao hơn đối thủ, khách không có nhu cầu, không liên hệ được..."
                value={reason}
                onChange={(e) => setReason(e.target.value)}
                data-testid="input-lost-reason"
                autoFocus
                required
              />
            </div>
          </div>

          <div className="crm-opp-modal-footer">
            <button
              type="button"
              className="crm-opp-btn crm-opp-btn--secondary"
              onClick={onCancel}
              data-testid="btn-cancel-lost-reason"
            >
              Huỷ bỏ
            </button>
            <button
              type="submit"
              className="crm-opp-btn crm-opp-btn--danger"
              data-testid="btn-confirm-lost-reason"
            >
              Xác nhận Thất bại
            </button>
          </div>
        </form>
      </div>
    </div>
  )
}

export default LostReasonDialog
