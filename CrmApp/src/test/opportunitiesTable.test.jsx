import React from 'react'
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { render, screen, fireEvent, waitFor, act } from '@testing-library/react'
import { MemoryRouter, Routes, Route, useLocation } from 'react-router-dom'
import { OpportunitiesFeature } from '../features/opportunities/OpportunitiesFeature'
import { TasksPage } from '../modules/tasks/TasksPage'
import { opportunityApi } from '../features/opportunities/api/opportunityApi'
import { customerApi } from '../features/customers/api/customerApi'
import { useAuthStore } from '../auth/authStore'

describe('AC fd9216b3 (a)(b)(d) — Opportunities Table & Header & Viewer & Customer Picker', () => {
  const mockStages = [
    { id: 'stg-1', name: 'Mới', kind: 1, sortOrder: 1, color: '#3b82f6' },
    { id: 'stg-2', name: 'Đủ điều kiện', kind: 1, sortOrder: 2, color: '#6366f1' },
    { id: 'stg-3', name: 'Đã mua', kind: 2, sortOrder: 5, color: '#10b981' },
    { id: 'stg-4', name: 'Thất bại', kind: 3, sortOrder: 6, color: '#ef4444' },
  ]

  const mockUsers = [
    { id: 'u-1', userName: 'sale1', displayName: 'Nguyễn Văn Sale' },
    { id: 'u-2', userName: 'sale2', displayName: 'Trần Thị Care' },
  ]

  const mockStats = {
    total: 10,
    open: 5,
    won: 2,
    lost: 3,
    activity: 8,
    rev: 50000000,
  }

  const createMockOpportunities = (count = 5) => {
    return Array.from({ length: count }, (_, i) => ({
      id: `opp-${i + 1}`,
      title: `Cơ hội khoá học Tiếng Anh ${i + 1}`,
      crmCustomerId: `cust-${i + 1}`,
      customerName: `Học viên ${i + 1}`,
      customerPhoneE164: `+8490000000${i}`,
      stageId: i === 2 ? 'stg-3' : 'stg-1',
      stageName: i === 2 ? 'Đã mua' : 'Mới',
      stageColor: i === 2 ? '#10b981' : '#3b82f6',
      status: i === 2 ? 2 : 1,
      isArchived: false,
      assigneeUserId: 'u-1',
      assignedTo: 'Nguyễn Văn Sale',
      expectedValue: 10000000,
      source: 1,
      channelPlatform: null,
      channelName: null,
      watcherUserIds: ['u-1', 'u-2'],
      lastActivityAtUtc: '2026-10-08T08:00:00Z',
      createdAt: '2026-10-08T07:00:00Z',
    }))
  }

  // Component helper to inspect current location
  const LocationTracker = () => {
    const location = useLocation()
    return <div data-testid="location-search">{location.search}</div>
  }

  beforeEach(() => {
    vi.restoreAllMocks()
    localStorage.clear()

    // Default auth: Admin / ContentManager
    useAuthStore.getState().setAuth('mock-token', {
      id: 'u-admin',
      email: 'admin@vni.local',
      userName: 'Admin User',
      roles: ['Admin', 'ContentManager'],
    })

    // Mock API methods
    vi.spyOn(opportunityApi, 'listStages').mockResolvedValue(mockStages)
    vi.spyOn(opportunityApi, 'listUsers').mockResolvedValue(mockUsers)
    vi.spyOn(opportunityApi, 'stats').mockResolvedValue(mockStats)
    vi.spyOn(opportunityApi, 'filter').mockResolvedValue({
      items: createMockOpportunities(5),
      total: 5,
      index: 1,
      size: 20,
    })
    vi.spyOn(opportunityApi, 'get').mockImplementation(async (id) => {
      const opp = createMockOpportunities(1)[0]
      return {
        ...opp,
        id,
        watchers: mockUsers,
      }
    })
    vi.spyOn(opportunityApi, 'moveStage').mockResolvedValue({ success: true })
    vi.spyOn(opportunityApi, 'archive').mockResolvedValue({ success: true })
    vi.spyOn(opportunityApi, 'unarchive').mockResolvedValue({ success: true })
    vi.spyOn(opportunityApi, 'delete').mockResolvedValue({ success: true })
    vi.spyOn(opportunityApi, 'create').mockResolvedValue({ id: 'opp-new' })
    vi.spyOn(opportunityApi, 'update').mockResolvedValue({ success: true })

    vi.spyOn(window, 'confirm').mockReturnValue(true)

    vi.spyOn(customerApi, 'filter').mockResolvedValue({
      items: [
        { id: 'c-1', displayName: 'Khách A', phoneE164: '+84911111111' },
        { id: 'c-2', displayName: 'Khách B', phoneE164: '+84922222222' },
      ],
      total: 2,
    })
  })

  afterEach(() => {
    vi.restoreAllMocks()
    localStorage.clear()
  })

  // =========================================================================
  // AC fd9216b3 (a) — Header "Cơ hội", thống kê, switch Bảng ↔ Pipeline,
  //                   URL ?view=, trực tiếp /tasks?view=pipeline, first request assignee=mine
  // =========================================================================
  describe('AC fd9216b3 (a) — Header, Thống kê, View Switcher & URL State', () => {
    it('/tasks render header "Cơ hội" và thanh thống kê đúng số từ API mock', async () => {
      render(
        <MemoryRouter initialEntries={['/tasks']}>
          <Routes>
            <Route path="/tasks" element={<TasksPage />} />
          </Routes>
        </MemoryRouter>
      )

      // Header title
      expect(await screen.findByRole('heading', { name: 'Cơ hội' })).toBeInTheDocument()

      // Stats bar values
      expect(screen.getByTestId('stat-total-val')).toHaveTextContent('10')
      expect(screen.getByTestId('stat-open-val')).toHaveTextContent('5')
      expect(screen.getByTestId('stat-won-val')).toHaveTextContent('2')
      expect(screen.getByTestId('stat-lost-val')).toHaveTextContent('3')
      expect(screen.getByTestId('stat-activity-val')).toHaveTextContent('8')
      // Rev is formatted in VND: e.g. 50.000.000 ₫
      expect(screen.getByTestId('stat-rev-val')).toHaveTextContent(/50\.000\.000/)
    })

    it('request đầu tiên gọi API filter có assigneeFilter=mine', async () => {
      render(
        <MemoryRouter initialEntries={['/tasks']}>
          <Routes>
            <Route path="/tasks" element={<TasksPage />} />
          </Routes>
        </MemoryRouter>
      )

      await waitFor(() => {
        expect(opportunityApi.filter).toHaveBeenCalledWith(
          expect.objectContaining({
            assigneeFilter: 'mine',
            index: 1,
            size: 20,
          })
        )
      })
    })

    it('chuyển Bảng ↔ Pipeline ghi ?view= lên URL; vào thẳng /tasks?view=pipeline thì mở Pipeline', async () => {
      const { unmount } = render(
        <MemoryRouter initialEntries={['/tasks']}>
          <LocationTracker />
          <Routes>
            <Route path="/tasks" element={<TasksPage />} />
          </Routes>
        </MemoryRouter>
      )

      // Initially table is rendered
      expect(await screen.findByTestId('opportunity-table')).toBeInTheDocument()
      expect(screen.queryByTestId('opportunity-pipeline')).not.toBeInTheDocument()

      // Click Pipeline button
      const btnPipeline = screen.getByTestId('btn-view-pipeline')
      fireEvent.click(btnPipeline)

      // Pipeline placeholder is shown and URL has ?view=pipeline
      expect(await screen.findByTestId('opportunity-pipeline')).toBeInTheDocument()
      expect(screen.queryByTestId('opportunity-table')).not.toBeInTheDocument()
      expect(screen.getByTestId('location-search').textContent).toContain('view=pipeline')

      // Click Bảng button
      const btnTable = screen.getByTestId('btn-view-table')
      fireEvent.click(btnTable)

      // Table is shown and URL has ?view=table
      expect(await screen.findByTestId('opportunity-table')).toBeInTheDocument()
      expect(screen.getByTestId('location-search').textContent).toContain('view=table')

      unmount()

      // Direct navigation to /tasks?view=pipeline
      render(
        <MemoryRouter initialEntries={['/tasks?view=pipeline']}>
          <Routes>
            <Route path="/tasks" element={<TasksPage />} />
          </Routes>
        </MemoryRouter>
      )

      expect(await screen.findByTestId('opportunity-pipeline')).toBeInTheDocument()
      expect(screen.queryByTestId('opportunity-table')).not.toBeInTheDocument()
    })
  })

  // =========================================================================
  // AC fd9216b3 (b) — Bảng: 4 tabs, Hiển thị cột (localStorage),
  //                   phân trang "x - y / tổng", menu ⋮
  // =========================================================================
  describe('AC fd9216b3 (b) — Bảng, Tabs, Hiển thị cột, Phân trang, Menu ⋮', () => {
    it('4 tab đổi bộ lọc status/archived; tab Hoạt động render placeholder data-testid="opportunity-activities"', async () => {
      render(
        <MemoryRouter initialEntries={['/tasks']}>
          <LocationTracker />
          <Routes>
            <Route path="/tasks" element={<TasksPage />} />
          </Routes>
        </MemoryRouter>
      )

      expect(await screen.findByTestId('opportunity-table')).toBeInTheDocument()

      // 1. Tab "Đang mở"
      const tabOpen = screen.getByTestId('tab-open')
      fireEvent.click(tabOpen)

      await waitFor(() => {
        expect(opportunityApi.filter).toHaveBeenCalledWith(
          expect.objectContaining({
            status: 1,
            isArchived: false,
          })
        )
      })
      expect(screen.getByTestId('location-search').textContent).toContain('tab=open')

      // 2. Tab "Lưu trữ"
      const tabArchived = screen.getByTestId('tab-archived')
      fireEvent.click(tabArchived)

      await waitFor(() => {
        expect(opportunityApi.filter).toHaveBeenCalledWith(
          expect.objectContaining({
            status: null,
            isArchived: true,
          })
        )
      })
      expect(screen.getByTestId('location-search').textContent).toContain('tab=archived')

      // 3. Tab "Hoạt động"
      const tabActivities = screen.getByTestId('tab-activities')
      fireEvent.click(tabActivities)

      expect(await screen.findByTestId('opportunity-activities')).toBeInTheDocument()
      expect(screen.queryByTestId('opportunity-table')).not.toBeInTheDocument()
      expect(screen.getByTestId('location-search').textContent).toContain('tab=activities')

      // 4. Tab "Tất cả"
      const tabAll = screen.getByTestId('tab-all')
      fireEvent.click(tabAll)

      await waitFor(() => {
        expect(opportunityApi.filter).toHaveBeenCalledWith(
          expect.objectContaining({
            status: null,
            isArchived: false,
          })
        )
      })
      expect(screen.getByTestId('opportunity-table')).toBeInTheDocument()
    })

    it('"Hiển thị" ẩn cột rồi render lại vẫn ẩn (localStorage), localStorage throw thì không crash', async () => {
      const { unmount } = render(
        <MemoryRouter initialEntries={['/tasks']}>
          <Routes>
            <Route path="/tasks" element={<TasksPage />} />
          </Routes>
        </MemoryRouter>
      )

      expect(await screen.findByTestId('opportunity-table')).toBeInTheDocument()

      // Column "Khách hàng" is initially visible
      expect(screen.getByRole('columnheader', { name: 'Khách hàng' })).toBeInTheDocument()

      // Open column settings dropdown
      const btnColSettings = screen.getByTestId('btn-column-settings')
      fireEvent.click(btnColSettings)

      expect(await screen.findByTestId('col-settings-dropdown')).toBeInTheDocument()

      // Toggle off column "customer"
      const chkCustomer = screen.getByTestId('col-toggle-customer')
      fireEvent.click(chkCustomer)

      // Column "Khách hàng" should disappear from table header
      expect(screen.queryByRole('columnheader', { name: 'Khách hàng' })).not.toBeInTheDocument()

      // Check localStorage
      const stored = JSON.parse(localStorage.getItem('crm_opp_columns') || '{}')
      expect(stored.customer).toBe(false)

      unmount()

      // Re-render: should restore hidden column state from localStorage
      const { unmount: unmount2 } = render(
        <MemoryRouter initialEntries={['/tasks']}>
          <Routes>
            <Route path="/tasks" element={<TasksPage />} />
          </Routes>
        </MemoryRouter>
      )

      expect(await screen.findByTestId('opportunity-table')).toBeInTheDocument()
      expect(screen.queryByRole('columnheader', { name: 'Khách hàng' })).not.toBeInTheDocument()

      unmount2()

      // If localStorage throws exception, it shouldn't crash
      vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => {
        throw new Error('QuotaExceeded or SecurityError in private mode')
      })

      render(
        <MemoryRouter initialEntries={['/tasks']}>
          <Routes>
            <Route path="/tasks" element={<TasksPage />} />
          </Routes>
        </MemoryRouter>
      )

      // Renders successfully without crash, default columns applied
      expect(await screen.findByTestId('opportunity-table')).toBeInTheDocument()
      expect(screen.getByRole('columnheader', { name: 'Khách hàng' })).toBeInTheDocument()
    })

    it('phân trang hiện "x - y / tổng" và gọi index kế', async () => {
      opportunityApi.filter.mockResolvedValue({
        items: createMockOpportunities(20),
        total: 45,
        index: 1,
        size: 20,
      })

      render(
        <MemoryRouter initialEntries={['/tasks']}>
          <LocationTracker />
          <Routes>
            <Route path="/tasks" element={<TasksPage />} />
          </Routes>
        </MemoryRouter>
      )

      expect(await screen.findByTestId('opportunity-table')).toBeInTheDocument()

      // Check pagination info
      const paginationInfo = screen.getByTestId('pagination-info')
      expect(paginationInfo).toHaveTextContent('1 - 20 / 45')

      // Click next page
      const btnNext = screen.getByTestId('btn-next-page')
      expect(btnNext).not.toBeDisabled()

      opportunityApi.filter.mockResolvedValue({
        items: createMockOpportunities(20),
        total: 45,
        index: 2,
        size: 20,
      })

      fireEvent.click(btnNext)

      await waitFor(() => {
        expect(opportunityApi.filter).toHaveBeenCalledWith(
          expect.objectContaining({
            index: 2,
            size: 20,
          })
        )
      })

      expect(screen.getByTestId('location-search').textContent).toContain('index=2')
      expect(screen.getByTestId('pagination-info')).toHaveTextContent('21 - 40 / 45')
    })

    it('menu ⋮ gọi đúng API: chuyển giai đoạn, lưu trữ, xoá', async () => {
      render(
        <MemoryRouter initialEntries={['/tasks']}>
          <Routes>
            <Route path="/tasks" element={<TasksPage />} />
          </Routes>
        </MemoryRouter>
      )

      expect(await screen.findByTestId('opportunity-table')).toBeInTheDocument()

      // Open menu ⋮ for opp-1
      const btnMenu = screen.getByTestId('btn-actions-opp-1')
      fireEvent.click(btnMenu)

      expect(await screen.findByTestId('menu-actions-opp-1')).toBeInTheDocument()

      // 1. Chuyển giai đoạn
      const btnMoveStage = screen.getByTestId('action-move-stage-opp-1')
      fireEvent.click(btnMoveStage)

      expect(await screen.findByTestId('move-stage-modal')).toBeInTheDocument()
      const selectTarget = screen.getByTestId('select-target-stage')
      fireEvent.change(selectTarget, { target: { value: 'stg-3' } }) // Đã mua (Won)

      const btnConfirmMove = screen.getByTestId('btn-confirm-move-stage')
      fireEvent.click(btnConfirmMove)

      await waitFor(() => {
        expect(opportunityApi.moveStage).toHaveBeenCalledWith('opp-1', 'stg-3', null)
      })

      // 2. Lưu trữ
      fireEvent.click(btnMenu)
      const btnArchive = screen.getByTestId('action-archive-opp-1')
      fireEvent.click(btnArchive)

      await waitFor(() => {
        expect(opportunityApi.archive).toHaveBeenCalledWith('opp-1')
      })

      // 3. Xoá
      fireEvent.click(btnMenu)
      const btnDelete = screen.getByTestId('action-delete-opp-1')
      fireEvent.click(btnDelete)

      await waitFor(() => {
        expect(opportunityApi.delete).toHaveBeenCalledWith('opp-1')
      })
    })

    it('chuyển sang giai đoạn Thất bại (Lost) bắt buộc nhập LostReason', async () => {
      render(
        <MemoryRouter initialEntries={['/tasks']}>
          <Routes>
            <Route path="/tasks" element={<TasksPage />} />
          </Routes>
        </MemoryRouter>
      )

      expect(await screen.findByTestId('opportunity-table')).toBeInTheDocument()

      // Open menu ⋮ and click Chuyển giai đoạn
      fireEvent.click(screen.getByTestId('btn-actions-opp-1'))
      fireEvent.click(screen.getByTestId('action-move-stage-opp-1'))

      // Select stg-4 (Thất bại / Lost)
      fireEvent.change(screen.getByTestId('select-target-stage'), { target: { value: 'stg-4' } })

      // Reason input appears
      const inputReason = await screen.findByTestId('input-lost-reason')
      expect(inputReason).toBeInTheDocument()

      // Attempt to submit without reason -> shows error, does not call API
      fireEvent.click(screen.getByTestId('btn-confirm-move-stage'))
      expect(opportunityApi.moveStage).not.toHaveBeenCalledWith('opp-1', 'stg-4', expect.anything())

      // Fill in reason and submit -> success
      fireEvent.change(inputReason, { target: { value: 'Khách hàng không đủ ngân sách' } })
      fireEvent.click(screen.getByTestId('btn-confirm-move-stage'))

      await waitFor(() => {
        expect(opportunityApi.moveStage).toHaveBeenCalledWith('opp-1', 'stg-4', 'Khách hàng không đủ ngân sách')
      })
    })
  })

  // =========================================================================
  // AC fd9216b3 (d) — Viewer: không có "+ Cơ hội", không menu ghi, drawer không nút sửa
  // =========================================================================
  describe('AC fd9216b3 (d) — Viewer Role Permissions', () => {
    it('Viewer: không có "+ Cơ hội", không có menu ghi ⋮, drawer không có nút sửa', async () => {
      // Set Viewer role
      useAuthStore.getState().setAuth('mock-token', {
        id: 'u-viewer',
        email: 'viewer@vni.local',
        userName: 'Viewer ReadOnly',
        roles: ['Viewer'],
      })

      render(
        <MemoryRouter initialEntries={['/tasks']}>
          <Routes>
            <Route path="/tasks" element={<TasksPage />} />
          </Routes>
        </MemoryRouter>
      )

      expect(await screen.findByTestId('opportunity-table')).toBeInTheDocument()

      // 1. Nút "+ Cơ hội" bị ẩn
      expect(screen.queryByTestId('btn-create-opportunity')).not.toBeInTheDocument()

      // 2. Cột hành động ⋮ bị ẩn
      expect(screen.queryByTestId('btn-actions-opp-1')).not.toBeInTheDocument()

      // 3. Click vào row mở Drawer, nhưng Drawer không có nút Sửa
      const row = screen.getByTestId('opp-row-opp-1')
      fireEvent.click(row)

      expect(await screen.findByTestId('opportunity-drawer')).toBeInTheDocument()
      expect(screen.queryByTestId('drawer-btn-edit')).not.toBeInTheDocument()
      expect(screen.queryByTestId('btn-add-watcher')).not.toBeInTheDocument()
    })
  })

  // =========================================================================
  // Customer Picker & Search (không dùng pageSize, dùng {keyword, index, size})
  // =========================================================================
  describe('Customer Picker trong Form Modal', () => {
    it('tìm kiếm khách qua customerApi.filter({keyword, index, size}), không dùng pageSize', async () => {
      render(
        <MemoryRouter initialEntries={['/tasks']}>
          <Routes>
            <Route path="/tasks" element={<TasksPage />} />
          </Routes>
        </MemoryRouter>
      )

      expect(await screen.findByTestId('opportunity-table')).toBeInTheDocument()

      // Open create modal
      const btnCreate = screen.getByTestId('btn-create-opportunity')
      fireEvent.click(btnCreate)

      expect(await screen.findByTestId('opportunity-form-modal')).toBeInTheDocument()

      // Open customer picker dropdown by typing in search
      const customerSearchInput = screen.getByTestId('input-customer-search')
      fireEvent.change(customerSearchInput, { target: { value: 'Minh' } })

      // Wait for debounce
      await waitFor(() => {
        expect(customerApi.filter).toHaveBeenCalledWith({
          keyword: 'Minh',
          index: 1,
          size: 20,
        })
      })

      // Verify pageSize was NOT passed
      const calls = customerApi.filter.mock.calls
      const lastCallArg = calls[calls.length - 1][0]
      expect(lastCallArg).not.toHaveProperty('pageSize')
    })
  })

  // =========================================================================
  // Drawer chi tiết: hiển thị thông tin, link /customers/:id, watchers
  // =========================================================================
  describe('Drawer chi tiết cơ hội', () => {
    it('bấm vào dòng mở drawer với link khách hàng và thông tin chi tiết', async () => {
      render(
        <MemoryRouter initialEntries={['/tasks']}>
          <Routes>
            <Route path="/tasks" element={<TasksPage />} />
          </Routes>
        </MemoryRouter>
      )

      expect(await screen.findByTestId('opportunity-table')).toBeInTheDocument()

      // Click row opp-1
      fireEvent.click(screen.getByTestId('opp-row-opp-1'))

      expect(await screen.findByTestId('opportunity-drawer')).toBeInTheDocument()
      expect(screen.getByText('👤 Học viên 1')).toBeInTheDocument()

      // Link to customer profile
      const custLink = screen.getByTestId('drawer-customer-link')
      expect(custLink).toHaveAttribute('href', '/customers/cust-1')

      // Close drawer
      fireEvent.click(screen.getByTestId('btn-close-drawer'))
      await waitFor(() => {
        expect(screen.queryByTestId('opportunity-drawer')).not.toBeInTheDocument()
      })
    })
  })

  // =========================================================================
  // State rỗng, đang tải, lỗi: KHÔNG có dữ liệu giả
  // =========================================================================
  describe('State rỗng, đang tải, lỗi (Không dữ liệu giả)', () => {
    it('hiển thị loading indicator khi đang tải, không có dòng dữ liệu giả', async () => {
      opportunityApi.filter.mockReturnValue(new Promise(() => {})) // pending promise

      render(
        <MemoryRouter initialEntries={['/tasks']}>
          <Routes>
            <Route path="/tasks" element={<TasksPage />} />
          </Routes>
        </MemoryRouter>
      )

      expect(await screen.findByTestId('table-loading-indicator')).toBeInTheDocument()
      expect(screen.queryByTestId('opp-row-opp-1')).not.toBeInTheDocument()
    })

    it('hiển thị empty state khi danh sách rỗng, không có dữ liệu giả', async () => {
      opportunityApi.filter.mockResolvedValue({
        items: [],
        total: 0,
        index: 1,
        size: 20,
      })

      render(
        <MemoryRouter initialEntries={['/tasks']}>
          <Routes>
            <Route path="/tasks" element={<TasksPage />} />
          </Routes>
        </MemoryRouter>
      )

      expect(await screen.findByTestId('table-empty-state')).toBeInTheDocument()
      expect(screen.queryByTestId('opp-row-opp-1')).not.toBeInTheDocument()
    })

    it('hiển thị error banner khi API lỗi, có nút thử lại', async () => {
      opportunityApi.filter.mockRejectedValue(new Error('Máy chủ đang bận'))

      render(
        <MemoryRouter initialEntries={['/tasks']}>
          <Routes>
            <Route path="/tasks" element={<TasksPage />} />
          </Routes>
        </MemoryRouter>
      )

      expect(await screen.findByTestId('table-error-banner')).toBeInTheDocument()
      expect(screen.getByText(/Máy chủ đang bận/)).toBeInTheDocument()
      expect(screen.getByTestId('btn-retry-table')).toBeInTheDocument()
    })
  })
})
