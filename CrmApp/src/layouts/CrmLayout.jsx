import React, { useState } from 'react'
import { NavLink, Outlet, useNavigate, useLocation } from 'react-router-dom'
import { useAuth } from '../auth/useAuth'
import Icon from '../shared/components/Icon'
import Badge from '../shared/components/Badge'
import Button from '../shared/components/Button'
import './CrmLayout.css'

const COLLAPSE_KEY = 'vni.crm.sidebar.collapsed'

export const CrmLayout = () => {
  const { user, logout, roles } = useAuth()
  const [mobileOpen, setMobileOpen] = useState(false)
  const [isCollapsed, setIsCollapsed] = useState(() => {
    try {
      return localStorage.getItem(COLLAPSE_KEY) === 'true'
    } catch {
      return false
    }
  })
  const navigate = useNavigate()
  const location = useLocation()

  const toggleCollapse = () => {
    setIsCollapsed((prev) => {
      const next = !prev
      try {
        localStorage.setItem(COLLAPSE_KEY, String(next))
      } catch {
        // ignore
      }
      return next
    })
  }

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
  const isInbox = location.pathname.startsWith('/inbox')

  return (
    <div
      className={`crm-layout ${isInbox ? 'crm-layout--full' : ''} ${isCollapsed ? 'crm-layout--collapsed' : ''}`}
    >
      {/* Mobile Drawer Backdrop */}
      <div
        className={`crm-mobile-backdrop ${mobileOpen ? 'crm-backdrop-open' : ''}`}
        onClick={() => setMobileOpen(false)}
        aria-hidden="true"
      />

      {/* Sidebar */}
      <aside
        className={`crm-sidebar ${mobileOpen ? 'crm-sidebar-open' : ''} ${isCollapsed ? 'crm-sidebar--collapsed collapsed' : ''}`}
        data-testid="crm-sidebar"
      >
        <div className="crm-sidebar-brand">
          <div className="crm-brand-content">
            <div className="crm-brand-badge" title="VNI CRM">
              CRM
            </div>
            {!isCollapsed && (
              <div className="crm-brand-text">
                <span className="crm-brand-title">VNI CRM</span>
                <span className="crm-brand-subtitle">Hộp thư & Chăm sóc</span>
              </div>
            )}
          </div>
          <button
            type="button"
            className="crm-sidebar-collapse btn-toggle-sidebar"
            onClick={toggleCollapse}
            aria-label={isCollapsed ? 'Mở rộng menu' : 'Thu gọn menu'}
            aria-expanded={!isCollapsed}
            title={isCollapsed ? 'Mở rộng menu' : 'Thu gọn menu'}
            data-testid="sidebar-collapse-btn"
          >
            <svg
              xmlns="http://www.w3.org/2000/svg"
              width="16"
              height="16"
              viewBox="0 0 24 24"
              fill="none"
              stroke="currentColor"
              strokeWidth="2"
              strokeLinecap="round"
              strokeLinejoin="round"
              aria-hidden="true"
            >
              <polyline points={isCollapsed ? '9 18 15 12 9 6' : '15 18 9 12 15 6'} />
            </svg>
          </button>
        </div>

        <nav className="crm-nav">
          {navItems.map((item) => (
            <NavLink
              key={item.path}
              to={item.path}
              className={({ isActive }) => `crm-nav-link ${isActive ? 'active' : ''}`}
              onClick={() => setMobileOpen(false)}
              data-testid={`nav-${item.path.slice(1)}`}
              title={item.name}
              aria-label={item.name}
            >
              <Icon name={item.icon} size={18} />
              <span
                className="crm-nav-link-label"
                style={isCollapsed ? { display: 'none' } : undefined}
                hidden={isCollapsed}
              >
                {item.name}
              </span>
              {item.badge && (
                <span
                  className={`crm-nav-link-badge ${isCollapsed ? 'crm-nav-link-badge--dot' : ''}`}
                  data-testid={`badge-${item.path.slice(1)}`}
                >
                  {isCollapsed ? '' : item.badge}
                </span>
              )}
            </NavLink>
          ))}
        </nav>

        <div
          className="crm-sidebar-footer"
          style={{
            padding: isCollapsed ? '12px 8px' : '16px',
            borderTop: '1px solid var(--crm-sidebar-border)',
          }}
        >
          <Button
            variant="ghost"
            size="sm"
            style={{
              width: '100%',
              justifyContent: isCollapsed ? 'center' : 'flex-start',
              color: '#94a3b8',
              padding: isCollapsed ? '8px 0' : undefined,
            }}
            onClick={handleLogout}
            icon={<Icon name="logout" size={16} />}
            title="Đăng xuất"
            aria-label="Đăng xuất"
            data-testid="sidebar-logout-btn"
          >
            {!isCollapsed && <span className="crm-sidebar-footer-text">Đăng xuất</span>}
          </Button>
        </div>
      </aside>

      {/* Main Content Area */}
      <div
        className={`crm-main-wrapper ${isInbox ? 'crm-main-wrapper--full' : ''} ${isCollapsed ? 'crm-main-wrapper--collapsed' : ''}`}
        data-testid="crm-main-wrapper"
      >
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

          <main
            className={`crm-content ${isInbox ? 'crm-content--full crm-content-full' : ''}`}
            data-testid="crm-main-content"
          >
            <Outlet />
          </main>
        </div>
      </div>
    )
}

export default CrmLayout
