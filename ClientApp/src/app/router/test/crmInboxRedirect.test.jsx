import { render, screen, waitFor } from '@testing-library/react'
import { MemoryRouter, useLocation } from 'react-router-dom'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import AppRouter from '@/app/router'
import MainLayout, { NAV_GROUPS } from '@/app/layouts/MainLayout'
import { buildCrmRedirectUrl } from '@/app/router/CrmRedirectPage'
import fs from 'node:fs'
import path from 'node:path'

vi.mock('@/shared/hooks/usePermissions', () => ({
  usePermissions: () => ({
    canViewDashboard: true,
    canViewPosts: true,
    canCreatePost: true,
    canViewCrawl: true,
    canViewComments: true,
    canViewMessages: true,
    canViewPlatforms: true,
    canViewMedia: true,
    canViewJobs: true,
    canManageTemplates: true,
    hasRole: () => true,
  }),
}))

vi.mock('@/app/router/ProtectedRoute', () => ({
  default: ({ children }) => {
    const { Outlet } = require('react-router-dom')
    return children || <Outlet />
  },
}))

vi.mock('@/app/router/GuestRoute', () => ({
  default: ({ children }) => {
    const { Outlet } = require('react-router-dom')
    return children || <Outlet />
  },
}))

function renderWithRouter(ui, { route = '/' } = {}) {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false, gcTime: 0 } },
  })

  return render(
    <QueryClientProvider client={queryClient}>
      <MemoryRouter initialEntries={[route]}>
        {ui}
      </MemoryRouter>
    </QueryClientProvider>,
  )
}

