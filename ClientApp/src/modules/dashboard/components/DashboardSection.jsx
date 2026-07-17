import './DashboardComponents.css'

export default function DashboardSection({ title, description, action, children }) {
  return (
    <section className="dashboard-section card">
      <div className="dashboard-section-header">
        <div>
          <h2 className="dashboard-section-title">{title}</h2>
          {description && (
            <p className="dashboard-section-description">{description}</p>
          )}
        </div>
        {action}
      </div>
      <div className="dashboard-section-body">{children}</div>
    </section>
  )
}
