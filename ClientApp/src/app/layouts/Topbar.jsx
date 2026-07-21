import { useNavigate, useLocation } from 'react-router-dom'
import { useAuthStore } from '@/modules/auth/stores/authStore'
import { useLogout } from '@/modules/auth/hooks/useAuth'
import './Topbar.css'

const PAGE_TITLES = {
  '/dashboard': 'Tổng quan',
  '/platforms': 'Platforms / Kênh',
  '/posts': 'Bài viết',
  '/posts/create': 'Tạo bài viết',
  '/bulk': 'Tạo hàng loạt',
  '/prompt-templates': 'Danh mục template',
  '/page-contexts': 'Page Context',
  '/media': 'Media',
  '/jobs': 'Jobs & Logs',
}

function resolveTitle(pathname) {
  if (pathname === '/posts/create') return 'Tạo bài viết mới'
  if (pathname.startsWith('/posts/')) return 'Chi tiết bài viết'
  if (pathname.startsWith('/bulk/')) return 'Chi tiết tiến trình hàng loạt'
  return PAGE_TITLES[pathname] ?? 'VNI Automation'
}

export default function Topbar({ onMenuToggle, isSidebarOpen = false }) {
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
      <div className="topbar-lead">
        <button
          type="button"
          className="topbar-menu-btn"
          onClick={onMenuToggle}
          aria-label="Mở menu điều hướng"
          aria-expanded={isSidebarOpen}
        >
          <span className="topbar-menu-icon" aria-hidden="true" />
        </button>
        <h1 className="topbar-title">{resolveTitle(pathname)}</h1>
      </div>
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
