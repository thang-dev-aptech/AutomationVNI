import './StatePanel.css'

export default function LoadingState({ message = 'Đang tải...' }) {
  return (
    <div className="state-panel">
      <div className="state-spinner" />
      <p>{message}</p>
    </div>
  )
}