describe('AC 85e37852 (d)-(f): Chuyển hướng CRM và gỡ bỏ module conversations', () => {
  const originalEnvUrl = import.meta.env.VITE_CRM_URL
  let assignSpy

  beforeEach(() => {
    vi.clearAllMocks()
    assignSpy = vi.fn()
    // Setup window.location.assign mock safely in JSDOM
    try {
      window.location.assign = assignSpy
    } catch {
      delete window.location
      window.location = { assign: assignSpy }
    }
  })

  afterEach(() => {
    import.meta.env.VITE_CRM_URL = originalEnvUrl
  })

  describe('Helper buildCrmRedirectUrl', () => {
    it('ghép URL chính xác với kind và các tham số query', () => {
      expect(buildCrmRedirectUrl('', 'message')).toBeNull()
      expect(buildCrmRedirectUrl('https://crm.example/inbox', undefined, '')).toBe('https://crm.example/inbox')
      expect(buildCrmRedirectUrl('https://crm.example/inbox', 'message', '')).toBe('https://crm.example/inbox?kind=message')
      expect(buildCrmRedirectUrl('https://crm.example/inbox', 'comment', '')).toBe('https://crm.example/inbox?kind=comment')
      expect(buildCrmRedirectUrl('https://crm.example/inbox', 'message', '?kind=comment')).toBe('https://crm.example/inbox?kind=comment')
      expect(buildCrmRedirectUrl('https://crm.example/inbox', undefined, '?kind=message&page=1')).toBe('https://crm.example/inbox?kind=message&page=1')
      expect(buildCrmRedirectUrl('https://crm.example/inbox?foo=bar', 'message', '')).toBe('https://crm.example/inbox?foo=bar&kind=message')
    })
  })

  describe('(d) Có VITE_CRM_URL: menu "Hội thoại" trỏ tới CrmApp, các route chuyển tiếp đúng URL', () => {
    const CRM_URL = 'https://crm.vni.edu.vn/inbox'

    beforeEach(() => {
      import.meta.env.VITE_CRM_URL = CRM_URL
    })

    it('menu "Hội thoại" trong MainLayout là link ngoài trỏ tới VITE_CRM_URL', () => {
      renderWithRouter(<MainLayout />, { route: '/dashboard' })

      const link = screen.getByText('Hội thoại').closest('a')
      expect(link).toBeInTheDocument()
      expect(link).toHaveAttribute('href', CRM_URL)
      expect(screen.queryByTestId('inbox-summary-badge')).not.toBeInTheDocument()
    })

    it('/messages chuyển hướng sang VITE_CRM_URL với kind=message', async () => {
      renderWithRouter(<AppRouter />, { route: '/messages' })

      await waitFor(() => {
        expect(assignSpy).toHaveBeenCalledWith(`${CRM_URL}?kind=message`)
      })
    })

    it('/comments chuyển hướng sang VITE_CRM_URL với kind=comment', async () => {
      renderWithRouter(<AppRouter />, { route: '/comments' })

      await waitFor(() => {
        expect(assignSpy).toHaveBeenCalledWith(`${CRM_URL}?kind=comment`)
      })
    })

    it('/conversations không query chuyển hướng sang VITE_CRM_URL', async () => {
      renderWithRouter(<AppRouter />, { route: '/conversations' })

      await waitFor(() => {
        expect(assignSpy).toHaveBeenCalledWith(CRM_URL)
      })
    })

    it('/conversations?kind=message giữ nguyên tham số query', async () => {
      renderWithRouter(<AppRouter />, { route: '/conversations?kind=message' })

      await waitFor(() => {
        expect(assignSpy).toHaveBeenCalledWith(`${CRM_URL}?kind=message`)
      })
    })

    it('/conversations?kind=comment&search=demo giữ nguyên tham số query', async () => {
      renderWithRouter(<AppRouter />, { route: '/conversations?kind=comment&search=demo' })

      await waitFor(() => {
        expect(assignSpy).toHaveBeenCalledWith(`${CRM_URL}?kind=comment&search=demo`)
      })
    })
  })

  describe('(e) Không có VITE_CRM_URL: menu ẩn, các route cũ hiện thông báo, không crash', () => {
    beforeEach(() => {
      delete import.meta.env.VITE_CRM_URL
    })

    it('menu "Hội thoại" bị ẩn trong MainLayout', () => {
      renderWithRouter(<MainLayout />, { route: '/dashboard' })

      expect(screen.queryByText('Hội thoại')).not.toBeInTheDocument()
    })

    it('/conversations hiển thị trang thông báo "Chưa cấu hình địa chỉ CRM"', async () => {
      renderWithRouter(<AppRouter />, { route: '/conversations' })

      expect(await screen.findByText('Chưa cấu hình địa chỉ CRM')).toBeInTheDocument()
      expect(assignSpy).not.toHaveBeenCalled()
    })

    it('/messages hiển thị trang thông báo "Chưa cấu hình địa chỉ CRM"', async () => {
      renderWithRouter(<AppRouter />, { route: '/messages' })

      expect(await screen.findByText('Chưa cấu hình địa chỉ CRM')).toBeInTheDocument()
      expect(assignSpy).not.toHaveBeenCalled()
    })

    it('/comments hiển thị trang thông báo "Chưa cấu hình địa chỉ CRM"', async () => {
      renderWithRouter(<AppRouter />, { route: '/comments' })

      expect(await screen.findByText('Chưa cấu hình địa chỉ CRM')).toBeInTheDocument()
      expect(assignSpy).not.toHaveBeenCalled()
    })
  })

  describe('(f) Không còn module conversations và không còn import tới module này', () => {
    it('thư mục src/modules/conversations không tồn tại', () => {
      const convDir = path.resolve(__dirname, '../../../modules/conversations')
      expect(fs.existsSync(convDir)).toBe(false)
    })

    it('không còn import nào tới modules/conversations trong ClientApp/src', () => {
      const srcDir = path.resolve(__dirname, '../../..')
      function scanDir(dir) {
        const files = fs.readdirSync(dir)
        for (const file of files) {
          const fullPath = path.join(dir, file)
          const stat = fs.statSync(fullPath)
          if (stat.isDirectory()) {
            scanDir(fullPath)
          } else if (/\.(jsx?|tsx?)$/.test(file)) {
            const content = fs.readFileSync(fullPath, 'utf8')
            expect(content).not.toMatch(/from\s+['"][^'"]*modules\/conversations/i)
            expect(content).not.toMatch(/import\s*\([^)]*modules\/conversations/i)
          }
        }
      }
      scanDir(srcDir)
    })
  })
})
