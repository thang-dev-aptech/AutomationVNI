import React from 'react'

export const formatCurrencyVnd = (amount) => {
  if (amount == null || isNaN(amount)) return '0 ₫'
  try {
    return new Intl.NumberFormat('vi-VN', {
      style: 'currency',
      currency: 'VND',
      maximumFractionDigits: 0,
    }).format(amount)
  } catch {
    return `${Number(amount).toLocaleString('vi-VN')} ₫`
  }
}

export const OpportunityStatsBar = ({ stats = null, loading = false }) => {
  if (loading && !stats) {
    return (
      <div className="crm-opp-stats-bar" data-testid="opportunity-stats-skeleton">
        <div className="crm-opp-stat-card crm-opp-stat-card--skeleton" />
        <div className="crm-opp-stat-card crm-opp-stat-card--skeleton" />
        <div className="crm-opp-stat-card crm-opp-stat-card--skeleton" />
        <div className="crm-opp-stat-card crm-opp-stat-card--skeleton" />
        <div className="crm-opp-stat-card crm-opp-stat-card--skeleton" />
        <div className="crm-opp-stat-card crm-opp-stat-card--skeleton" />
      </div>
    )
  }

  const s = stats || { total: 0, open: 0, won: 0, lost: 0, activity: 0, rev: 0 }

  return (
    <div className="crm-opp-stats-bar" data-testid="opportunity-stats-bar">
      <div className="crm-opp-stat-card crm-opp-stat-card--total" data-testid="stat-total">
        <span className="crm-opp-stat-lbl">Tổng cơ hội</span>
        <strong className="crm-opp-stat-val" data-testid="stat-total-val">{s.total ?? 0}</strong>
      </div>

      <div className="crm-opp-stat-card crm-opp-stat-card--open" data-testid="stat-open">
        <span className="crm-opp-stat-lbl">Đang mở</span>
        <strong className="crm-opp-stat-val" data-testid="stat-open-val">{s.open ?? 0}</strong>
      </div>

      <div className="crm-opp-stat-card crm-opp-stat-card--won" data-testid="stat-won">
        <span className="crm-opp-stat-lbl">Thắng (Won)</span>
        <strong className="crm-opp-stat-val" data-testid="stat-won-val">{s.won ?? 0}</strong>
      </div>

      <div className="crm-opp-stat-card crm-opp-stat-card--lost" data-testid="stat-lost">
        <span className="crm-opp-stat-lbl">Thất bại (Lost)</span>
        <strong className="crm-opp-stat-val" data-testid="stat-lost-val">{s.lost ?? 0}</strong>
      </div>

      <div className="crm-opp-stat-card crm-opp-stat-card--activity" data-testid="stat-activity" title="Ghi chú gắn cơ hội">
        <span className="crm-opp-stat-lbl">Hoạt động</span>
        <strong className="crm-opp-stat-val" data-testid="stat-activity-val">{s.activity ?? 0}</strong>
      </div>

      <div className="crm-opp-stat-card crm-opp-stat-card--rev" data-testid="stat-rev">
        <span className="crm-opp-stat-lbl">Doanh thu chốt</span>
        <strong className="crm-opp-stat-val" data-testid="stat-rev-val">{formatCurrencyVnd(s.rev ?? 0)}</strong>
      </div>
    </div>
  )
}

export default OpportunityStatsBar
