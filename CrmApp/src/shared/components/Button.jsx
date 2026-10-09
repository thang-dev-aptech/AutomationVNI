import React from 'react'

export const Button = ({
  children,
  variant = 'primary',
  size = 'md',
  disabled = false,
  isLoading = false,
  icon = null,
  className = '',
  style = {},
  ...props
}) => {
  const baseStyle = {
    display: 'inline-flex',
    alignItems: 'center',
    justifyContent: 'center',
    gap: '8px',
    borderRadius: 'var(--crm-radius-md)',
    fontWeight: '600',
    border: '1px solid transparent',
    transition: 'all var(--crm-transition-fast)',
    outline: 'none',
    userSelect: 'none',
    opacity: disabled || isLoading ? 0.6 : 1,
    cursor: disabled || isLoading ? 'not-allowed' : 'pointer',
    ...style,
  }

  const variantStyles = {
    primary: {
      backgroundColor: 'var(--crm-primary)',
      color: '#ffffff',
      boxShadow: '0 2px 4px rgba(79, 70, 229, 0.25)',
    },
    secondary: {
      backgroundColor: 'var(--crm-surface-subtle)',
      color: 'var(--crm-text)',
      borderColor: 'var(--crm-border)',
    },
    outline: {
      backgroundColor: 'transparent',
      color: 'var(--crm-primary)',
      borderColor: 'var(--crm-primary)',
    },
    danger: {
      backgroundColor: 'var(--crm-danger)',
      color: '#ffffff',
      boxShadow: '0 2px 4px rgba(239, 68, 68, 0.25)',
    },
    ghost: {
      backgroundColor: 'transparent',
      color: 'var(--crm-text-muted)',
    },
  }

  const sizeStyles = {
    sm: { padding: '6px 12px', fontSize: '13px', minHeight: '32px' },
    md: { padding: '9px 16px', fontSize: '14px', minHeight: '40px' },
    lg: { padding: '12px 20px', fontSize: '15px', minHeight: '46px' },
  }

  return (
    <button
      className={`crm-btn crm-btn-${variant} ${className}`}
      disabled={disabled || isLoading}
      style={{
        ...baseStyle,
        ...variantStyles[variant],
        ...sizeStyles[size],
      }}
      {...props}
    >
      {isLoading ? (
        <span
          style={{
            width: 14,
            height: 14,
            border: '2px solid currentColor',
            borderTopColor: 'transparent',
            borderRadius: '50%',
            animation: 'spin 0.6s linear infinite',
            display: 'inline-block',
          }}
        />
      ) : (
        icon
      )}
      {children}
    </button>
  )
}

export default Button
