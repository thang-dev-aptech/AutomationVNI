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
    to: '/crawl',
    label: 'Tin đã cào',
    icon: (
      <svg xmlns="http://www.w3.org/2000/svg" width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" className="sidebar-link-icon">
        <path d="M4 11a9 9 0 0 1 9 9" />
        <path d="M4 4a16 16 0 0 1 16 16" />
        <circle cx="5" cy="19" r="1" />
      </svg>
    ),
    visible: (p) => p.canViewCrawl,
  },
  {
    to: '/news-site',
    label: 'Bài đã lên web',
    icon: (
      <svg xmlns="http://www.w3.org/2000/svg" width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" className="sidebar-link-icon">
        <rect x="3" y="4" width="18" height="16" rx="2" />
        <path d="M3 9h18" />
        <path d="M7 13h6" />
        <path d="M7 16h10" />
      </svg>
    ),
    visible: (p) => p.canViewCrawl,
  },
  {
    to: '/comments',
    label: 'Comments',
    icon: (
      <svg xmlns="http://www.w3.org/2000/svg" width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" className="sidebar-link-icon">
        <path d="M21 15a2 2 0 0 1-2 2H7l-4 4V5a2 2 0 0 1 2-2h14a2 2 0 0 1 2 2z" />
      </svg>
    ),
    visible: (p) => p.canViewComments,
  },
  {
    to: '/messages',
    label: 'Tin nhắn Page',
    icon: (
      <svg xmlns="http://www.w3.org/2000/svg" width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" className="sidebar-link-icon">
        <path d="M21 11.5a8.4 8.4 0 0 1-9 8.5 9.8 9.8 0 0 1-4-.9L3 21l1.7-4.6A8.4 8.4 0 1 1 21 11.5z" />
        <path d="m8 13 3-3 2 2 3-3" />
      </svg>
    ),
    visible: (p) => p.canViewMessages,
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
    to: '/categories',
    label: 'Loại bài (Danh mục)',
    icon: (
      <svg xmlns="http://www.w3.org/2000/svg" width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" className="sidebar-link-icon">
        <path d="M20.59 13.41l-7.17 7.17a2 2 0 0 1-2.83 0L2 12V2h10l8.59 8.59a2 2 0 0 1 0 2.82z" />
        <line x1="7" y1="7" x2="7.01" y2="7" />
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

const COLLAPSE_KEY = 'vni.sidebar.collapsed'

export default function MainLayout() {
  const permissions = usePermissions()
  const visibleNavItems = NAV_ITEMS.filter((item) => item.visible(permissions))
  const [isSidebarOpen, setSidebarOpen] = useState(false)
  // Đọc ngay trong khởi tạo state chứ không qua useEffect: đặt sau lượt vẽ đầu thì sidebar
  // bung ra rồi mới co lại, thấy rõ một cú giật mỗi lần tải trang.
  const [isCollapsed, setCollapsed] = useState(
    () => localStorage.getItem(COLLAPSE_KEY) === '1',
  )
  const { pathname } = useLocation()

  // Đóng drawer mỗi khi đổi route
  useEffect(() => {
    setSidebarOpen(false)
  }, [pathname])

  useEffect(() => {
    localStorage.setItem(COLLAPSE_KEY, isCollapsed ? '1' : '0')
  }, [isCollapsed])

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
    <div className={`main-layout${isCollapsed ? ' main-layout--collapsed' : ''}`}>
      <div
        className={`sidebar-backdrop${isSidebarOpen ? ' sidebar-backdrop--visible' : ''}`}
        onClick={() => setSidebarOpen(false)}
        aria-hidden="true"
      />
      <aside
        className={`sidebar${isSidebarOpen ? ' sidebar--open' : ''}`
          + `${isCollapsed ? ' sidebar--collapsed' : ''}`}
      >
        <div className="sidebar-brand">
          <img className="sidebar-logo" src="/app-mark.svg" alt="" />
          <span className="sidebar-title">Automation</span>
          <button
            type="button"
            className="sidebar-collapse"
            onClick={() => setCollapsed((v) => !v)}
            aria-label={isCollapsed ? 'Mở rộng menu' : 'Thu gọn menu'}
            aria-expanded={!isCollapsed}
            title={isCollapsed ? 'Mở rộng menu' : 'Thu gọn menu'}
          >
            <svg xmlns="http://www.w3.org/2000/svg" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
              <polyline points={isCollapsed ? '9 18 15 12 9 6' : '15 18 9 12 15 6'} />
            </svg>
          </button>
        </div>
        <nav className="sidebar-nav">
          {visibleNavItems.map((item) => (
            <NavLink
              key={item.to}
              to={item.to}
              className={({ isActive }) =>
                `sidebar-link${isActive ? ' sidebar-link--active' : ''}`
              }
              // Lúc thu gọn chỉ còn icon, không có title thì không cách nào biết nút nào là gì.
              title={isCollapsed ? item.label : undefined}
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
