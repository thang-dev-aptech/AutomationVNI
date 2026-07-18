import { NavLink, Outlet } from 'react-router-dom'
import { usePermissions } from '@/shared/hooks/usePermissions'
import Topbar from './Topbar'
import './MainLayout.css'

const NAV_ITEMS = [
  { to: '/dashboard', label: 'Dashboard', visible: (p) => p.canViewDashboard },
  { to: '/platforms', label: 'Platforms / Kênh', visible: (p) => p.canViewPlatforms },
  { to: '/posts', label: 'Posts', visible: (p) => p.canViewPosts },
  { to: '/bulk', label: 'Tạo hàng loạt', visible: (p) => p.canCreatePost },
  { to: '/prompt-templates', label: 'Prompt Templates', visible: (p) => p.canManageTemplates },
  { to: '/page-contexts', label: 'Page Context', visible: (p) => p.canManageTemplates },
  { to: '/media', label: 'Media', visible: (p) => p.canViewMedia },
  { to: '/jobs', label: 'Jobs', visible: (p) => p.canViewJobs },
]

export default function MainLayout() {
  const permissions = usePermissions()
  const visibleNavItems = NAV_ITEMS.filter((item) => item.visible(permissions))

  return (
    <div className="main-layout">
      <aside className="sidebar">
        <div className="sidebar-brand">
          <span className="sidebar-logo">VNI</span>
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
              {item.label}
            </NavLink>
          ))}
        </nav>
      </aside>
      <div className="main-shell">
        <Topbar />
        <main className="main-content">
          <Outlet />
        </main>
      </div>
    </div>
  )
}
