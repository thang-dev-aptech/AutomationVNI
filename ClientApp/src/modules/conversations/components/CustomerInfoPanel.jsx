import './CustomerInfoPanel.css'

export default function CustomerInfoPanel({
  kind,
  id,
  conversation,
  onClose,
}) {
  return (
    <div className="customer-info-panel" data-testid="customer-info-panel">
      <header className="info-header">
        <h3 className="info-title">Thông tin khách hàng</h3>
        <button
          type="button"
          className="info-close-btn"
          onClick={onClose}
          aria-label="Đóng thông tin khách hàng"
          data-testid="close-info-btn"
        >
          ×
        </button>
      </header>

      <div className="info-body" data-testid="customer-info-stub">
        <div className="info-stub-placeholder">
          <span>Khung thông tin khách hàng (t5 slot)</span>
          <small>Hội thoại {kind}: {id}</small>
        </div>
      </div>
    </div>
  )
}
