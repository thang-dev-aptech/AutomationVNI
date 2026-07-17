import { Link } from 'react-router-dom'
import './DashboardComponents.css'

export default function StatCard({
  label,
  value,
  hint,
  tone = 'neutral',
  to,
  emphasized = false,
}) {
  const displayValue = value === null || value === undefined ? '—' : value

  const content = (
  <>
    <div className="stat-card-label">{label}</div>
    <div className={`stat-card-value stat-card-value--${tone}${emphasized ? ' stat-card-value--emphasized' : ''}`}>
      {displayValue}
    </div>
    {hint && <div className="stat-card-hint">{hint}</div>}
  </>
  )

  if (to) {
    return (
      <Link to={to} className={`stat-card card stat-card--link stat-card--${tone}`}>
        {content}
      </Link>
    )
  }

  return (
    <article className={`stat-card card stat-card--${tone}`}>
      {content}
    </article>
  )
}
