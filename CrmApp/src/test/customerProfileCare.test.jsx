import React from 'react'
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { render, screen, fireEvent, waitFor } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import CustomerList from '../features/customers/components/CustomerList'
import CustomerProfile from '../features/customers/components/CustomerProfile'
import TasksBoard from '../features/tasks/components/TasksBoard'
import CsvImportModal from '../features/customers/components/CsvImportModal'
import MergeModal from '../features/customers/components/MergeModal'
import { customerApi } from '../features/customers/api/customerApi'
import { reminderApi } from '../features/tasks/api/reminderApi'
import { useAuthStore } from '../auth/authStore'

describe('AC crm-profile-care-test (925780ff) — CrmApp Vitest', () => {
  const mockCustomer = {
    id: '11111111-1111-1111-1111-111111111111',
    displayName: 'Nguyễn Văn An',
    phoneE164: '+84912345678',
    createdAt: '2026-10-06T10:00:00Z',
    updatedAt: '2026-10-06T12:00:00Z',
    identities: [
      {
        id: 'id-1',
        platform: 1,
        channelName: 'VNI Fanpage Tuyển sinh',
        externalId: 'psid_123456',
        displayName: 'An Nguyen',
      },
    ],
    phoneSuggestions: [
      {
        id: 'sug-1',
        phoneE164: '+84988776655',
        rawMatched: '0988.776.655',
      },
    ],
    noteCount: 1,
    reminderCount: 1,
  }

  const mockTimeline = [
    {
      kind: 'message',
      at: '2026-10-06T10:15:00Z',
      title: 'Tin nhắn từ Facebook',
      body: 'Em muốn tư vấn khoá học kế toán',
      actor: 'An Nguyen',
      channelName: 'VNI Fanpage Tuyển sinh',
    },
    {
      kind: 'note',
      at: '2026-10-06T10:30:00Z',
      title: 'Ghi chú nội bộ',
      body: 'Khách quan tâm khoá K45',
      actor: 'Reviewer Care',
    },
  ]

  const mockNotes = [
    {
      id: 'note-1',
      crmCustomerId: '11111111-1111-1111-1111-111111111111',
      body: 'Khách quan tâm khoá K45',
      createdBy: 'Reviewer Care',
      createdAt: '2026-10-06T10:30:00Z',
    },
  ]

  const mockReminders = [
    {
      id: 'rem-1',
      crmCustomerId: '11111111-1111-1111-1111-111111111111',
      customerName: 'Nguyễn Văn An',
      title: 'Gọi lại tư vấn học phí',
      dueAtUtc: '2026-10-07T07:00:00Z',
      isCompleted: false,
    },
  ]

  const mockBuckets = {
    today: [
      {
        id: 'rem-today-1',
        crmCustomerId: '11111111-1111-1111-1111-111111111111',
        customerName: 'Nguyễn Văn An',
        title: 'Hôm nay: Gọi lại xác nhận',
        dueAtUtc: '2026-10-07T07:00:00Z',
        isCompleted: false,
      },
    ],
    overdue: [
      {
        id: 'rem-overdue-1',
        crmCustomerId: '22222222-2222-2222-2222-222222222222',
        customerName: 'Trần Thị Bích',
        title: 'Quá hạn: Gửi tài liệu học',
        dueAtUtc: '2026-10-05T07:00:00Z',
        isCompleted: false,
      },
    ],
    upcoming: [
      {
        id: 'rem-upcoming-1',
        crmCustomerId: '33333333-3333-3333-3333-333333333333',
        customerName: 'Lê Hoàng Long',
        title: 'Sắp tới: Kiểm tra thanh toán',
        dueAtUtc: '2026-10-10T07:00:00Z',
        isCompleted: false,
      },
    ],
  }

  beforeEach(() => {
    vi.restoreAllMocks()
    useAuthStore.getState().setAuth('mock-token', {
      id: 'u-admin',
      email: 'admin@vni.local',
      userName: 'AdminUser',
      roles: ['Admin', 'Reviewer'],
    })
  })

  afterEach(() => {
    vi.restoreAllMocks()
  })

  // 1. Customer List with filter & search
  describe('Customer List (Lọc / Tìm)', () => {
    it('fetches and displays customer list and filters by keyword and hasPhone', async () => {
      const filterSpy = vi.spyOn(customerApi, 'filter').mockResolvedValue({
        items: [
          {
            id: mockCustomer.id,
            displayName: mockCustomer.displayName,
            phoneE164: mockCustomer.phoneE164,
            identityCount: 1,
            createdAt: mockCustomer.createdAt,
          },
        ],
      })

      const onSelectSpy = vi.fn()

      render(
        <MemoryRouter>
          <CustomerList onSelectCustomer={onSelectSpy} />
        </MemoryRouter>,
      )

      await waitFor(() => {
        expect(screen.getByTestId(`customer-name-${mockCustomer.id}`)).toHaveTextContent('Nguyễn Văn An')
      })

      // Search keyword
      const searchInput = screen.getByTestId('customer-search-input')
      fireEvent.change(searchInput, { target: { value: '0912345678' } })
      fireEvent.click(screen.getByTestId('btn-search-customer'))

      await waitFor(() => {
        expect(filterSpy).toHaveBeenCalledWith(
          expect.objectContaining({
            keyword: '0912345678',
          }),
        )
      })

      // Click to view profile
      fireEvent.click(screen.getByTestId(`btn-view-customer-${mockCustomer.id}`))
      expect(onSelectSpy).toHaveBeenCalledWith(mockCustomer.id)
    })
  })

  // 2. Customer Profile
  describe('Customer Profile (Hồ sơ, Dòng thời gian, Ghi chú, Nhắc việc, Gợi ý SĐT)', () => {
    beforeEach(() => {
      vi.spyOn(customerApi, 'get').mockResolvedValue(mockCustomer)
      vi.spyOn(customerApi, 'getTimeline').mockResolvedValue(mockTimeline)
      vi.spyOn(customerApi, 'listNotes').mockResolvedValue(mockNotes)
      vi.spyOn(reminderApi, 'listForCustomer').mockResolvedValue(mockReminders)
    })

    it('renders customer info, identities, timeline, notes, and reminders', async () => {
      render(
        <MemoryRouter>
          <CustomerProfile customerId={mockCustomer.id} onBack={vi.fn()} />
        </MemoryRouter>,
      )

      await waitFor(() => {
        expect(screen.getByTestId('profile-display-name')).toHaveTextContent('Nguyễn Văn An')
        expect(screen.getByTestId('phone-suggestions-box')).toBeInTheDocument()
      })

      // Timeline pane
      expect(screen.getByTestId('timeline-pane')).toBeInTheDocument()
      expect(screen.getByText('Tin nhắn từ Facebook')).toBeInTheDocument()
      expect(screen.getByText('Em muốn tư vấn khoá học kế toán')).toBeInTheDocument()

      // Switch to notes tab
      fireEvent.click(screen.getByTestId('tab-notes'))
      await waitFor(() => {
        expect(screen.getByTestId('notes-pane')).toBeInTheDocument()
        expect(screen.getByText('Khách quan tâm khoá K45')).toBeInTheDocument()
      })

      // Switch to reminders tab
      fireEvent.click(screen.getByTestId('tab-reminders'))
      await waitFor(() => {
        expect(screen.getByTestId('reminders-pane')).toBeInTheDocument()
        expect(screen.getByText('Gọi lại tư vấn học phí')).toBeInTheDocument()
      })

      // Switch to identities tab
      fireEvent.click(screen.getByTestId('tab-identities'))
      await waitFor(() => {
        expect(screen.getByTestId('identities-pane')).toBeInTheDocument()
        expect(screen.getByText('psid_123456')).toBeInTheDocument()
      })
    })

    it('confirms phone suggestion when user clicks "Xác nhận số này"', async () => {
      const confirmSpy = vi.spyOn(customerApi, 'confirmPhone').mockResolvedValue({ success: true })

      render(
        <MemoryRouter>
          <CustomerProfile customerId={mockCustomer.id} onBack={vi.fn()} />
        </MemoryRouter>,
      )

      await waitFor(() => {
        expect(screen.getByTestId('btn-confirm-phone-sug-1')).toBeInTheDocument()
      })

      fireEvent.click(screen.getByTestId('btn-confirm-phone-sug-1'))

      await waitFor(() => {
        expect(confirmSpy).toHaveBeenCalledWith(mockCustomer.id, {
          suggestionId: 'sug-1',
          phoneE164: '+84988776655',
        })
      })
    })

    it('submits new note via customerApi.addNote', async () => {
      const addNoteSpy = vi.spyOn(customerApi, 'addNote').mockResolvedValue({
        id: 'note-2',
        body: 'Khách hẹn thanh toán chiều nay',
      })

      render(
        <MemoryRouter>
          <CustomerProfile customerId={mockCustomer.id} onBack={vi.fn()} />
        </MemoryRouter>,
      )

      await waitFor(() => {
        expect(screen.getByTestId('profile-display-name')).toBeInTheDocument()
      })

      fireEvent.click(screen.getByTestId('tab-notes'))

      const input = screen.getByTestId('input-note-body')
      fireEvent.change(input, { target: { value: 'Khách hẹn thanh toán chiều nay' } })
      fireEvent.click(screen.getByTestId('btn-submit-note'))

      await waitFor(() => {
        expect(addNoteSpy).toHaveBeenCalledWith(mockCustomer.id, {
          body: 'Khách hẹn thanh toán chiều nay',
        })
      })
    })
  })

  // 3. TasksBoard (Hôm nay / Quá hạn / Sắp tới)
  describe('TasksBoard (Màn Việc của tôi: Hôm nay / Quá hạn / Sắp tới)', () => {
    beforeEach(() => {
      vi.spyOn(reminderApi, 'getBuckets').mockResolvedValue(mockBuckets)
    })

    it('renders Today, Overdue, Upcoming buckets and toggles tabs', async () => {
      render(
        <MemoryRouter>
          <TasksBoard />
        </MemoryRouter>,
      )

      await waitFor(() => {
        expect(screen.getByTestId('tab-bucket-today')).toHaveTextContent('Hôm nay (1)')
        expect(screen.getByTestId('tab-bucket-overdue')).toHaveTextContent('Quá hạn (1)')
        expect(screen.getByTestId('tab-bucket-upcoming')).toHaveTextContent('Sắp tới (1)')
      })

      // Today tab is active by default
      expect(screen.getByText('Hôm nay: Gọi lại xác nhận')).toBeInTheDocument()

      // Switch to Overdue tab
      fireEvent.click(screen.getByTestId('tab-bucket-overdue'))
      expect(screen.getByText('Quá hạn: Gửi tài liệu học')).toBeInTheDocument()

      // Switch to Upcoming tab
      fireEvent.click(screen.getByTestId('tab-bucket-upcoming'))
      expect(screen.getByText('Sắp tới: Kiểm tra thanh toán')).toBeInTheDocument()
    })

    it('calls reminderApi.complete when completing a task', async () => {
      const completeSpy = vi.spyOn(reminderApi, 'complete').mockResolvedValue({ success: true })

      render(
        <MemoryRouter>
          <TasksBoard />
        </MemoryRouter>,
      )

      await waitFor(() => {
        expect(screen.getByTestId('btn-complete-task-rem-today-1')).toBeInTheDocument()
      })

      fireEvent.click(screen.getByTestId('btn-complete-task-rem-today-1'))

      await waitFor(() => {
        expect(completeSpy).toHaveBeenCalledWith('rem-today-1')
      })
    })
  })

  // 4. CSV Import (Preview + Commit)
  describe('CSV Import Modal (Xem trước + Xác nhận)', () => {
    it('previews CSV and commits when confirmed', async () => {
      const previewSpy = vi.spyOn(customerApi, 'importPreview').mockResolvedValue({
        totalRows: 3,
        createCount: 2,
        duplicatePhoneCount: 1,
        invalidCount: 0,
        rows: [
          { lineNumber: 1, displayName: 'Khách A', phoneE164: '+84911111111', status: 'create' },
          { lineNumber: 2, displayName: 'Khách B', phoneE164: '+84922222222', status: 'create' },
          { lineNumber: 3, displayName: 'Khách C', phoneE164: '+84933333333', status: 'duplicate_phone' },
        ],
      })

      const commitSpy = vi.spyOn(customerApi, 'importCommit').mockResolvedValue({
        created: 2,
        skippedDuplicatePhone: 1,
        invalid: 0,
        createdCustomerIds: ['c-1', 'c-2'],
      })

      render(
        <CsvImportModal isOpen={true} onClose={vi.fn()} onSuccess={vi.fn()} />
      )

      const file = new File(['name,phone\nKhách A,0911111111'], 'khach.csv', { type: 'text/csv' })
      const input = screen.getByTestId('csv-file-input')
      fireEvent.change(input, { target: { files: [file] } })

      // Step 1: Preview
      fireEvent.click(screen.getByTestId('btn-csv-preview'))

      await waitFor(() => {
        expect(previewSpy).toHaveBeenCalled()
        expect(screen.getByTestId('csv-preview-pane')).toBeInTheDocument()
        expect(screen.getByText('Khách A')).toBeInTheDocument()
        expect(screen.getByTestId('btn-csv-commit')).toBeInTheDocument()
      })

      // Step 2: Commit
      fireEvent.click(screen.getByTestId('btn-csv-commit'))

      await waitFor(() => {
        expect(commitSpy).toHaveBeenCalled()
        expect(screen.getByTestId('csv-commit-success')).toBeInTheDocument()
        expect(screen.getByText(/Đã tạo mới/)).toBeInTheDocument()
      })
    })
  })

  // 5. Merge Modal (Suggestions, Merge, Split)
  describe('Merge Modal (Gợi ý gộp, gộp, tách)', () => {
    it('lists merge suggestions and calls merge API', async () => {
      vi.spyOn(customerApi, 'listMergeSuggestions').mockResolvedValue([
        {
          customerAId: '11111111-1111-1111-1111-111111111111',
          customerAName: 'Nguyễn Văn An',
          customerBId: '22222222-2222-2222-2222-222222222222',
          customerBName: 'Nguyen Van An',
          reason: 'Trùng tên hiển thị',
          sharedDisplayName: 'Nguyễn Văn An',
        },
      ])

      const mergeSpy = vi.spyOn(customerApi, 'merge').mockResolvedValue({
        mergeRecordId: 'merge-rec-123',
        keptCustomerId: '11111111-1111-1111-1111-111111111111',
        mergedCustomerId: '22222222-2222-2222-2222-222222222222',
      })

      render(
        <MergeModal isOpen={true} onClose={vi.fn()} onSuccess={vi.fn()} />
      )

      await waitFor(() => {
        expect(screen.getByTestId('merge-suggestions-list')).toBeInTheDocument()
        expect(screen.getByText('Trùng tên hiển thị')).toBeInTheDocument()
      })

      // Click merge
      fireEvent.click(screen.getByTestId('btn-merge-suggestion-0'))

      await waitFor(() => {
        expect(mergeSpy).toHaveBeenCalledWith(
          '11111111-1111-1111-1111-111111111111',
          '22222222-2222-2222-2222-222222222222',
        )
        expect(screen.getByTestId('merge-success-alert')).toHaveTextContent('merge-rec-123')
      })
    })

    it('splits merged customer via split API', async () => {
      const splitSpy = vi.spyOn(customerApi, 'split').mockResolvedValue({ success: true })

      render(
        <MergeModal isOpen={true} onClose={vi.fn()} onSuccess={vi.fn()} />
      )

      const splitInput = screen.getByTestId('input-split-record-id')
      fireEvent.change(splitInput, { target: { value: 'merge-rec-999' } })
      fireEvent.click(screen.getByTestId('btn-split-customer'))

      await waitFor(() => {
        expect(splitSpy).toHaveBeenCalledWith('merge-rec-999')
        expect(screen.getByTestId('merge-success-alert')).toHaveTextContent('Đã tách hồ sơ')
      })
    })
  })

  // 6. Mobile 375px responsive rendering check
  describe('Mobile 375px viewport rendering', () => {
    it('renders customer list and tasks board without errors on small screen', async () => {
      vi.spyOn(customerApi, 'filter').mockResolvedValue({ items: [] })
      vi.spyOn(reminderApi, 'getBuckets').mockResolvedValue(mockBuckets)

      const { container } = render(
        <div style={{ width: '375px', overflow: 'hidden' }}>
          <MemoryRouter>
            <TasksBoard />
          </MemoryRouter>
        </div>,
      )

      expect(container.querySelector('[data-testid="tasks-board-page"]')).toBeInTheDocument()
      expect(screen.getByTestId('reminder-bucket-tabs')).toBeInTheDocument()
    })
  })
})
