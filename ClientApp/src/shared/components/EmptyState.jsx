export default function EmptyState({ message = 'Chưa có dữ liệu', action }) {
  return (
    <div className="state-panel">
      <p>{message}</p>
      {action}
    </div>
  )
}
