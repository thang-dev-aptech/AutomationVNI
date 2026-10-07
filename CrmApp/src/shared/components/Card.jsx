import React from 'react'

export const Card = ({ children, className = '', style = {}, title, subtitle, headerAction, ...props }) => {
  return (
    <div
      className={`crm-card ${className}`}
      style={{
        backgroundColor: 'var(--crm-surface)',
        borderRadius: 'var(--crm-radius-lg)',
        border: '1px solid var(--crm-border)',
        boxShadow: 'var(--crm-shadow-sm)',
        overflow: 'hidden',
        ...style,
      }}
      {...props}
    >
      {(title || subtitle || headerAction) && (
        <div
          style={{
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'space-between',
            padding: '16px 20px',
            borderBottom: '1px solid var(--crm-border)',
            gap: '12px',
          }}
        >
          <div>
            {title && (
              <h3 style={{ margin: 0, fontSize: '16px', fontWeight: '700', color: 'var(--crm-text)' }}>
                {title}
              </h3>
            )}
            {subtitle && (
              <p style={{ margin: '4px 0 0', fontSize: '13px', color: 'var(--crm-text-muted)' }}>
                {subtitle}
              </p>
            )}
          </div>
          {headerAction && <div>{headerAction}</div>}
        </div>
      )}
      <div style={{ padding: '20px' }}>{children}</div>
    </div>
  )
}

export default Card
