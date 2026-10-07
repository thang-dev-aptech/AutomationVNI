import { useAuthStore } from './authStore'

export const useAuth = () => {
  const {
    token,
    user,
    isAuthenticated,
    isLoading,
    error,
    login,
    logout,
    clearAuth,
    setAuth,
  } = useAuthStore()

  const roles = user?.roles || []

  // Check roles: Admin, ContentManager, Reviewer, Viewer
  const isAdmin = roles.includes('Admin')
  const isContentManager = roles.includes('ContentManager')
  const isManager = isAdmin || isContentManager // Full management
  const isReviewer = roles.includes('Reviewer') // Customer care
  const isViewer = roles.includes('Viewer') // Read-only

  // Business capabilities according to invariant crm-auth-roles:
  // Admin/ContentManager = Quản lý (Tag config, Tự chia, Nhập CSV, Xuất, Xoá)
  const canManage = isManager

  // Reviewer = Chăm sóc khách (Trả lời, Ghi chú, Trạng thái, Nhắc việc, Tạo khách tay, Gộp hồ sơ)
  // Admin/ContentManager also have all care rights
  const canCare = isManager || isReviewer

  // Viewer = Chỉ đọc (mọi nút thao tác ghi/nhập/xuất bị ẩn)
  const isReadOnly = isViewer && !canCare

  const hasRole = (requiredRoles) => {
    if (!requiredRoles || requiredRoles.length === 0) return true
    if (typeof requiredRoles === 'string') return roles.includes(requiredRoles)
    return requiredRoles.some((r) => roles.includes(r))
  }

  return {
    token,
    user,
    roles,
    isAuthenticated,
    isLoading,
    error,
    login,
    logout,
    clearAuth,
    setAuth,
    isAdmin,
    isContentManager,
    isManager,
    isReviewer,
    isViewer,
    canManage,
    canCare,
    isReadOnly,
    hasRole,
  }
}

export default useAuth
