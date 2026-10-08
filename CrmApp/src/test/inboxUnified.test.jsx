import React from 'react'
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { render, screen, fireEvent, waitFor, act } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import InboxFeature from '../features/inbox/InboxFeature'
import { inboxApi } from '../features/inbox/api/inboxApi'
import { useAuthStore } from '../auth/authStore'

describe('AC crm-inbox-list-test (56154ec6) — Unified Inbox Vitest', () => {
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
    unreadCount: 2,
    canReply: true,
    tags: [{ id: 'tag-1', name: 'Tuyển sinh', color: '#3B82F6' }],
  }

  const mockCommentItem = {
    id: '22222222-3333-4444-5555-666666666666',
    kind: 2, // Comment
    socialChannelId: 'aaaa1111-bb22-cc33-dd44-ee5555555555',
    channelName: 'VNI Fanpage Tuyển sinh',
    displayName: 'Trần Thị Bích',
    snippet: 'Khóa học này có cấp chứng chỉ không ạ?',
    lastCustomerActivityAt: '2026-10-07T05:30:00Z',
    status: 2, // Đang xử lý
    assignedUserId: 'u-reviewer',
    assignedTo: 'Reviewer Care',
    unreadCount: 0,
    canReply: true,
    tags: [],
  }

  const mockMessageDetailOpen = {
    kind: 1,
    conversation: {
      id: mockMessageItem.id,
      participantName: 'Nguyễn Văn An',
      channelName: 'VNI Fanpage Tuyển sinh',
      canReply: true,
      isReplyWindowOpen: true,
      inboxStatus: 1,
      internalNote: 'Khách tiềm năng',
      messages: [
        {
          id: 'msg-1',
          text: 'Em muốn hỏi về học phí khóa K45',
          isFromPage: false,
          sentAt: '2026-10-07T06:00:00Z',
        },
      ],
    },
    tags: [{ id: 'tag-1', name: 'Tuyển sinh', color: '#3B82F6' }],
    replyEndpoint: `/api/PageMessage/${mockMessageItem.id}/send`,
  }

  const mockMessageDetailLocked = {
    kind: 1,
    conversation: {
      id: mockMessageItem.id,
      participantName: 'Nguyễn Văn An',
      channelName: 'VNI Fanpage Tuyển sinh',
      canReply: false,
      isReplyWindowOpen: false,
      inboxStatus: 1,
      messages: [
        {
          id: 'msg-old',
          text: 'Tin nhắn từ 2 ngày trước',
          isFromPage: false,
          sentAt: '2026-10-05T01:00:00Z',
        },
      ],
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
      postMessage: 'Khai giảng khóa kế toán thực hành tháng 10/2026',
      postPermalinkUrl: 'https://facebook.com/posts/123456',
      likeCount: 5,
      commentedAt: '2026-10-07T05:30:00Z',
      capabilities: { canReply: true },
      replies: [
        {
          id: 'rep-1',
          authorName: 'VNI Fanpage Tuyển sinh',
          message: 'Chào bạn, sau khóa học bạn sẽ được cấp chứng chỉ tốt nghiệp nhé!',
          isFromPage: true,
          commentedAt: '2026-10-07T05:45:00Z',
        },
      ],
    },
    tags: [],
    replyEndpoint: `/api/SocialComment/${mockCommentItem.id}/reply`,
  }

  const mockUsers = [
    { id: 'u-reviewer', displayName: 'Reviewer Care', userName: 'reviewer', roles: ['Reviewer'], isActive: true },
    { id: 'u-admin', displayName: 'Admin User', userName: 'admin', roles: ['Admin'], isActive: true },
  ]

  const mockTags = [
    { id: 'tag-1', name: 'Tuyển sinh', color: '#3B82F6' },
    { id: 'tag-2', name: 'Học phí', color: '#10B981' },
  ]

  const mockChannels = [
    { id: 'aaaa1111-bb22-cc33-dd44-ee5555555555', pageName: 'VNI Fanpage Tuyển sinh' },
  ]

  beforeEach(() => {
    vi.restoreAllMocks()
    useAuthStore.getState().setAuth('mock-token', {
      id: 'u-reviewer',
      email: 'reviewer@vni.local',
      userName: 'Reviewer Care',
      roles: ['Reviewer'],
    })

    vi.spyOn(inboxApi, 'listUsers').mockResolvedValue(mockUsers)
    vi.spyOn(inboxApi, 'listTags').mockResolvedValue(mockTags)
    vi.spyOn(inboxApi, 'listChannels').mockResolvedValue(mockChannels)
  })

  afterEach(() => {
    vi.restoreAllMocks()
  })

  // 1. Render Unified List & Filters
  it('renders unified list of messages and comments with filters', async () => {
    const filterSpy = vi.spyOn(inboxApi, 'filter').mockResolvedValue({
      items: [mockMessageItem, mockCommentItem],
      total: 2,
    })

    render(
      <MemoryRouter>
        <InboxFeature />
      </MemoryRouter>,
    )

    await waitFor(() => {
      expect(screen.getByTestId(`conv-item-${mockMessageItem.id}`)).toBeInTheDocument()
      expect(screen.getByTestId(`conv-item-${mockCommentItem.id}`)).toBeInTheDocument()
    })

    expect(screen.getAllByText('💬 Tin nhắn').length).toBeGreaterThan(0)
    expect(screen.getAllByText('📝 Bình luận').length).toBeGreaterThan(0)
    expect(screen.getByText('Tuyển sinh')).toBeInTheDocument()
    expect(screen.getAllByText('👤 Reviewer Care').length).toBeGreaterThan(0)

    // Test filter kind: click 'Bình luận'
    fireEvent.click(screen.getByTestId('filter-kind-comment'))
    await waitFor(() => {
      expect(filterSpy).toHaveBeenCalledWith(
        expect.objectContaining({ kind: 2 }),
      )
    })

    // Test unread only toggle
    fireEvent.click(screen.getByTestId('filter-unread-only'))
    await waitFor(() => {
      expect(filterSpy).toHaveBeenCalledWith(
        expect.objectContaining({ unreadOnly: true }),
      )
    })

    // Test status filter dropdown
    fireEvent.change(screen.getByTestId('select-filter-status'), { target: { value: '1' } })
    await waitFor(() => {
      expect(filterSpy).toHaveBeenCalledWith(
        expect.objectContaining({ status: 1 }),
      )
    })
  })

  // 2. View message detail and reply within 24h
  it('displays conversation messages and sends reply when within 24h window', async () => {
    vi.spyOn(inboxApi, 'filter').mockResolvedValue({
      items: [mockMessageItem],
    })
    vi.spyOn(inboxApi, 'getMessage').mockResolvedValue(mockMessageDetailOpen)
    const sendSpy = vi.spyOn(inboxApi, 'sendMessage').mockResolvedValue({ success: true })

    render(
      <MemoryRouter>
        <InboxFeature />
      </MemoryRouter>,
    )

    await waitFor(() => {
      expect(screen.getByTestId(`conv-item-${mockMessageItem.id}`)).toBeInTheDocument()
    })

    // Click item to load detail
    fireEvent.click(screen.getByTestId(`conv-item-${mockMessageItem.id}`))

    await waitFor(() => {
      expect(screen.getByText('Em muốn hỏi về học phí khóa K45')).toBeInTheDocument()
      expect(screen.getByTestId('reply-form')).toBeInTheDocument()
      expect(screen.queryByTestId('reply-locked-24h')).not.toBeInTheDocument()
    })

    // Enter reply and submit
    fireEvent.change(screen.getByTestId('reply-input'), {
      target: { value: 'Dạ học phí khóa K45 là 3.500.000đ bạn nhé!' },
    })
    fireEvent.submit(screen.getByTestId('reply-form'))

    await waitFor(() => {
      expect(sendSpy).toHaveBeenCalledWith(
        mockMessageItem.id,
        'Dạ học phí khóa K45 là 3.500.000đ bạn nhé!',
      )
    })
  })

  // 3. 24h locked window: input disabled with explanation
  it('locks reply input and displays clear explanation when past 24h window', async () => {
    const lockedItem = { ...mockMessageItem, canReply: false }
    vi.spyOn(inboxApi, 'filter').mockResolvedValue({
      items: [lockedItem],
    })
    vi.spyOn(inboxApi, 'getMessage').mockResolvedValue(mockMessageDetailLocked)

    render(
      <MemoryRouter>
        <InboxFeature />
      </MemoryRouter>,
    )

    await waitFor(() => {
      expect(screen.getByTestId(`conv-item-${lockedItem.id}`)).toBeInTheDocument()
    })

    fireEvent.click(screen.getByTestId(`conv-item-${lockedItem.id}`))

    await waitFor(() => {
      expect(screen.getByTestId('reply-locked-24h')).toBeInTheDocument()
    })

    // Explains 24-hour RESPONSE window policy
    expect(screen.getByTestId('reply-locked-24h')).toHaveTextContent('24 giờ')
    expect(screen.getByTestId('reply-locked-24h')).toHaveTextContent('chính sách của Meta')

    // Reply input and send button are locked (disabled) when 24h window is closed
    expect(screen.getByTestId('reply-input')).toBeDisabled()
    expect(screen.getByTestId('btn-send-reply')).toBeDisabled()
  })

  // 4. View comment thread detail and send reply
  it('displays post preview, comment, replies and sends comment reply', async () => {
    vi.spyOn(inboxApi, 'filter').mockResolvedValue({
      items: [mockCommentItem],
    })
    vi.spyOn(inboxApi, 'getComment').mockResolvedValue(mockCommentDetail)
    const replyCommentSpy = vi.spyOn(inboxApi, 'replyComment').mockResolvedValue({ success: true })

    render(
      <MemoryRouter>
        <InboxFeature />
      </MemoryRouter>,
    )

    await waitFor(() => {
      expect(screen.getByTestId(`conv-item-${mockCommentItem.id}`)).toBeInTheDocument()
    })

    fireEvent.click(screen.getByTestId(`conv-item-${mockCommentItem.id}`))

    await waitFor(() => {
      expect(screen.getByTestId('comment-post-preview')).toHaveTextContent('Khai giảng khóa kế toán')
      expect(screen.getByTestId('main-comment-bubble')).toHaveTextContent('Khóa học này có cấp chứng chỉ không ạ?')
      expect(screen.getByText('Các câu trả lời (1):')).toBeInTheDocument()
    })

    // Reply to comment
    fireEvent.change(screen.getByTestId('reply-input'), {
      target: { value: 'Bạn có thể để lại SĐT để trung tâm tư vấn nhé!' },
    })
    fireEvent.submit(screen.getByTestId('reply-form'))

    await waitFor(() => {
      expect(replyCommentSpy).toHaveBeenCalledWith(
        mockCommentItem.id,
        'Bạn có thể để lại SĐT để trung tâm tư vấn nhé!',
      )
    })
  })

  // 5. Status change, Assignee change, Notes, Tag attach/detach
  it('supports changing status, assigning user, adding note, and attaching/detaching tag', async () => {
    vi.spyOn(inboxApi, 'filter').mockResolvedValue({
      items: [mockMessageItem],
    })
    vi.spyOn(inboxApi, 'getMessage').mockResolvedValue(mockMessageDetailOpen)
    const statusSpy = vi.spyOn(inboxApi, 'setMessageStatus').mockResolvedValue({ success: true })
    const assignSpy = vi.spyOn(inboxApi, 'assignMessage').mockResolvedValue({ success: true })
    const noteSpy = vi.spyOn(inboxApi, 'addMessageNote').mockResolvedValue({ success: true })
    const attachTagSpy = vi.spyOn(inboxApi, 'attachTag').mockResolvedValue({ success: true })
    const detachTagSpy = vi.spyOn(inboxApi, 'detachTag').mockResolvedValue({ success: true })

    render(
      <MemoryRouter>
        <InboxFeature />
      </MemoryRouter>,
    )

    await waitFor(() => {
      expect(screen.getByTestId(`conv-item-${mockMessageItem.id}`)).toBeInTheDocument()
    })

    fireEvent.click(screen.getByTestId(`conv-item-${mockMessageItem.id}`))

    await waitFor(() => {
      expect(screen.getByTestId('select-change-status')).toBeInTheDocument()
    })

    // Change status to Resolved (3)
    fireEvent.change(screen.getByTestId('select-change-status'), { target: { value: '3' } })
    await waitFor(() => {
      expect(statusSpy).toHaveBeenCalledWith(mockMessageItem.id, 3)
    })

    // Assign to Admin User
    fireEvent.change(screen.getByTestId('select-assign-user'), { target: { value: 'u-admin' } })
    await waitFor(() => {
      expect(assignSpy).toHaveBeenCalledWith(
        mockMessageItem.id,
        expect.objectContaining({ assignedUserId: 'u-admin', assignedTo: 'Admin User' }),
      )
    })

    // Open Note panel and add note
    fireEvent.click(screen.getByTestId('btn-add-note'))
    expect(screen.getByTestId('inbox-note-panel')).toBeInTheDocument()
    fireEvent.change(screen.getByTestId('input-note-text'), { target: { value: 'Đã gọi tư vấn' } })
    fireEvent.click(screen.getByTestId('btn-save-note'))
    await waitFor(() => {
      expect(noteSpy).toHaveBeenCalledWith(mockMessageItem.id, 'Đã gọi tư vấn')
    })

    // Attach Tag
    fireEvent.click(screen.getByTestId('btn-toggle-tag-select'))
    expect(screen.getByTestId('select-attach-tag')).toBeInTheDocument()
    fireEvent.change(screen.getByTestId('select-attach-tag'), { target: { value: 'tag-2' } })
    await waitFor(() => {
      expect(attachTagSpy).toHaveBeenCalledWith('tag-2', 1, mockMessageItem.id)
    })

    // Detach Tag
    expect(screen.getByTestId('btn-detach-tag-tag-1')).toBeInTheDocument()
    fireEvent.click(screen.getByTestId('btn-detach-tag-tag-1'))
    await waitFor(() => {
      expect(detachTagSpy).toHaveBeenCalledWith('tag-1', 1, mockMessageItem.id)
    })
  })

  // 6. Periodic refresh and focus refetch
  it('automatically refetches inbox periodically and when returning to window tab', async () => {
    const filterSpy = vi.spyOn(inboxApi, 'filter').mockResolvedValue({
      items: [mockMessageItem],
    })

    render(
      <MemoryRouter>
        <InboxFeature />
      </MemoryRouter>,
    )

    await waitFor(() => {
      expect(filterSpy).toHaveBeenCalledTimes(1)
    })

    // Simulate window focus event
    act(() => {
      window.dispatchEvent(new Event('focus'))
    })

    await waitFor(() => {
      expect(filterSpy).toHaveBeenCalledTimes(2)
    })

    // Simulate document visibilitychange event
    act(() => {
      Object.defineProperty(document, 'visibilityState', { value: 'visible', configurable: true })
      document.dispatchEvent(new Event('visibilitychange'))
    })

    await waitFor(() => {
      expect(filterSpy).toHaveBeenCalledTimes(3)
    })
  })

  // 7. Mobile responsive layout (375px)
  it('toggles between list and detail on mobile viewport', async () => {
    vi.spyOn(inboxApi, 'filter').mockResolvedValue({
      items: [mockMessageItem],
    })
    vi.spyOn(inboxApi, 'getMessage').mockResolvedValue(mockMessageDetailOpen)

    render(
      <MemoryRouter>
        <InboxFeature />
      </MemoryRouter>,
    )

    await waitFor(() => {
      expect(screen.getByTestId(`conv-item-${mockMessageItem.id}`)).toBeInTheDocument()
    })

    // Click item to view detail
    fireEvent.click(screen.getByTestId(`conv-item-${mockMessageItem.id}`))

    await waitFor(() => {
      expect(screen.getByTestId('btn-back-to-list')).toBeInTheDocument()
    })

    // Click back to return to list
    fireEvent.click(screen.getByTestId('btn-back-to-list'))
    expect(screen.getByTestId('conversation-list')).toBeInTheDocument()
  })
})
