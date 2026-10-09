import React from 'react'

export const Badge = ({ children, variant = 'default', size = 'sm', className = '', ...props }) => {
  const variantStyles = {
    default: {
      backgroundColor: 'var(--crm-surface-subtle)',
      color: 'var(--crm-text)',
      border: '1px solid var(--crm-border)',
    },
    primary: {
      backgroundColor: 'var(--crm-primary-light)',
      color: 'var(--crm-primary-text)',
      border: '1px solid rgba(79, 70, 229, 0.2)',
    },
    success: {
      backgroundColor: 'var(--crm-success-light)',
      color: 'var(--crm-success-text)',
      border: '1px solid rgba(16, 185, 129, 0.2)',
    },
    warning: {
      backgroundColor: 'var(--crm-warning-light)',
      color: 'var(--crm-warning-text)',
      border: '1px solid rgba(245, 158, 11, 0.2)',
    },
    danger: {
      backgroundColor: 'var(--crm-danger-light)',
      color: 'var(--crm-danger-text)',
      border: '1px solid rgba(239, 68, 68, 0.2)',
    },
    info: {
      backgroundColor: 'var(--crm-info-light)',
      color: 'var(--crm-info-text)',
      border: '1px solid rgba(59, 130, 246, 0.2)',
    },
  }

  const sizeStyles = {
    sm: { padding: '2px 8px', fontSize: '11px', fontWeight: '600' },
    md: { padding: '4px 12px', fontSize: '12px', fontWeight: '600' },
  }

  return (
    <span
      className={`crm-badge ${className}`}
      style={{
        display: 'inline-flex',
        alignItems: 'center',
        gap: '4px',
        borderRadius: 'var(--crm-radius-full)',
        lineHeight: 1.2,
        ...variantStyles[variant],
        ...sizeStyles[size],
      }}
      {...props}
    >
      {children}
    </span>
  )
}

export default Badge
