export default function StatusBadge({ label, tone = 'neutral' }) {
  return <span className={`badge badge-${tone}`}>{label}</span>
}
