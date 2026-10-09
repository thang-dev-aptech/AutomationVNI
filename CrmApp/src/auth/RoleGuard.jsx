import React from 'react'
import { useAuth } from './useAuth'

export const RoleGuard = ({
  roles,
  canManageOnly = false,
  canCareOnly = false,
  readOnlyHidden = true,
  fallback = null,
  children,
}) => {
  const { hasRole, canManage, canCare, isReadOnly } = useAuth()

  if (readOnlyHidden && isReadOnly) {
    return fallback
  }

  if (canManageOnly && !canManage) {
    return fallback
  }

  if (canCareOnly && !canCare) {
    return fallback
  }

  if (roles && !hasRole(roles)) {
    return fallback
  }

  return <>{children}</>
}

export default RoleGuard
