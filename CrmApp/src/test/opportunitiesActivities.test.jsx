import React from 'react'
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { render, screen, fireEvent, waitFor, act } from '@testing-library/react'
import { MemoryRouter, Routes, Route, useLocation } from 'react-router-dom'
import TasksBoard from '../features/tasks/components/TasksBoard'
import { reminderApi } from '../features/tasks/api/reminderApi'
import { customerApi } from '../features/customers/api/customerApi'
import { opportunityApi } from '../features/opportunities/api/opportunityApi'
import { useAuthStore } from '../auth/authStore'
import OpportunitiesFeature from '../features/opportunities/OpportunitiesFeature'
import OpportunityDrawer from '../features/opportunities/components/OpportunityDrawer'

const deferred = () => {
  let resolve, reject
  const promise = new Promise((res, rej) => {
    resolve = res
    reject = rej
  })
  return { promise, resolve, reject }
}

describe('AC 176ae13f (c)(d) & AC fd9216b3 (b) — Tab Hoạt động & Nhắc việc gắn Cơ hội', () => {
  beforeEach(() => {
    vi.restoreAllMocks()
    localStorage.clear()

    useAuthStore.getState().setAuth('mock-token', {
      id: 'u-reviewer',
      email: 'reviewer@vni.local',
      userName: 'Reviewer Care',
      roles: ['Reviewer'],
    })

    vi.spyOn(opportunityApi, 'listStages').mockResolvedValue([
      { id: 'stg-1', name: 'Mới', color: '#3b82f6', kind: 1, sortOrder: 1 },
      { id: 'stg-2', name: 'Đủ điều kiện', color: '#6366f1', kind: 1, sortOrder: 2 },
    ])
    vi.spyOn(opportunityApi, 'listUsers').mockResolvedValue([
      { id: 'u-1', userName: 'sale1', displayName: 'Nguyễn Văn Sale' },
    ])
    vi.spyOn(opportunityApi, 'stats').mockResolvedValue({
      total: 5,
      open: 3,
      won: 1,
      lost: 1,
      activity: 4,
      rev: 10000000,
    })
    vi.spyOn(opportunityApi, 'filter').mockResolvedValue({ items: [], total: 0 })
  })

  afterEach(() => {
    vi.restoreAllMocks()
  })

  // =========================================================================
  // AC 176ae13f (c) — Không có dữ liệu giả lúc tải; lỗi thì danh sách rỗng
  // =========================================================================
  describe('AC 176ae13f (c) — Không còn dữ liệu nhắc việc giả & xử lý lỗi', () => {
    it('lúc đang tải: KHÔNG có chữ "Gọi điện tư vấn lộ trình học cho bạn An" hay dữ liệu giả', async () => {
      const def = deferred()
      vi.spyOn(reminderApi, 'getBuckets').mockImplementation(() => def.promise)

      render(
        <MemoryRouter>
          <TasksBoard />
        </MemoryRouter>
      )

      // Trong lúc đang tải (loading)
      expect(screen.getByTestId('tasks-loading')).toBeInTheDocument()
      expect(
        screen.queryByText(/Gọi điện tư vấn lộ trình học cho bạn An/i)
      ).not.toBeInTheDocument()
      expect(screen.queryByText(/Nguyễn Văn An/i)).not.toBeInTheDocument()

      // Giải quyết promise rỗng
      await act(async () => {
        def.resolve({ today: [], overdue: [], upcoming: [] })
      })

      expect(screen.getByTestId('tasks-empty')).toBeInTheDocument()
      expect(
        screen.queryByText(/Gọi điện tư vấn lộ trình học cho bạn An/i)
      ).not.toBeInTheDocument()
    })

    it('API lỗi: chỉ hiện thông báo lỗi, danh sách việc rỗng', async () => {
      vi.spyOn(reminderApi, 'getBuckets').mockRejectedValue(
        new Error('Lỗi kết nối cơ sở dữ liệu nhắc việc')
      )

      render(
        <MemoryRouter>
          <TasksBoard />
        </MemoryRouter>
      )

      await waitFor(() => {
        expect(
          screen.getByText(/Lỗi kết nối cơ sở dữ liệu nhắc việc/i)
        ).toBeInTheDocument()
      })

      // Danh sách việc phải rỗng, không hiện bất kỳ card việc giả nào
      expect(
        screen.queryByText(/Gọi điện tư vấn lộ trình học cho bạn An/i)
      ).not.toBeInTheDocument()
      expect(screen.getByTestId('tasks-empty')).toBeInTheDocument()
    })
  })

  // =========================================================================
  // AC 176ae13f (d) — Picker khách gọi {keyword, index, size}, KHÔNG có pageSize
  // =========================================================================
  describe('AC 176ae13f (d) — Ô chọn khách hàng tìm kiếm phía server', () => {
    it('gọi customerApi.filter với {keyword, index, size}, KHÔNG có pageSize; gõ keyword gọi server tìm khách ngoài 20 khách đầu', async () => {
      const filterSpy = vi.spyOn(customerApi, 'filter').mockResolvedValue({
        items: [
          { id: 'c-1', displayName: 'Khách 1', phoneE164: '+84900000001' },
          { id: 'c-2', displayName: 'Khách 2', phoneE164: '+84900000002' },
        ],
        total: 50,
      })
      vi.spyOn(reminderApi, 'getBuckets').mockResolvedValue({
        today: [],
        overdue: [],
        upcoming: [],
      })

      render(
        <MemoryRouter>
          <TasksBoard />
        </MemoryRouter>
      )

      await waitFor(() => {
        expect(screen.getByTestId('btn-create-task')).toBeInTheDocument()
      })

      // Bấm "Tạo nhắc việc"
      fireEvent.click(screen.getByTestId('btn-create-task'))
      expect(screen.getByTestId('create-reminder-modal')).toBeInTheDocument()

      // Kiểm tra lần gọi ban đầu
      await waitFor(() => {
        expect(filterSpy).toHaveBeenCalledWith(
          expect.objectContaining({ index: 1, size: 20 })
        )
      })

      // Tuyệt đối không được chứa pageSize
      const lastCallArg = filterSpy.mock.calls[0][0]
      expect(lastCallArg).not.toHaveProperty('pageSize')

      // Mock kết quả tìm kiếm theo keyword cho khách thứ 35
      filterSpy.mockResolvedValueOnce({
        items: [
          {
            id: 'c-35',
            displayName: 'Trần Văn Đặc Biệt (Khách 35)',
            phoneE164: '+84935000035',
          },
        ],
        total: 1,
      })

      // Gõ keyword tìm kiếm
      const searchInput = screen.getByTestId('input-customer-search')
      fireEvent.change(searchInput, { target: { value: 'Đặc Biệt' } })

      // Chờ debounce và kiểm tra gọi lại server
      await waitFor(() => {
        expect(filterSpy).toHaveBeenCalledWith(
          expect.objectContaining({
            keyword: 'Đặc Biệt',
            index: 1,
            size: 20,
          })
        )
      })

      // Kiểm tra khách thứ 35 xuất hiện trong select
      await waitFor(() => {
        expect(
          screen.getByText(/Trần Văn Đặc Biệt \(Khách 35\)/i)
        ).toBeInTheDocument()
      })
    })

    it('chọn cơ hội tuỳ chọn khi tạo nhắc việc và hiển thị tên cơ hội trên card', async () => {
      vi.spyOn(customerApi, 'filter').mockResolvedValue({
        items: [{ id: 'cust-10', displayName: 'Nguyễn Văn Mười' }],
        total: 1,
      })
      const oppFilterSpy = vi.spyOn(opportunityApi, 'filter').mockResolvedValue({
        items: [
          { id: 'opp-101', title: 'Cơ hội khoá Fullstack Web' },
          { id: 'opp-102', title: 'Cơ hội khoá DevOps' },
        ],
        total: 2,
      })
      const createReminderSpy = vi.spyOn(reminderApi, 'create').mockResolvedValue({
        id: 'rem-new-1',
        title: 'Gọi chốt học phí Fullstack',
      })
      vi.spyOn(reminderApi, 'getBuckets').mockResolvedValue({
        today: [
          {
            id: 'rem-99',
            title: 'Tư vấn lộ trình học',
            customerName: 'Nguyễn Văn Mười',
            opportunityTitle: 'Cơ hội khoá Fullstack Web',
            dueAtUtc: '2026-10-09T10:00:00Z',
            isCompleted: false,
          },
        ],
        overdue: [],
        upcoming: [],
      })

      render(
        <MemoryRouter>
          <TasksBoard />
        </MemoryRouter>
      )

      // Card hiển thị tên cơ hội với badge
      await waitFor(() => {
        expect(screen.getByTestId('reminder-opportunity-rem-99')).toHaveTextContent(
          'Cơ hội khoá Fullstack Web'
        )
      })

      // Mở modal tạo nhắc việc
      fireEvent.click(screen.getByTestId('btn-create-task'))

      // Chọn khách hàng cust-10
      const customerSelect = screen.getByTestId('select-reminder-customer')
      await waitFor(() => {
        expect(screen.getByText('Nguyễn Văn Mười')).toBeInTheDocument()
      })
      fireEvent.change(customerSelect, { target: { value: 'cust-10' } })

      // Kiểm tra load cơ hội của khách hàng
      await waitFor(() => {
        expect(oppFilterSpy).toHaveBeenCalledWith(
          expect.objectContaining({ customerId: 'cust-10' })
        )
        expect(
          screen.getByText('Cơ hội khoá Fullstack Web')
        ).toBeInTheDocument()
      })

      // Chọn cơ hội
      const oppSelect = screen.getByTestId('select-reminder-opportunity')
      fireEvent.change(oppSelect, { target: { value: 'opp-101' } })

      // Nhập tiêu đề và hạn chót
      fireEvent.change(screen.getByTestId('input-new-reminder-title'), {
        target: { value: 'Gọi chốt học phí Fullstack' },
      })
      fireEvent.change(screen.getByTestId('input-new-reminder-due'), {
        target: { value: '2026-10-10T14:30' },
      })

      // Lưu nhắc việc
      fireEvent.click(screen.getByTestId('btn-save-reminder'))

      await waitFor(() => {
        expect(createReminderSpy).toHaveBeenCalledWith(
          expect.objectContaining({
            crmCustomerId: 'cust-10',
            crmOpportunityId: 'opp-101',
            title: 'Gọi chốt học phí Fullstack',
          })
        )
      })
    })
  })

  // =========================================================================
  // AC fd9216b3 (b) — Tab Hoạt động trong OpportunitiesFeature render TasksBoard
  // =========================================================================
  describe('Tab Hoạt động trong OpportunitiesFeature', () => {
    it('tab Hoạt động hiển thị TasksBoard trong data-testid="opportunity-activities"', async () => {
      vi.spyOn(reminderApi, 'getBuckets').mockResolvedValue({
        today: [
          {
            id: 'rem-act-1',
            title: 'Gặp mặt trao đổi hợp đồng',
            customerName: 'Hoàng Anh Tuấn',
            dueAtUtc: '2026-10-09T08:00:00Z',
            isCompleted: false,
          },
        ],
        overdue: [],
        upcoming: [],
      })

      render(
        <MemoryRouter initialEntries={['/tasks?tab=activities']}>
          <OpportunitiesFeature />
        </MemoryRouter>
      )

      // Pane Hoạt động xuất hiện và chứa TasksBoard
      expect(
        await screen.findByTestId('opportunity-activities')
      ).toBeInTheDocument()
      expect(screen.getByTestId('tasks-board-page')).toBeInTheDocument()
      expect(
        screen.getByText('Gặp mặt trao đổi hợp đồng')
      ).toBeInTheDocument()
    })
  })

  // =========================================================================
  // OpportunityDrawer — Danh sách nhắc việc của cơ hội & tạo nhắc việc gắn cơ hội
  // =========================================================================
  describe('OpportunityDrawer — Nhắc việc của cơ hội', () => {
    it('hiển thị danh sách nhắc việc của cơ hội và cho phép tạo nhắc việc mới', async () => {
      vi.spyOn(opportunityApi, 'get').mockResolvedValue({
        id: 'opp-50',
        title: 'Cơ hội khoá AI Engineer',
        crmCustomerId: 'cust-50',
        customerName: 'Nguyễn Thị Bích',
        expectedValue: 15000000,
        stageId: 'stg-1',
        status: 1,
        watcherUserIds: [],
      })
      const listOppRemindersSpy = vi
        .spyOn(reminderApi, 'listForOpportunity')
        .mockResolvedValue([
          {
            id: 'rem-opp-1',
            title: 'Gửi đề cương khoá AI',
            dueAtUtc: '2026-10-10T09:00:00Z',
            isCompleted: false,
          },
        ])
      const createReminderSpy = vi
        .spyOn(reminderApi, 'create')
        .mockResolvedValue({
          id: 'rem-opp-2',
          title: 'Gọi demo dự án mẫu',
        })
      const completeReminderSpy = vi
        .spyOn(reminderApi, 'complete')
        .mockResolvedValue({ success: true })

      const onUpdatedMock = vi.fn()

      render(
        <MemoryRouter>
          <OpportunityDrawer
            opportunityId="opp-50"
            isOpen={true}
            onClose={vi.fn()}
            onOpportunityUpdated={onUpdatedMock}
          />
        </MemoryRouter>
      )

      // Hiển thị card nhắc việc và gọi listForOpportunity
      expect(
        await screen.findByTestId('drawer-reminders-card')
      ).toBeInTheDocument()
      expect(listOppRemindersSpy).toHaveBeenCalledWith('opp-50')
      expect(screen.getByText('Gửi đề cương khoá AI')).toBeInTheDocument()

      // Bấm hoàn thành nhắc việc
      const btnComplete = screen.getByTestId(
        'btn-complete-drawer-reminder-rem-opp-1'
      )
      fireEvent.click(btnComplete)
      await waitFor(() => {
        expect(completeReminderSpy).toHaveBeenCalledWith('rem-opp-1')
        expect(onUpdatedMock).toHaveBeenCalled()
      })

      // Mở form tạo nhắc việc mới
      fireEvent.click(screen.getByTestId('btn-add-opp-reminder-toggle'))
      expect(screen.getByTestId('form-add-opp-reminder')).toBeInTheDocument()

      fireEvent.change(screen.getByTestId('input-drawer-reminder-title'), {
        target: { value: 'Gọi demo dự án mẫu' },
      })
      fireEvent.change(screen.getByTestId('input-drawer-reminder-due'), {
        target: { value: '2026-10-11T15:00' },
      })

      // Lưu
      fireEvent.click(screen.getByTestId('btn-save-drawer-reminder'))

      await waitFor(() => {
        expect(createReminderSpy).toHaveBeenCalledWith(
          expect.objectContaining({
            crmCustomerId: 'cust-50',
            crmOpportunityId: 'opp-50',
            title: 'Gọi demo dự án mẫu',
          })
        )
      })
    })
  })
})
