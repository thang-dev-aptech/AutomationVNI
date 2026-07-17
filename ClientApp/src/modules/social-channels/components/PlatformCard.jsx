import './PlatformCard.css'

export default function PlatformCard({
  label,
  description,
  channelCount,
  supported,
  selected,
  onClick,
}) {
  return (
    <button
      type="button"
      className={`platform-card card${selected ? ' platform-card--selected' : ''}${!supported ? ' platform-card--disabled' : ''}`}
      onClick={onClick}
      disabled={!supported}
    >
      <div className="platform-card-label">{label}</div>
      <div className="platform-card-desc">{description}</div>
      <div className="platform-card-meta">
        {supported ? (
          <span>{channelCount} kênh đã kết nối</span>
        ) : (
          <span className="platform-card-soon">Sắp hỗ trợ</span>
        )}
      </div>
    </button>
  )
}
