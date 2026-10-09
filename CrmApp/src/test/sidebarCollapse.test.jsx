import React from 'react'
import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest'
import { render, screen, fireEvent, within } from '@testing-library/react'
import { MemoryRouter, Routes, Route } from 'react-router-dom'
import CrmLayout from '../layouts/CrmLayout'
import { useAuthStore } from '../auth/authStore'

const COLLAPSE_KEY = 'vni.crm.sidebar.collapsed'

describe('AC 85ef074a — CrmApp: nav trái thu gọn/mở rộng (chỉ icon khi thu gọn)', () => {
  beforeEach(() => {
    localStorage.clear()
    vi.restoreAllMocks()
    useAuthStore.getState().setAuth('mock-token', {
      email: 'admin@vni.local',
      userName: 'AdminUser',
      roles: ['Admin'],
    })
  })

  afterEach(() => {
    localStorage.clear()
    vi.restoreAllMocks()
  })

  // (a) Bấm nút thu gọn → sidebar có class collapsed, nhãn chữ ẩn, mỗi link vẫn có aria-label/title đúng tên mục. Bấm lại → mở.
  describe('AC 85ef074a (a) — Thao tác thu gọn/mở rộng sidebar', () => {
    it('bấm nút thu gọn: sidebar có class collapsed, nhãn chữ ẩn, mỗi link có aria-label/title đúng tên mục; bấm lại mở ra', () => {
      render(
        <MemoryRouter initialEntries={['/customers']}>
          <Routes>
            <Route path="/" element={<CrmLayout />}>
              <Route path="customers" element={<div>Khách hàng content</div>} />
            </Route>
          </Routes>
        </MemoryRouter>,
      )

      const sidebar = screen.getByTestId('crm-sidebar')
      const toggleBtn = screen.getByTestId('sidebar-collapse-btn')

      // Mặc định mở rộng: không có class collapsed, aria-expanded="true"
      expect(sidebar).not.toHaveClass('collapsed')
      expect(sidebar).not.toHaveClass('crm-sidebar--collapsed')
      expect(toggleBtn).toHaveAttribute('aria-expanded', 'true')
      expect(toggleBtn).toHaveAttribute('aria-label', 'Thu gọn menu')

      // Nhãn chữ các mục đang hiển thị trong sidebar
      expect(within(sidebar).getByText('Hộp thư')).toBeVisible()
      expect(within(sidebar).getByText('Khách hàng')).toBeVisible()
      expect(within(sidebar).getByText('Việc của tôi')).toBeVisible()
      expect(within(sidebar).getByText('Cài đặt')).toBeVisible()

      // Bấm nút thu gọn
      fireEvent.click(toggleBtn)

      // Sidebar có class collapsed và crm-sidebar--collapsed, aria-expanded="false"
      expect(sidebar).toHaveClass('collapsed')
      expect(sidebar).toHaveClass('crm-sidebar--collapsed')
      expect(toggleBtn).toHaveAttribute('aria-expanded', 'false')
      expect(toggleBtn).toHaveAttribute('aria-label', 'Mở rộng menu')

      // Nhãn chữ trong sidebar bị ẩn
      expect(within(sidebar).getByText('Hộp thư')).not.toBeVisible()
      expect(within(sidebar).getByText('Khách hàng')).not.toBeVisible()
      expect(within(sidebar).getByText('Việc của tôi')).not.toBeVisible()
      expect(within(sidebar).getByText('Cài đặt')).not.toBeVisible()

      // Mỗi link vẫn giữ icon và có aria-label / title đúng tên mục
      const navInbox = screen.getByTestId('nav-inbox')
      const navCustomers = screen.getByTestId('nav-customers')
      const navTasks = screen.getByTestId('nav-tasks')
      const navSettings = screen.getByTestId('nav-settings')

      expect(navInbox).toHaveAttribute('aria-label', 'Hộp thư')
      expect(navInbox).toHaveAttribute('title', 'Hộp thư')

      expect(navCustomers).toHaveAttribute('aria-label', 'Khách hàng')
      expect(navCustomers).toHaveAttribute('title', 'Khách hàng')

      expect(navTasks).toHaveAttribute('aria-label', 'Việc của tôi')
      expect(navTasks).toHaveAttribute('title', 'Việc của tôi')

      expect(navSettings).toHaveAttribute('aria-label', 'Cài đặt')
      expect(navSettings).toHaveAttribute('title', 'Cài đặt')

      // Link active (/customers) vẫn có class active
      expect(navCustomers).toHaveClass('active')

      // Bấm nút mở rộng lại
      fireEvent.click(toggleBtn)

      expect(sidebar).not.toHaveClass('collapsed')
      expect(sidebar).not.toHaveClass('crm-sidebar--collapsed')
      expect(toggleBtn).toHaveAttribute('aria-expanded', 'true')
      expect(within(sidebar).getByText('Hộp thư')).toBeVisible()
      expect(within(sidebar).getByText('Khách hàng')).toBeVisible()
    })
  })

  // (b) Render lại thì giữ trạng thái (localStorage). localStorage throw thì vẫn render, mặc định mở.
  describe('AC 85ef074a (b) — Lưu và khôi phục trạng thái từ localStorage, an toàn khi lỗi', () => {
    it('lưu trạng thái thu gọn vào localStorage và giữ trạng thái khi render lại', () => {
      // Đặt sẵn collapsed trong localStorage
      localStorage.setItem(COLLAPSE_KEY, 'true')

      const { unmount } = render(
        <MemoryRouter initialEntries={['/tasks']}>
          <Routes>
            <Route path="/" element={<CrmLayout />}>
              <Route path="tasks" element={<div>Tasks content</div>} />
            </Route>
          </Routes>
        </MemoryRouter>,
      )

      // Khởi tạo đã ở trạng thái thu gọn
      const sidebar = screen.getByTestId('crm-sidebar')
      expect(sidebar).toHaveClass('collapsed')
      expect(sidebar).toHaveClass('crm-sidebar--collapsed')
      expect(screen.getByTestId('sidebar-collapse-btn')).toHaveAttribute('aria-expanded', 'false')

      // Bấm mở lại -> cập nhật localStorage thành 'false'
      fireEvent.click(screen.getByTestId('sidebar-collapse-btn'))
      expect(sidebar).not.toHaveClass('collapsed')
      expect(localStorage.getItem(COLLAPSE_KEY)).toBe('false')

      unmount()

      // Render lại với localStorage 'false' -> mở rộng
      render(
        <MemoryRouter initialEntries={['/tasks']}>
          <Routes>
            <Route path="/" element={<CrmLayout />}>
              <Route path="tasks" element={<div>Tasks content</div>} />
            </Route>
          </Routes>
        </MemoryRouter>,
      )

      expect(screen.getByTestId('crm-sidebar')).not.toHaveClass('collapsed')
    })

    it('khi localStorage throw exception: vẫn render bình thường, mặc định mở rộng', () => {
      vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => {
        throw new Error('SecurityError: LocalStorage is disabled')
      })
      vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
        throw new Error('QuotaExceededError')
      })

      render(
        <MemoryRouter initialEntries={['/customers']}>
          <Routes>
            <Route path="/" element={<CrmLayout />}>
              <Route path="customers" element={<div>Khách hàng content</div>} />
            </Route>
          </Routes>
        </MemoryRouter>,
      )

      // Không crash, mặc định mở rộng
      const sidebar = screen.getByTestId('crm-sidebar')
      const toggleBtn = screen.getByTestId('sidebar-collapse-btn')

      expect(sidebar).toBeInTheDocument()
      expect(sidebar).not.toHaveClass('collapsed')
      expect(toggleBtn).toHaveAttribute('aria-expanded', 'true')

      // Bấm toggle -> setItem throw nhưng không crash app
      expect(() => {
        fireEvent.click(toggleBtn)
      }).not.toThrow()

      expect(sidebar).toHaveClass('collapsed')
    })
  })

  // (c) Ở /inbox khi thu gọn: main-wrapper có modifier tương ứng.
  describe('AC 85ef074a (c) — Layout full-bleed /inbox khi thu gọn', () => {
    it('ở /inbox: crm-main-wrapper có cả modifier --full và --collapsed khi thu gọn', () => {
      render(
        <MemoryRouter initialEntries={['/inbox']}>
          <Routes>
            <Route path="/" element={<CrmLayout />}>
              <Route path="inbox" element={<div>Inbox full bleed</div>} />
            </Route>
          </Routes>
        </MemoryRouter>,
      )

      const mainWrapper = screen.getByTestId('crm-main-wrapper')
      const sidebar = screen.getByTestId('crm-sidebar')
      const toggleBtn = screen.getByTestId('sidebar-collapse-btn')

      // Ở /inbox ban đầu: có class crm-main-wrapper--full
      expect(mainWrapper).toHaveClass('crm-main-wrapper--full')
      expect(mainWrapper).not.toHaveClass('crm-main-wrapper--collapsed')

      // Thu gọn
      fireEvent.click(toggleBtn)

      // main-wrapper có modifier tương ứng: crm-main-wrapper--collapsed và crm-main-wrapper--full
      expect(sidebar).toHaveClass('crm-sidebar--collapsed')
      expect(mainWrapper).toHaveClass('crm-main-wrapper--collapsed')
      expect(mainWrapper).toHaveClass('crm-main-wrapper--full')

      // Mở lại
      fireEvent.click(toggleBtn)
      expect(mainWrapper).not.toHaveClass('crm-main-wrapper--collapsed')
      expect(mainWrapper).toHaveClass('crm-main-wrapper--full')
    })
  })
})
