export default function ErrorState({ message, onRetry }) {
  return (
    <div className="state-panel state-panel--error">
      <p>{message || 'Không thể tải dữ liệu'}</p>
      {onRetry && (
        <button type="button" className="btn btn-secondary" onClick={onRetry}>
          Thử lại
        </button>
      )}
    </div>
  )
}
