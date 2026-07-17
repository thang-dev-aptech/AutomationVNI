import { useNavigate, useLocation } from 'react-router-dom'
import { useAuthStore } from '@/modules/auth/stores/authStore'
import { useLogout } from '@/modules/auth/hooks/useAuth'
import './Topbar.css'

const PAGE_TITLES = {
  '/dashboard': 'Tổng quan',
  '/platforms': 'Platforms / Kênh',
  '/posts': 'Bài viết',
  '/media': 'Media',
  '/jobs': 'Jobs',
}

function resolveTitle(pathname) {
  return PAGE_TITLES[pathname] ?? 'VNI Automation'
}

export default function Topbar() {
  const navigate = useNavigate()
  const { pathname } = useLocation()
  const currentUser = useAuthStore((state) => state.currentUser)
  const logoutMutation = useLogout()

  const handleLogout = () => {
    logoutMutation.mutate(undefined, {
      onSettled: () => navigate('/login', { replace: true }),
    })
  }

  return (
    <header className="topbar">
      <h1 className="topbar-title">{resolveTitle(pathname)}</h1>
      <div className="topbar-actions">
        <span className="topbar-user">{currentUser?.email}</span>
        <button
          type="button"
          className="btn btn-secondary btn-sm"
          onClick={handleLogout}
          disabled={logoutMutation.isPending}
        >
          {logoutMutation.isPending ? 'Đang thoát...' : 'Đăng xuất'}
        </button>
      </div>
    </header>
  )
}
