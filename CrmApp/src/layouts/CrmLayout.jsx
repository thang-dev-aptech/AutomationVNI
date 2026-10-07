import React, { useState } from 'react'
import { NavLink, Outlet, useNavigate, useLocation } from 'react-router-dom'
import { useAuth } from '../auth/useAuth'
import Icon from '../shared/components/Icon'
import Badge from '../shared/components/Badge'
import Button from '../shared/components/Button'
import './CrmLayout.css'

export const CrmLayout = () => {
  const { user, logout, roles } = useAuth()
  const [mobileOpen, setMobileOpen] = useState(false)
  const navigate = useNavigate()
  const location = useLocation()

  const navItems = [
    { name: 'Hộp thư', path: '/inbox', icon: 'inbox', badge: null },
    { name: 'Khách hàng', path: '/customers', icon: 'customers', badge: null },
    { name: 'Việc của tôi', path: '/tasks', icon: 'tasks', badge: null },
    { name: 'Cài đặt', path: '/settings', icon: 'settings', badge: null },
  ]

  const handleLogout = async () => {
    await logout()
    navigate('/login')
  }

  const primaryRole = roles[0] || 'Viewer'
  const getRoleBadgeVariant = (role) => {
    switch (role) {
      case 'Admin':
        return 'danger'
      case 'ContentManager':
        return 'primary'
      case 'Reviewer':
        return 'warning'
      case 'Viewer':
      default:
        return 'default'
    }
  }

  const getPageTitle = () => {
    const current = navItems.find((item) => location.pathname.startsWith(item.path))
    return current ? current.name : 'VNI CRM'
  }

  const userInitial = user?.userName?.[0] || user?.email?.[0] || 'U'

  return (
    <div className="crm-layout">
      {/* Mobile Drawer Backdrop */}
      <div
        className={`crm-mobile-backdrop ${mobileOpen ? 'crm-backdrop-open' : ''}`}
        onClick={() => setMobileOpen(false)}
        aria-hidden="true"
      />

      {/* Sidebar */}
      <aside className={`crm-sidebar ${mobileOpen ? 'crm-sidebar-open' : ''}`} data-testid="crm-sidebar">
        <div className="crm-sidebar-brand">
          <div className="crm-brand-badge">CRM</div>
          <div className="crm-brand-text">
            <span className="crm-brand-title">VNI CRM</span>
            <span className="crm-brand-subtitle">Hộp thư & Chăm sóc</span>
          </div>
        </div>

        <nav className="crm-nav">
          {navItems.map((item) => (
            <NavLink
              key={item.path}
              to={item.path}
              className={({ isActive }) => `crm-nav-link ${isActive ? 'active' : ''}`}
              onClick={() => setMobileOpen(false)}
              data-testid={`nav-${item.path.slice(1)}`}
            >
              <Icon name={item.icon} size={18} />
              <span>{item.name}</span>
              {item.badge && <span className="crm-nav-link-badge">{item.badge}</span>}
            </NavLink>
          ))}
        </nav>

        <div style={{ padding: '16px', borderTop: '1px solid var(--crm-sidebar-border)' }}>
          <Button
            variant="ghost"
            size="sm"
            style={{ width: '100%', justifyContent: 'flex-start', color: '#94a3b8' }}
            onClick={handleLogout}
            icon={<Icon name="logout" size={16} />}
            data-testid="sidebar-logout-btn"
          >
            Đăng xuất
          </Button>
        </div>
      </aside>

      {/* Main Content Area */}
      <div className="crm-main-wrapper">
        <header className="crm-header">
          <div className="crm-header-left">
            <button
              type="button"
              className="crm-mobile-toggle"
              onClick={() => setMobileOpen(!mobileOpen)}
              aria-label="Toggle menu"
              data-testid="mobile-menu-btn"
            >
              <Icon name={mobileOpen ? 'close' : 'menu'} size={20} />
            </button>
            <h1 className="crm-header-title">{getPageTitle()}</h1>
          </div>

          <div className="crm-header-right">
            <div className="crm-user-profile" data-testid="user-profile-badge">
              <div className="crm-user-avatar">{userInitial.toUpperCase()}</div>
              <div className="crm-user-meta">
                <span className="crm-user-email">{user?.email || 'Người dùng'}</span>
                <Badge variant={getRoleBadgeVariant(primaryRole)} size="sm">
                  {primaryRole}
                </Badge>
              </div>
            </div>

            <Button
              variant="outline"
              size="sm"
              onClick={handleLogout}
              icon={<Icon name="logout" size={16} />}
              data-testid="header-logout-btn"
            >
              Thoát
            </Button>
          </div>
        </header>

        <main className="crm-content">
          <Outlet />
        </main>
      </div>
    </div>
  )
}

export default CrmLayout
