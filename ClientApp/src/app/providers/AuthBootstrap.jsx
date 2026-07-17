import { useEffect } from 'react'
import LoadingState from '@/shared/components/LoadingState'
import { useAuthStore } from '@/modules/auth/stores/authStore'
import { useCurrentUser } from '@/modules/auth/hooks/useAuth'

/**
 * Xác thực phiên khi reload — gọi GET /api/Auth/me nếu có token.
 */
export default function AuthBootstrap({ children }) {
  const isAuthenticated = useAuthStore((state) => state.isAuthenticated)
  const clearAuth = useAuthStore((state) => state.clearAuth)
  const { isLoading, isError } = useCurrentUser()

  useEffect(() => {
    if (isError) {
      clearAuth()
    }
  }, [isError, clearAuth])

  if (isAuthenticated && isLoading) {
    return (
      <div style={{ minHeight: '100vh', display: 'grid', placeItems: 'center' }}>
        <LoadingState message="Đang xác thực phiên..." />
      </div>
    )
  }

  return children
}
