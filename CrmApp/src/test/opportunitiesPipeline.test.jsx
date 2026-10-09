import React from 'react'
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { render, screen, fireEvent, waitFor } from '@testing-library/react'
import { MemoryRouter, Routes, Route, useLocation } from 'react-router-dom'
import { TasksPage } from '../modules/tasks/TasksPage'
import { opportunityApi } from '../features/opportunities/api/opportunityApi'
import { useAuthStore } from '../auth/authStore'

describe('AC fd9216b3 (c)(d) — Opportunities Pipeline Kanban', () => {
  const mockStages = [
    { id: 'stg-1', name: 'Mới', kind: 1, sortOrder: 1, color: '#3b82f6' },
    { id: 'stg-2', name: 'Đủ điều kiện', kind: 1, sortOrder: 2, color: '#6366f1' },
    { id: 'stg-3', name: 'Đã mua', kind: 2, sortOrder: 5, color: '#10b981' },
    { id: 'stg-4', name: 'Thất bại', kind: 3, sortOrder: 6, color: '#ef4444' },
  ]

  const mockUsers = [
    { id: 'u-1', userName: 'sale1', displayName: 'Nguyễn Văn Sale' },
  ]

  const mockStats = {
    total: 4,
    open: 2,
    won: 1,
    lost: 1,
    activity: 5,
    rev: 25000000,
  }

  const createMockPipelineData = () => {
    return {
      columns: [
        {
          stageId: 'stg-1',
          stageName: 'Mới',
          stageColor: '#3b82f6',
          kind: 1,
          sortOrder: 1,
          total: 2,
          items: [
            {
              id: 'opp-1',
              title: 'Cơ hội khoá IELTS VIP',
              crmCustomerId: 'cust-1',
              customerName: 'Nguyễn Tuấn Anh',
              customerPhoneE164: '+84901234567',
              stageId: 'stg-1',
              stageName: 'Mới',
              stageColor: '#3b82f6',
              stageKind: 1,
              status: 1,
              isArchived: false,
              assigneeUserId: 'u-1',
              assignedTo: 'Nguyễn Văn Sale',
              expectedValue: 15000000,
              source: 2, // Message
              channelPlatform: 1,
              channelName: 'Fanpage Tuyển sinh',
              pageConversationId: 'conv-101',
              sourceSnippet: 'Em muốn đăng ký học thử IELTS tuần này ạ',
              lastActivityAtUtc: new Date().toISOString(), // Hôm nay
              createdAt: '2026-10-08T00:00:00Z',
            },
            {
              id: 'opp-2',
              title: 'Cơ hội khoá Giao tiếp',
              crmCustomerId: 'cust-2',
              customerName: 'Lê Thuỳ Linh',
              customerPhoneE164: '+84912345678',
              stageId: 'stg-1',
              stageName: 'Mới',
              stageColor: '#3b82f6',
              stageKind: 1,
              status: 1,
              isArchived: false,
              assigneeUserId: 'u-1',
              assignedTo: 'Nguyễn Văn Sale',
              expectedValue: 8000000,
              source: 1, // Manual
              channelPlatform: null,
              channelName: null,
              lastActivityAtUtc: null,
              createdAt: '2026-10-08T00:00:00Z',
            },
          ],
        },
        {
          stageId: 'stg-2',
          stageName: 'Đủ điều kiện',
          stageColor: '#6366f1',
          kind: 1,
          sortOrder: 2,
          total: 1,
          items: [
            {
              id: 'opp-3',
              title: 'Cơ hội khoá TOEIC',
              crmCustomerId: 'cust-3',
              customerName: 'Trần Văn Cường',
              customerPhoneE164: '+84923456789',
              stageId: 'stg-2',
              stageName: 'Đủ điều kiện',
              stageColor: '#6366f1',
              stageKind: 1,
              status: 1,
              isArchived: false,
              assigneeUserId: 'u-1',
              assignedTo: 'Nguyễn Văn Sale',
              expectedValue: 5000000,
              source: 3, // Comment
              channelPlatform: 1,
              channelName: 'Fanpage Tuyển sinh',
              pageConversationId: 'post-1',
              socialCommentId: 'comm-202',
              sourceSnippet: 'Cho mình xin học phí khoá này với',
              lastActivityAtUtc: new Date(Date.now() - 86400000).toISOString(), // Hôm qua
              createdAt: '2026-10-07T00:00:00Z',
            },
          ],
        },
        {
          stageId: 'stg-3',
          stageName: 'Đã mua',
          stageColor: '#10b981',
          kind: 2,
          sortOrder: 5,
          total: 1,
          items: [
            {
              id: 'opp-4',
              title: 'Cơ hội khoá Luyện đề',
              crmCustomerId: 'cust-4',
              customerName: 'Hoàng Mai',
              customerPhoneE164: '+84934567890',
              stageId: 'stg-3',
              stageName: 'Đã mua',
              stageColor: '#10b981',
              stageKind: 2,
              status: 2,
              isArchived: false,
              assigneeUserId: 'u-1',
              assignedTo: 'Nguyễn Văn Sale',
              expectedValue: 2000000,
              source: 1,
              lastActivityAtUtc: '2026-10-01T00:00:00Z',
              createdAt: '2026-10-01T00:00:00Z',
            },
          ],
        },
        {
          stageId: 'stg-4',
          stageName: 'Thất bại',
          stageColor: '#ef4444',
          kind: 3,
          sortOrder: 6,
          total: 0,
          items: [],
        },
      ],
    }
  }

  // Component to inspect URL
  const LocationTracker = () => {
    const loc = useLocation()
    return <div data-testid="location-tracker">{loc.pathname}{loc.search}</div>
  }

  beforeEach(() => {
    vi.restoreAllMocks()
    localStorage.clear()

    useAuthStore.getState().setAuth('mock-token', {
      id: 'u-admin',
      email: 'admin@vni.local',
      userName: 'Admin User',
      roles: ['Admin', 'ContentManager'],
    })

    vi.spyOn(opportunityApi, 'listStages').mockResolvedValue(mockStages)
    vi.spyOn(opportunityApi, 'listUsers').mockResolvedValue(mockUsers)
    vi.spyOn(opportunityApi, 'stats').mockResolvedValue(mockStats)
    vi.spyOn(opportunityApi, 'pipeline').mockResolvedValue(createMockPipelineData())
    vi.spyOn(opportunityApi, 'pipelineStage').mockResolvedValue({
      items: [
        {
          id: 'opp-extra-1',
          title: 'Cơ hội tải thêm',
          crmCustomerId: 'cust-99',
          customerName: 'Khách hàng trang 2',
          customerPhoneE164: '+84999999999',
          stageId: 'stg-1',
          stageName: 'Mới',
          stageColor: '#3b82f6',
          stageKind: 1,
          status: 1,
          isArchived: false,
          expectedValue: 1000000,
          source: 1,
        },
      ],
      total: 3,
      index: 2,
      size: 20,
    })
    vi.spyOn(opportunityApi, 'moveStage').mockResolvedValue({ success: true })
    vi.spyOn(opportunityApi, 'get').mockResolvedValue({ id: 'opp-1', title: 'Cơ hội khoá IELTS VIP' })
  })

  afterEach(() => {
    vi.restoreAllMocks()
    localStorage.clear()
  })

  // =========================================================================
  // AC fd9216b3 (c) — Pipeline: cột theo giai đoạn, kéo thả, LostReasonDialog,
  //                   tải thêm theo cột, snippet điều hướng inbox
  // =========================================================================
  describe('AC fd9216b3 (c) — Pipeline Columns, Cards, Drag & Drop, Snippet, Load More', () => {
    it('đủ cột theo giai đoạn kèm count đúng', async () => {
      render(
        <MemoryRouter initialEntries={['/tasks?view=pipeline']}>
          <Routes>
            <Route path="/tasks" element={<TasksPage />} />
          </Routes>
        </MemoryRouter>
      )

      expect(await screen.findByTestId('opportunity-pipeline')).toBeInTheDocument()

      // 4 stage columns
      expect(screen.getByTestId('pipeline-column-stg-1')).toBeInTheDocument()
      expect(screen.getByTestId('pipeline-column-stg-2')).toBeInTheDocument()
      expect(screen.getByTestId('pipeline-column-stg-3')).toBeInTheDocument()
      expect(screen.getByTestId('pipeline-column-stg-4')).toBeInTheDocument()

      // Count badge on columns
      expect(screen.getByTestId('stage-count-stg-1')).toHaveTextContent('2')
      expect(screen.getByTestId('stage-count-stg-2')).toHaveTextContent('1')
      expect(screen.getByTestId('stage-count-stg-3')).toHaveTextContent('1')
      expect(screen.getByTestId('stage-count-stg-4')).toHaveTextContent('0')
    })

    it('thẻ hiển thị đầy đủ thông tin: tên, khách, SĐT, giá trị, hoạt động cuối tiếng Việt', async () => {
      render(
        <MemoryRouter initialEntries={['/tasks?view=pipeline']}>
          <Routes>
            <Route path="/tasks" element={<TasksPage />} />
          </Routes>
        </MemoryRouter>
      )

      expect(await screen.findByTestId('opp-card-opp-1')).toBeInTheDocument()

      // Title
      expect(screen.getByTestId('card-title-opp-1')).toHaveTextContent('Cơ hội khoá IELTS VIP')
      // Customer
      expect(screen.getByTestId('card-customer-opp-1')).toHaveTextContent('Nguyễn Tuấn Anh')
      // Phone
      expect(screen.getByTestId('card-phone-opp-1')).toHaveTextContent('+84901234567')
      // Value VND
      expect(screen.getByTestId('card-value-opp-1')).toHaveTextContent(/15\.000\.000/)
      // Relative activity time: "Hôm nay"
      expect(screen.getByTestId('card-last-activity-opp-1')).toHaveTextContent(/Hôm nay/)

      // opp-2 last activity is null -> "Chưa có hoạt động"
      expect(screen.getByTestId('card-last-activity-opp-2')).toHaveTextContent('Chưa có hoạt động')

      // opp-3 last activity was yesterday -> "Hôm qua"
      expect(screen.getByTestId('card-last-activity-opp-3')).toHaveTextContent('Hôm qua')
    })

    it('bấm tin nhắn gần nhất trên thẻ thì mở hội thoại trong /inbox?kind=…&id=…', async () => {
      render(
        <MemoryRouter initialEntries={['/tasks?view=pipeline']}>
          <Routes>
            <Route path="/tasks" element={<TasksPage />} />
          </Routes>
        </MemoryRouter>
      )

      expect(await screen.findByTestId('opp-card-opp-1')).toBeInTheDocument()

      // Snippet bubble for message (source = 2 -> kind = message)
      const snippetLink = screen.getByTestId('card-snippet-opp-1')
      expect(snippetLink).toHaveTextContent('Em muốn đăng ký học thử IELTS tuần này ạ')
      expect(snippetLink).toHaveAttribute('href', '/inbox?kind=message&id=conv-101')

      // Snippet bubble for comment (source = 3 -> kind = comment)
      const commentSnippetLink = screen.getByTestId('card-snippet-opp-3')
      expect(commentSnippetLink).toHaveTextContent('Cho mình xin học phí khoá này với')
      expect(commentSnippetLink).toHaveAttribute('href', '/inbox?kind=comment&id=comm-202')
    })

    it('kéo thẻ sang cột khác gọi move-stage với stageId đích (cập nhật lạc quan)', async () => {
      render(
        <MemoryRouter initialEntries={['/tasks?view=pipeline']}>
          <Routes>
            <Route path="/tasks" element={<TasksPage />} />
          </Routes>
        </MemoryRouter>
      )

      expect(await screen.findByTestId('opp-card-opp-1')).toBeInTheDocument()

      // Drag opp-1
      const card = screen.getByTestId('opp-card-opp-1')
      const dataTransfer = {
        setData: vi.fn(),
        getData: vi.fn().mockReturnValue('opp-1'),
        effectAllowed: '',
        dropEffect: '',
      }

      fireEvent.dragStart(card, { dataTransfer })

      // Drop on column stg-2 (Đủ điều kiện)
      const dropZoneStg2 = screen.getByTestId('column-drop-zone-stg-2')
      fireEvent.dragOver(dropZoneStg2, { dataTransfer })
      fireEvent.drop(dropZoneStg2, { dataTransfer })

      // Calls opportunityApi.moveStage with targetStageId 'stg-2' and null lostReason
      await waitFor(() => {
        expect(opportunityApi.moveStage).toHaveBeenCalledWith('opp-1', 'stg-2', null)
      })

      // Column stg-2 count increases to 2, stg-1 count decreases to 1
      expect(screen.getByTestId('stage-count-stg-2')).toHaveTextContent('2')
      expect(screen.getByTestId('stage-count-stg-1')).toHaveTextContent('1')
    })

    it('thả vào Lost: hiện ô lý do, huỷ thì KHÔNG gọi API, xác nhận thì gọi move-stage kèm lý do', async () => {
      render(
        <MemoryRouter initialEntries={['/tasks?view=pipeline']}>
          <Routes>
            <Route path="/tasks" element={<TasksPage />} />
          </Routes>
        </MemoryRouter>
      )

      expect(await screen.findByTestId('opp-card-opp-1')).toBeInTheDocument()

      const card = screen.getByTestId('opp-card-opp-1')
      const dataTransfer = {
        setData: vi.fn(),
        getData: vi.fn().mockReturnValue('opp-1'),
        effectAllowed: '',
        dropEffect: '',
      }

      // DragStart
      fireEvent.dragStart(card, { dataTransfer })

      // Drop into stg-4 (Thất bại / Kind = 3)
      const dropZoneStg4 = screen.getByTestId('column-drop-zone-stg-4')
      fireEvent.dragOver(dropZoneStg4, { dataTransfer })
      fireEvent.drop(dropZoneStg4, { dataTransfer })

      // LostReasonDialog appears
      expect(await screen.findByTestId('lost-reason-dialog')).toBeInTheDocument()
      expect(opportunityApi.moveStage).not.toHaveBeenCalled()

      // 1. Click Huỷ -> modal closes, API NOT called, card remains in stg-1
      const btnCancel = screen.getByTestId('btn-cancel-lost-reason')
      fireEvent.click(btnCancel)

      await waitFor(() => {
        expect(screen.queryByTestId('lost-reason-dialog')).not.toBeInTheDocument()
      })
      expect(opportunityApi.moveStage).not.toHaveBeenCalled()
      expect(screen.getByTestId('stage-count-stg-1')).toHaveTextContent('2')

      // 2. Drop into stg-4 again and submit reason
      fireEvent.dragStart(card, { dataTransfer })
      fireEvent.drop(dropZoneStg4, { dataTransfer })

      expect(await screen.findByTestId('lost-reason-dialog')).toBeInTheDocument()
      const inputReason = screen.getByTestId('input-lost-reason')
      fireEvent.change(inputReason, { target: { value: 'Khách hàng chuyển sang học offline đối thủ' } })

      const btnConfirm = screen.getByTestId('btn-confirm-lost-reason')
      fireEvent.click(btnConfirm)

      await waitFor(() => {
        expect(opportunityApi.moveStage).toHaveBeenCalledWith(
          'opp-1',
          'stg-4',
          'Khách hàng chuyển sang học offline đối thủ'
        )
      })

      // Modal is closed
      expect(screen.queryByTestId('lost-reason-dialog')).not.toBeInTheDocument()
    })

    it('API lỗi khi kéo thả: rollback thẻ về cột cũ và hiển thị toast', async () => {
      opportunityApi.moveStage.mockRejectedValue(new Error('Mạng không ổn định'))

      render(
        <MemoryRouter initialEntries={['/tasks?view=pipeline']}>
          <Routes>
            <Route path="/tasks" element={<TasksPage />} />
          </Routes>
        </MemoryRouter>
      )

      expect(await screen.findByTestId('opp-card-opp-1')).toBeInTheDocument()

      const card = screen.getByTestId('opp-card-opp-1')
      const dataTransfer = {
        setData: vi.fn(),
        getData: vi.fn().mockReturnValue('opp-1'),
        effectAllowed: '',
        dropEffect: '',
      }

      fireEvent.dragStart(card, { dataTransfer })

      const dropZoneStg2 = screen.getByTestId('column-drop-zone-stg-2')
      fireEvent.drop(dropZoneStg2, { dataTransfer })

      // Toast appears
      expect(await screen.findByTestId('pipeline-toast')).toBeInTheDocument()
      expect(screen.getByTestId('pipeline-toast')).toHaveTextContent(/Mạng không ổn định/)

      // Card is restored to original column stg-1
      expect(screen.getByTestId('stage-count-stg-1')).toHaveTextContent('2')
      expect(screen.getByTestId('stage-count-stg-2')).toHaveTextContent('1')
    })

    it('tải thêm theo cột qua POST pipeline/{stageId}', async () => {
      // Mock column stg-1 has total 3, but only 2 loaded
      const pipelineData = createMockPipelineData()
      pipelineData.columns[0].total = 3
      opportunityApi.pipeline.mockResolvedValue(pipelineData)

      render(
        <MemoryRouter initialEntries={['/tasks?view=pipeline']}>
          <Routes>
            <Route path="/tasks" element={<TasksPage />} />
          </Routes>
        </MemoryRouter>
      )

      expect(await screen.findByTestId('btn-load-more-stg-1')).toBeInTheDocument()
      expect(screen.getByTestId('btn-load-more-stg-1')).toHaveTextContent('Tải thêm (2/3)')

      // Click Load More
      fireEvent.click(screen.getByTestId('btn-load-more-stg-1'))

      await waitFor(() => {
        expect(opportunityApi.pipelineStage).toHaveBeenCalledWith(
          'stg-1',
          expect.objectContaining({
            index: 2,
            size: 20,
          })
        )
      })

      // Extra card is now visible
      expect(await screen.findByTestId('opp-card-opp-extra-1')).toBeInTheDocument()
      // Load more button disappears since 3/3 loaded
      expect(screen.queryByTestId('btn-load-more-stg-1')).not.toBeInTheDocument()
    })

    it('menu "Chuyển giai đoạn" trên thẻ hỗ trợ thao tác bàn phím/mobile', async () => {
      render(
        <MemoryRouter initialEntries={['/tasks?view=pipeline']}>
          <Routes>
            <Route path="/tasks" element={<TasksPage />} />
          </Routes>
        </MemoryRouter>
      )

      expect(await screen.findByTestId('btn-card-menu-opp-1')).toBeInTheDocument()

      // Open quick menu on card
      fireEvent.click(screen.getByTestId('btn-card-menu-opp-1'))

      expect(await screen.findByTestId('card-menu-opp-1')).toBeInTheDocument()

      // Click move to stg-2
      const btnMoveToStg2 = screen.getByTestId('action-card-move-to-stg-2')
      fireEvent.click(btnMoveToStg2)

      await waitFor(() => {
        expect(opportunityApi.moveStage).toHaveBeenCalledWith('opp-1', 'stg-2', null)
      })
    })

    it('nút "+" trên cột mở form tạo cơ hội với stageId điền sẵn', async () => {
      render(
        <MemoryRouter initialEntries={['/tasks?view=pipeline']}>
          <Routes>
            <Route path="/tasks" element={<TasksPage />} />
          </Routes>
        </MemoryRouter>
      )

      expect(await screen.findByTestId('btn-add-opp-stage-stg-2')).toBeInTheDocument()

      // Click "+" on stage 2
      fireEvent.click(screen.getByTestId('btn-add-opp-stage-stg-2'))

      // OpportunityFormModal opens with stage stg-2 selected
      expect(await screen.findByTestId('opportunity-form-modal')).toBeInTheDocument()
      const stageSelect = screen.getByTestId('select-form-stage')
      expect(stageSelect).toHaveValue('stg-2')
    })
  })

  // =========================================================================
  // AC fd9216b3 (d) — Viewer: draggable=false, không nút "+", không menu ghi
  // =========================================================================
  describe('AC fd9216b3 (d) — Viewer Role Restrictions on Pipeline', () => {
    it('Viewer: không kéo thả được, không nút tạo mới, không menu ghi', async () => {
      useAuthStore.getState().setAuth('mock-token', {
        id: 'u-viewer',
        email: 'viewer@vni.local',
        userName: 'Viewer ReadOnly',
        roles: ['Viewer'],
      })

      render(
        <MemoryRouter initialEntries={['/tasks?view=pipeline']}>
          <Routes>
            <Route path="/tasks" element={<TasksPage />} />
          </Routes>
        </MemoryRouter>
      )

      expect(await screen.findByTestId('opportunity-pipeline')).toBeInTheDocument()

      // 1. Cards have draggable=false
      const card = screen.getByTestId('opp-card-opp-1')
      expect(card).toHaveAttribute('draggable', 'false')

      // 2. No "+ Cơ hội" toolbar button
      expect(screen.queryByTestId('btn-create-opportunity')).not.toBeInTheDocument()

      // 3. No "+" on column headers
      expect(screen.queryByTestId('btn-add-opp-stage-stg-1')).not.toBeInTheDocument()
      expect(screen.queryByTestId('btn-add-opp-stage-stg-2')).not.toBeInTheDocument()

      // 4. No action menu on cards
      expect(screen.queryByTestId('btn-card-menu-opp-1')).not.toBeInTheDocument()
    })
  })
})
