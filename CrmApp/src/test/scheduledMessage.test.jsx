import React from 'react'
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { render, screen, fireEvent, waitFor } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import InboxDetail from '../features/inbox/components/InboxDetail'
import { InboxList } from '../features/inbox/components/InboxList'
import { scheduledMessageApi } from '../features/inbox/api/scheduledMessageApi'
import { useAuthStore } from '../auth/authStore'
import crmApi from '../api/crmApi'

describe('Requirement 47c67ee0, AC dc3224c7 (a)-(d) — Hẹn giờ gửi tin nhắn Messenger', () => {
  const mockMessageItem = {
    id: '11111111-2222-3333-4444-555555555555',
    kind: 1, // Message
    socialChannelId: 'aaaa1111-bb22-cc33-dd44-ee5555555555',
    channelName: 'VNI Fanpage Tuyển sinh',
    displayName: 'Nguyễn Văn An',
    snippet: 'Em muốn hỏi về học phí khóa K45',
    lastCustomerActivityAt: '2026-10-07T06:00:00Z',
    status: 1, // Mới
    assignedUserId: null,
    assignedTo: null,
    unreadCount: 0,
    canReply: true,
    hasPendingScheduled: false,
    replyWindowClosesAt: '2026-10-08T06:00:00Z',
    tags: [],
  }

  const mockCommentItem = {
    id: '22222222-3333-4444-5555-666666666666',
    kind: 2, // Comment
    socialChannelId: 'aaaa1111-bb22-cc33-dd44-ee5555555555',
    channelName: 'VNI Fanpage Tuyển sinh',
    displayName: 'Trần Thị Bích',
    snippet: 'Khóa học này có cấp chứng chỉ không ạ?',
    lastCustomerActivityAt: '2026-10-07T05:30:00Z',
    status: 2,
    assignedUserId: null,
    assignedTo: null,
    unreadCount: 0,
    canReply: true,
    hasPendingScheduled: false,
    tags: [],
  }

  const mockOpenMessageDetail = {
    kind: 1,
    conversation: {
      id: mockMessageItem.id,
      participantName: 'Nguyễn Văn An',
      channelName: 'VNI Fanpage Tuyển sinh',
      canReply: true,
      isReplyWindowOpen: true,
      replyWindowClosesAt: new Date(Date.now() + 24 * 3600 * 1000).toISOString(),
      inboxStatus: 1,
      messages: [
        {
          id: 'msg-1',
          text: 'Em muốn hỏi về học phí khóa K45',
          isFromPage: false,
          sentAt: '2026-10-07T06:00:00Z',
        },
      ],
    },
    tags: [],
    replyEndpoint: `/api/PageMessage/${mockMessageItem.id}/send`,
  }

  const mockClosedMessageDetail = {
    kind: 1,
    conversation: {
      id: mockMessageItem.id,
      participantName: 'Nguyễn Văn An',
      channelName: 'VNI Fanpage Tuyển sinh',
      canReply: false,
      isReplyWindowOpen: false,
      replyWindowClosesAt: '2026-10-07T05:00:00Z',
      inboxStatus: 1,
      messages: [],
    },
    tags: [],
    replyEndpoint: `/api/PageMessage/${mockMessageItem.id}/send`,
  }

  const mockCommentDetail = {
    kind: 2,
    thread: {
      id: mockCommentItem.id,
      authorName: 'Trần Thị Bích',
      message: 'Khóa học này có cấp chứng chỉ không ạ?',
      commentedAt: '2026-10-07T05:30:00Z',
      capabilities: { canReply: true },
      replies: [],
    },
    tags: [],
    replyEndpoint: `/api/SocialComment/${mockCommentItem.id}/reply`,
  }

  beforeEach(() => {
    vi.restoreAllMocks()
    useAuthStore.getState().setAuth('mock-token', {
      id: 'u-reviewer',
      userName: 'Reviewer Care',
      roles: ['Reviewer'],
    })
  })

  afterEach(() => {
    vi.restoreAllMocks()
  })

  describe('scheduledMessageApi client', () => {
    it('listByConversation calls GET /CrmScheduledMessage/by-conversation/:id', async () => {
      const getSpy = vi.spyOn(crmApi, 'get').mockResolvedValue({
        data: { success: true, data: [{ id: 'sched-1' }] },
      })
      const res = await scheduledMessageApi.listByConversation('conv-123')
      expect(getSpy).toHaveBeenCalledWith('/CrmScheduledMessage/by-conversation/conv-123')
      expect(res).toEqual([{ id: 'sched-1' }])
    })

    it('create calls POST /CrmScheduledMessage with payload', async () => {
      const postSpy = vi.spyOn(crmApi, 'post').mockResolvedValue({
        data: { success: true, data: { id: 'sched-new' } },
      })
      const res = await scheduledMessageApi.create({
        pageConversationId: 'conv-123',
        text: 'Nội dung hẹn giờ',
        scheduledAtUtc: '2026-10-07T08:00:00Z',
      })
      expect(postSpy).toHaveBeenCalledWith('/CrmScheduledMessage', {
        pageConversationId: 'conv-123',
        text: 'Nội dung hẹn giờ',
        scheduledAtUtc: '2026-10-07T08:00:00Z',
      })
      expect(res).toEqual({ id: 'sched-new' })
    })

    it('update calls PUT /CrmScheduledMessage/:id', async () => {
      const putSpy = vi.spyOn(crmApi, 'put').mockResolvedValue({
        data: { success: true, data: { id: 'sched-1', text: 'Sửa' } },
      })
      const res = await scheduledMessageApi.update('sched-1', {
        text: 'Sửa',
        scheduledAtUtc: '2026-10-07T09:00:00Z',
      })
      expect(putSpy).toHaveBeenCalledWith('/CrmScheduledMessage/sched-1', {
        text: 'Sửa',
        scheduledAtUtc: '2026-10-07T09:00:00Z',
      })
      expect(res).toEqual({ id: 'sched-1', text: 'Sửa' })
    })

    it('cancel calls POST /CrmScheduledMessage/:id/cancel', async () => {
      const postSpy = vi.spyOn(crmApi, 'post').mockResolvedValue({
        data: { success: true, data: 'Đã huỷ' },
      })
      const res = await scheduledMessageApi.cancel('sched-1')
      expect(postSpy).toHaveBeenCalledWith('/CrmScheduledMessage/sched-1/cancel')
      expect(res).toBe('Đã huỷ')
    })
  })

  // (a) Nút "Hẹn giờ gửi" có với Admin/Reviewer khi cửa sổ mở. Bị khoá khi đang tải, chi tiết lỗi hoặc cửa sổ đóng. Không có với Viewer. Không có cho bình luận.
  describe('AC dc3224c7 (a) — Điều kiện hiển thị và khoá nút "Hẹn giờ gửi"', () => {
    it('hiển thị và bật (enabled) với Reviewer/Admin khi cửa sổ 24h mở', () => {
      vi.spyOn(scheduledMessageApi, 'listByConversation').mockResolvedValue([])

      render(
        <MemoryRouter>
          <InboxDetail
            item={mockMessageItem}
            detail={mockOpenMessageDetail}
            loading={false}
          />
        </MemoryRouter>,
      )

      const btnSchedule = screen.getByTestId('btn-schedule-send')
      expect(btnSchedule).toBeInTheDocument()
      expect(btnSchedule).toBeEnabled()
      expect(btnSchedule).toHaveTextContent('Hẹn giờ gửi')
    })

    it('bị khoá (disabled) khi đang tải (loading=true)', () => {
      render(
        <MemoryRouter>
          <InboxDetail
            item={mockMessageItem}
            detail={mockOpenMessageDetail}
            loading={true}
          />
        </MemoryRouter>,
      )

      const btnSchedule = screen.getByTestId('btn-schedule-send')
      expect(btnSchedule).toBeDisabled()
    })

    it('bị khoá (disabled) khi chi tiết lỗi / null (detail=null)', () => {
      render(
        <MemoryRouter>
          <InboxDetail
            item={mockMessageItem}
            detail={null}
            loading={false}
          />
        </MemoryRouter>,
      )

      const btnSchedule = screen.getByTestId('btn-schedule-send')
      expect(btnSchedule).toBeDisabled()
    })

    it('bị khoá (disabled) khi cửa sổ 24h đã đóng (canReply=false / isReplyWindowOpen=false)', () => {
      render(
        <MemoryRouter>
          <InboxDetail
            item={mockMessageItem}
            detail={mockClosedMessageDetail}
            loading={false}
          />
        </MemoryRouter>,
      )

      const btnSchedule = screen.getByTestId('btn-schedule-send')
      expect(btnSchedule).toBeDisabled()
    })

    it('không hiển thị (not in document) với Viewer', () => {
      useAuthStore.getState().setAuth('mock-token', {
        id: 'u-viewer',
        userName: 'Viewer User',
        roles: ['Viewer'],
      })

      render(
        <MemoryRouter>
          <InboxDetail
            item={mockMessageItem}
            detail={mockOpenMessageDetail}
            loading={false}
          />
        </MemoryRouter>,
      )

      expect(screen.queryByTestId('btn-schedule-send')).not.toBeInTheDocument()
    })

    it('không hiển thị (not in document) cho bình luận (item.kind = 2)', () => {
      render(
        <MemoryRouter>
          <InboxDetail
            item={mockCommentItem}
            detail={mockCommentDetail}
            loading={false}
          />
        </MemoryRouter>,
      )

      expect(screen.queryByTestId('btn-schedule-send')).not.toBeInTheDocument()
    })
  })

  // (b) Chọn gợi ý "+1 giờ" rồi xác nhận → gọi createScheduled với text và ISO UTC đúng. Ô soạn bị xoá. Giờ chọn vượt giờ cửa sổ đóng → nút xác nhận bị khoá và hiện "Cửa sổ 24h đóng lúc …".
  describe('AC dc3224c7 (b) — Popover hẹn giờ, gợi ý +1h và kiểm tra cửa sổ 24h', () => {
    it('chọn gợi ý "+1 giờ" rồi xác nhận → gọi createScheduled với text và ISO UTC đúng, ô soạn bị xoá', async () => {
      const createSpy = vi.spyOn(scheduledMessageApi, 'create').mockResolvedValue({
        id: 'sched-created-1',
        pageConversationId: mockMessageItem.id,
        text: 'Em kiểm tra lại thông tin học phí nhé!',
        scheduledAtUtc: '2026-10-07T07:00:00.000Z',
        status: 1,
      })
      vi.spyOn(scheduledMessageApi, 'listByConversation').mockResolvedValue([])

      render(
        <MemoryRouter>
          <InboxDetail
            item={mockMessageItem}
            detail={mockOpenMessageDetail}
            loading={false}
          />
        </MemoryRouter>,
      )

      // Type text into reply input
      const replyInput = screen.getByTestId('reply-input')
      fireEvent.change(replyInput, { target: { value: 'Em kiểm tra lại thông tin học phí nhé!' } })
      expect(replyInput.value).toBe('Em kiểm tra lại thông tin học phí nhé!')

      // Click "Hẹn giờ gửi"
      const btnSchedule = screen.getByTestId('btn-schedule-send')
      fireEvent.click(btnSchedule)

      // Popover modal appears
      expect(screen.getByTestId('schedule-message-modal')).toBeInTheDocument()

      // Click quick option "+1 giờ"
      const beforeClick = Date.now()
      const btnPlus1h = screen.getByTestId('quick-schedule-1h')
      fireEvent.click(btnPlus1h)

      // Confirm
      const btnConfirm = screen.getByTestId('btn-confirm-schedule')
      expect(btnConfirm).toBeEnabled()
      fireEvent.click(btnConfirm)

      await waitFor(() => {
        expect(createSpy).toHaveBeenCalledWith({
          pageConversationId: mockMessageItem.id,
          text: 'Em kiểm tra lại thông tin học phí nhé!',
          scheduledAtUtc: expect.any(String),
        })
      })

      // Scheduled time should be ~1h in future (+/- 1 minute)
      const calledArg = createSpy.mock.calls[0][0]
      const scheduledMs = new Date(calledArg.scheduledAtUtc).getTime()
      const diffMinutes = Math.round((scheduledMs - beforeClick) / 60000)
      expect(diffMinutes).toBeGreaterThanOrEqual(59)
      expect(diffMinutes).toBeLessThanOrEqual(61)

      // Composer input cleared
      expect(replyInput.value).toBe('')

      // Modal closed
      await waitFor(() => {
        expect(screen.queryByTestId('schedule-message-modal')).not.toBeInTheDocument()
      })
    })

    it('giờ chọn vượt quá cửa sổ 24h đóng → nút xác nhận bị khoá và hiện "Cửa sổ 24h đóng lúc …"', async () => {
      // Window closes in 1 hour from now
      const windowCloses = new Date(Date.now() + 60 * 60 * 1000).toISOString()
      const detailWithShortWindow = {
        kind: 1,
        conversation: {
          ...mockOpenMessageDetail.conversation,
          replyWindowClosesAt: windowCloses,
        },
      }
      vi.spyOn(scheduledMessageApi, 'listByConversation').mockResolvedValue([])

      render(
        <MemoryRouter>
          <InboxDetail
            item={mockMessageItem}
            detail={detailWithShortWindow}
            loading={false}
          />
        </MemoryRouter>,
      )

      const replyInput = screen.getByTestId('reply-input')
      fireEvent.change(replyInput, { target: { value: 'Tin nhắn gửi muộn' } })

      fireEvent.click(screen.getByTestId('btn-schedule-send'))

      // Click "+3 giờ" (which exceeds 1h window)
      fireEvent.click(screen.getByTestId('quick-schedule-3h'))

      // Check banner displays "Cửa sổ 24h đóng lúc …"
      expect(screen.getByTestId('schedule-window-info')).toHaveTextContent(/Cửa sổ 24h đóng lúc/)

      // Confirm button must be disabled!
      const btnConfirm = screen.getByTestId('btn-confirm-schedule')
      expect(btnConfirm).toBeDisabled()
    })
  })

  // (c) Danh sách tin hẹn giờ hiện Pending kèm Sửa/Huỷ. Huỷ → gọi cancel và tin biến khỏi danh sách. Failed hiện lý do.
  describe('AC dc3224c7 (c) — Danh sách tin hẹn giờ, Sửa/Huỷ và hiển thị lỗi Failed', () => {
    it('hiển thị danh sách tin hẹn giờ: Pending có nút Sửa/Huỷ; Huỷ → gọi cancel và tin biến khỏi danh sách; Failed hiện lý do', async () => {
      const mockPendingItem = {
        id: 'sched-item-pending',
        pageConversationId: mockMessageItem.id,
        text: 'Nhắc khách nộp hồ sơ trước 17h',
        scheduledAtUtc: '2026-10-07T09:00:00Z',
        status: 1, // Pending
        error: null,
      }

      const mockFailedItem = {
        id: 'sched-item-failed',
        pageConversationId: mockMessageItem.id,
        text: 'Tin nhắn gửi thất bại',
        scheduledAtUtc: '2026-10-07T08:00:00Z',
        status: 4, // Failed
        error: 'Cửa sổ 24h đã đóng khi tới giờ gửi',
      }

      let currentList = [mockPendingItem, mockFailedItem]
      vi.spyOn(scheduledMessageApi, 'listByConversation').mockImplementation(async () => currentList)
      const cancelSpy = vi.spyOn(scheduledMessageApi, 'cancel').mockImplementation(async (id) => {
        currentList = currentList.filter((m) => m.id !== id)
        return { success: true }
      })

      render(
        <MemoryRouter>
          <InboxDetail
            item={mockMessageItem}
            detail={mockOpenMessageDetail}
            loading={false}
          />
        </MemoryRouter>,
      )

      // Both items rendered in scheduled messages list
      await waitFor(() => {
        expect(screen.getByTestId(`scheduled-item-${mockPendingItem.id}`)).toBeInTheDocument()
        expect(screen.getByTestId(`scheduled-item-${mockFailedItem.id}`)).toBeInTheDocument()
      })

      // Pending item has text, status "Chờ gửi", and Sửa / Huỷ buttons
      const pendingEl = screen.getByTestId(`scheduled-item-${mockPendingItem.id}`)
      expect(pendingEl).toHaveTextContent('Nhắc khách nộp hồ sơ trước 17h')
      expect(pendingEl).toHaveTextContent('Chờ gửi')
      expect(screen.getByTestId(`btn-edit-scheduled-${mockPendingItem.id}`)).toBeInTheDocument()
      expect(screen.getByTestId(`btn-cancel-scheduled-${mockPendingItem.id}`)).toBeInTheDocument()

      // Failed item has status "Thất bại" and error message
      const failedEl = screen.getByTestId(`scheduled-item-${mockFailedItem.id}`)
      expect(failedEl).toHaveTextContent('Thất bại')
      expect(screen.getByTestId(`scheduled-error-${mockFailedItem.id}`)).toHaveTextContent(
        'Cửa sổ 24h đã đóng khi tới giờ gửi',
      )

      // Click "Huỷ" on pending item
      fireEvent.click(screen.getByTestId(`btn-cancel-scheduled-${mockPendingItem.id}`))

      await waitFor(() => {
        expect(cancelSpy).toHaveBeenCalledWith(mockPendingItem.id)
      })

      // Pending item disappears from the list
      await waitFor(() => {
        expect(screen.queryByTestId(`scheduled-item-${mockPendingItem.id}`)).not.toBeInTheDocument()
      })

      // Failed item still remains
      expect(screen.getByTestId(`scheduled-item-${mockFailedItem.id}`)).toBeInTheDocument()
    })

    it('Viewer không thấy nút Sửa và Huỷ trong danh sách tin hẹn giờ', async () => {
      useAuthStore.getState().setAuth('mock-token', {
        id: 'u-viewer',
        userName: 'Viewer ReadOnly',
        roles: ['Viewer'],
      })

      const mockPendingItem = {
        id: 'sched-viewer-test',
        pageConversationId: mockMessageItem.id,
        text: 'Nội dung tin chờ gửi',
        scheduledAtUtc: '2026-10-07T09:00:00Z',
        status: 1,
      }

      render(
        <MemoryRouter>
          <InboxDetail
            item={mockMessageItem}
            detail={mockOpenMessageDetail}
            loading={false}
          />
        </MemoryRouter>,
      )

      // Viewer should not see edit/cancel buttons
      expect(screen.queryByTestId(`btn-edit-scheduled-${mockPendingItem.id}`)).not.toBeInTheDocument()
      expect(screen.queryByTestId(`btn-cancel-scheduled-${mockPendingItem.id}`)).not.toBeInTheDocument()
    })
  })

  // (d) Item hội thoại có tin Pending → có icon đồng hồ.
  describe('AC dc3224c7 (d) — Icon đồng hồ trên item hội thoại trong InboxList', () => {
    it('hiển thị icon đồng hồ khi hasPendingScheduled = true, và ẩn khi false', () => {
      const itemWithPending = {
        ...mockMessageItem,
        id: 'item-has-pending',
        displayName: 'Khách hàng có tin hẹn giờ',
        hasPendingScheduled: true,
      }

      const itemWithoutPending = {
        ...mockMessageItem,
        id: 'item-no-pending',
        displayName: 'Khách hàng không có tin hẹn giờ',
        hasPendingScheduled: false,
      }

      render(
        <InboxList
          items={[itemWithPending, itemWithoutPending]}
          selectedId={null}
          onSelectItem={() => {}}
        />,
      )

      // itemWithPending has clock icon
      const iconItem1 = screen.getByTestId(`scheduled-icon-${itemWithPending.id}`)
      expect(iconItem1).toBeInTheDocument()
      expect(iconItem1).toHaveTextContent('⏰')

      // itemWithoutPending does NOT have clock icon
      expect(screen.queryByTestId(`scheduled-icon-${itemWithoutPending.id}`)).not.toBeInTheDocument()
    })
  })
})
