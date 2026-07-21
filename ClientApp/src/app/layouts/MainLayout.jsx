import { useEffect, useState } from 'react'
import { NavLink, Outlet, useLocation } from 'react-router-dom'
import { usePermissions } from '@/shared/hooks/usePermissions'
import Topbar from './Topbar'
import './MainLayout.css'

const NAV_ITEMS = [
  {
    to: '/dashboard',
    label: 'Dashboard',
    icon: (
      <svg xmlns="http://www.w3.org/2000/svg" width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" className="sidebar-link-icon">
        <rect x="3" y="3" width="7" height="9" rx="1" />
        <rect x="14" y="3" width="7" height="5" rx="1" />
        <rect x="14" y="12" width="7" height="9" rx="1" />
        <rect x="3" y="16" width="7" height="5" rx="1" />
      </svg>
    ),
    visible: (p) => p.canViewDashboard,
  },
  {
    to: '/platforms',
    label: 'Platforms / Kênh',
    icon: (
      <svg xmlns="http://www.w3.org/2000/svg" width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" className="sidebar-link-icon">
        <circle cx="18" cy="5" r="3" />
        <circle cx="6" cy="12" r="3" />
        <circle cx="18" cy="19" r="3" />
        <line x1="8.59" y1="13.51" x2="15.42" y2="17.49" />
        <line x1="15.41" y1="6.51" x2="8.59" y2="10.49" />
      </svg>
    ),
    visible: (p) => p.canViewPlatforms,
  },
  {
    to: '/posts',
    label: 'Posts',
    icon: (
      <svg xmlns="http://www.w3.org/2000/svg" width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" className="sidebar-link-icon">
        <path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z" />
        <polyline points="14 2 14 8 20 8" />
        <line x1="16" y1="13" x2="8" y2="13" />
        <line x1="16" y1="17" x2="8" y2="17" />
        <polyline points="10 9 9 9 8 9" />
      </svg>
    ),
    visible: (p) => p.canViewPosts,
  },
  {
    to: '/bulk',
    label: 'Tạo hàng loạt',
    icon: (
      <svg xmlns="http://www.w3.org/2000/svg" width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" className="sidebar-link-icon">
        <rect x="8" y="2" width="8" height="14" rx="2" ry="2" />
        <path d="M4 6H2a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h14a2 2 0 0 0 2-2v-2" />
      </svg>
    ),
    visible: (p) => p.canCreatePost,
  },
  {
    to: '/prompt-templates',
    label: 'Danh mục template',
    icon: (
      <svg xmlns="http://www.w3.org/2000/svg" width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" className="sidebar-link-icon">
        <rect x="3" y="3" width="18" height="18" rx="2" ry="2" />
        <line x1="3" y1="9" x2="21" y2="9" />
        <line x1="9" y1="21" x2="9" y2="9" />
      </svg>
    ),
    visible: (p) => p.canManageTemplates,
  },
  {
    to: '/page-contexts',
    label: 'Page Context',
    icon: (
      <svg xmlns="http://www.w3.org/2000/svg" width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" className="sidebar-link-icon">
        <path d="M21 15a2 2 0 0 1-2 2H7l-4 4V5a2 2 0 0 1 2-2h14a2 2 0 0 1 2 2z" />
      </svg>
    ),
    visible: (p) => p.canManageTemplates,
  },
  {
    to: '/media',
    label: 'Media',
    icon: (
      <svg xmlns="http://www.w3.org/2000/svg" width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" className="sidebar-link-icon">
        <rect x="3" y="3" width="18" height="18" rx="2" ry="2" />
        <circle cx="8.5" cy="8.5" r="1.5" />
        <polyline points="21 15 16 10 5 21" />
      </svg>
    ),
    visible: (p) => p.canViewMedia,
  },
  {
    to: '/jobs',
    label: 'Jobs',
    icon: (
      <svg xmlns="http://www.w3.org/2000/svg" width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" className="sidebar-link-icon">
        <circle cx="12" cy="12" r="10" />
        <polyline points="12 6 12 12 16 14" />
      </svg>
    ),
    visible: (p) => p.canViewJobs,
  },
]

export default function MainLayout() {
  const permissions = usePermissions()
  const visibleNavItems = NAV_ITEMS.filter((item) => item.visible(permissions))
  const [isSidebarOpen, setSidebarOpen] = useState(false)
  const { pathname } = useLocation()

  // Đóng drawer mỗi khi đổi route
  useEffect(() => {
    setSidebarOpen(false)
  }, [pathname])

  // Đóng bằng Esc + khoá scroll body khi drawer mở
  useEffect(() => {
    if (!isSidebarOpen) return undefined
    const onKeyDown = (event) => {
      if (event.key === 'Escape') setSidebarOpen(false)
    }
    document.addEventListener('keydown', onKeyDown)
    document.body.style.overflow = 'hidden'
    return () => {
      document.removeEventListener('keydown', onKeyDown)
      document.body.style.overflow = ''
    }
  }, [isSidebarOpen])

  return (
    <div className="main-layout">
      <div
        className={`sidebar-backdrop${isSidebarOpen ? ' sidebar-backdrop--visible' : ''}`}
        onClick={() => setSidebarOpen(false)}
        aria-hidden="true"
      />
      <aside className={`sidebar${isSidebarOpen ? ' sidebar--open' : ''}`}>
        <div className="sidebar-brand">
          <img className="sidebar-logo" src="/vni-logo.png" alt="VNI Education" />
          <span className="sidebar-title">Automation</span>
        </div>
        <nav className="sidebar-nav">
          {visibleNavItems.map((item) => (
            <NavLink
              key={item.to}
              to={item.to}
              className={({ isActive }) =>
                `sidebar-link${isActive ? ' sidebar-link--active' : ''}`
              }
            >
              {item.icon}
              <span className="sidebar-link-label">{item.label}</span>
            </NavLink>
          ))}
        </nav>
      </aside>
      <div className="main-shell">
        <Topbar
          onMenuToggle={() => setSidebarOpen((open) => !open)}
          isSidebarOpen={isSidebarOpen}
        />
        <main className="main-content">
          <Outlet />
        </main>
      </div>
    </div>
  )
}
