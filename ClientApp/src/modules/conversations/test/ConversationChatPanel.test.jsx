import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import ConversationChatPanel from '../components/ConversationChatPanel'

// Mock services
vi.mock('@/modules/messages/services/pageMessageApi', () => ({
  pageMessageApi: {
    get: vi.fn(),
    send: vi.fn(),
    setStatus: vi.fn(),
    assign: vi.fn(),
    note: vi.fn(),
  },
  pageMessageKeys: {
    all: ['page-messages'],
    detail: (id) => ['page-messages', 'detail', id],
  },
}))

vi.mock('@/modules/comments/services/commentApi', () => ({
  commentApi: {
    getThread: vi.fn(),
    reply: vi.fn(),
    hide: vi.fn(),
    unhide: vi.fn(),
    remove: vi.fn(),
    pending: vi.fn(),
    setStatus: vi.fn(),
    assign: vi.fn(),
    note: vi.fn(),
  },
  commentQueryKeys: {
    all: ['social-comments'],
    thread: (id) => ['social-comments', 'thread', id],
  },
}))

vi.mock('../services/inboxApi', () => ({
  inboxApi: {
    suggestReply: vi.fn(),
    getProfile: vi.fn(),
  },
  inboxQueryKeys: {
    all: ['inbox'],
    profile: (kind, id) => ['inbox', 'profile', kind, id],
  },
}))

const mockHasRole = vi.fn()
vi.mock('@/shared/hooks/usePermissions', () => ({
  usePermissions: () => ({
    hasRole: mockHasRole,
  }),
}))

vi.mock('@/shared/stores/toastStore', () => ({
  toast: {
    success: vi.fn(),
    error: vi.fn(),
    info: vi.fn(),
  },
}))

import { pageMessageApi } from '@/modules/messages/services/pageMessageApi'
import { commentApi } from '@/modules/comments/services/commentApi'
import { inboxApi } from '../services/inboxApi'
import { toast } from '@/shared/stores/toastStore'

function renderComponent(props) {
  const queryClient = new QueryClient({
    defaultOptions: {
      queries: { retry: false },
      mutations: { retry: false },
    },
  })
  return render(
    <QueryClientProvider client={queryClient}>
      <MemoryRouter>
        <ConversationChatPanel {...props} />
      </MemoryRouter>
    </QueryClientProvider>,
  )
}

describe('ConversationChatPanel — Tin nhắn Page (AC 458fbe60 (a), (b))', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    mockHasRole.mockReturnValue(true) // Admin / ContentManager / Reviewer
  })

  it('gửi tin gọi pageMessageApi.send; đổi trạng thái / giao / ghi chú gọi đúng API PageMessage', async () => {
    const user = userEvent.setup()
    pageMessageApi.get.mockResolvedValue({
      data: {
        id: 'msg-1',
        participantName: 'Nguyễn Văn A',
        channelName: 'Page VNi',
        isReplyWindowOpen: true,
        inboxStatus: 1,
        assignedTo: 'nhanvien1',
        internalNote: 'Ghi chú cũ',
        messages: [
          {
            id: 'm1',
            text: 'Chào page, tư vấn giúp em khóa học',
            isFromPage: false,
            sentAt: '2026-10-08T08:00:00Z',
          },
        ],
      },
    })
    pageMessageApi.send.mockResolvedValue({ data: { success: true } })
    pageMessageApi.setStatus.mockResolvedValue({ data: { success: true } })
    pageMessageApi.assign.mockResolvedValue({ data: { success: true } })
    pageMessageApi.note.mockResolvedValue({ data: { success: true } })

    renderComponent({
      kind: 'message',
      id: 'msg-1',
      conversation: { kind: 'message', id: 'msg-1' },
    })

    // Wait for message thread to load
    expect(await screen.findByText('Chào page, tư vấn giúp em khóa học')).toBeInTheDocument()
    expect(screen.getByText('Nguyễn Văn A')).toBeInTheDocument()

    // Gửi tin nhắn
    const composerTextarea = screen.getByTestId('chat-composer-textarea')
    await user.type(composerTextarea, 'Dạ em chào anh, bên em có các khóa học sau')
    const sendBtn = screen.getByTestId('chat-send-btn')
    expect(sendBtn).not.toBeDisabled()
    await user.click(sendBtn)

    expect(pageMessageApi.send).toHaveBeenCalledWith('msg-1', 'Dạ em chào anh, bên em có các khóa học sau')
    await waitFor(() => expect(toast.success).toHaveBeenCalledWith('Đã gửi tin nhắn'))

    // Đổi trạng thái sang "Đang xử lý" (status 2)
    const inProgressBtn = screen.getByTestId('status-in-progress-btn')
    await user.click(inProgressBtn)
    expect(pageMessageApi.setStatus).toHaveBeenCalledWith('msg-1', 2)

    // Đổi trạng thái sang "Bỏ qua" (status 4)
    const ignoreBtn = screen.getByTestId('status-ignore-btn')
    await user.click(ignoreBtn)
    expect(pageMessageApi.setStatus).toHaveBeenCalledWith('msg-1', 4)

    // Giao người xử lý
    const toggleAssignBtn = screen.getByTestId('toggle-assign-btn')
    await user.click(toggleAssignBtn)
    const assigneeInput = screen.getByTestId('workflow-assignee-input')
    await user.clear(assigneeInput)
    await user.type(assigneeInput, 'admin@vni.edu.vn')
    const assignSubmitBtn = screen.getByTestId('assign-submit-btn')
    await user.click(assignSubmitBtn)
    expect(pageMessageApi.assign).toHaveBeenCalledWith('msg-1', 'admin@vni.edu.vn')

    // Ghi chú nội bộ
    const toggleNoteBtn = screen.getByTestId('toggle-note-btn')
    await user.click(toggleNoteBtn)
    const noteTextarea = screen.getByTestId('workflow-note-input')
    await user.clear(noteTextarea)
    await user.type(noteTextarea, 'Khách hẹn chiều gọi lại')
    const noteSubmitBtn = screen.getByTestId('note-submit-btn')
    await user.click(noteSubmitBtn)
    expect(pageMessageApi.note).toHaveBeenCalledWith('msg-1', 'Khách hẹn chiều gọi lại')
  })

  it('Cửa sổ 24h đóng → ô soạn disabled + banner; mở → gửi được', async () => {
    const user = userEvent.setup()

    // 1. Cửa sổ đóng: isReplyWindowOpen = false
    pageMessageApi.get.mockResolvedValueOnce({
      data: {
        id: 'msg-closed',
        participantName: 'Khách Hết Hạn',
        isReplyWindowOpen: false,
        replyWindowClosesAt: '2026-10-07T12:00:00Z',
        messages: [{ id: 'm1', text: 'Tin nhắn cũ', isFromPage: false, sentAt: '2026-10-07T10:00:00Z' }],
      },
    })

    const { rerender } = renderComponent({
      kind: 'message',
      id: 'msg-closed',
      conversation: { kind: 'message', id: 'msg-closed' },
    })

    expect(await screen.findByText('Khách Hết Hạn')).toBeInTheDocument()
    // Banner 24h hiển thị
    const banner = screen.getByTestId('window-24h-alert')
    expect(banner).toBeInTheDocument()
    expect(banner).toHaveTextContent('Cửa sổ 24 giờ đã đóng. Hệ thống khóa gửi để tránh vi phạm chính sách Meta.')

    // Ô soạn & nút gửi bị disabled
    const composerTextarea = screen.getByTestId('chat-composer-textarea')
    expect(composerTextarea).toBeDisabled()
    const sendBtn = screen.getByTestId('chat-send-btn')
    expect(sendBtn).toBeDisabled()

    // 2. Cửa sổ mở: isReplyWindowOpen = true
    pageMessageApi.get.mockResolvedValueOnce({
      data: {
        id: 'msg-open',
        participantName: 'Khách Còn Hạn',
        isReplyWindowOpen: true,
        messages: [{ id: 'm2', text: 'Tin mới', isFromPage: false, sentAt: '2026-10-08T09:00:00Z' }],
      },
    })
    pageMessageApi.send.mockResolvedValue({ data: { success: true } })

    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    rerender(
      <QueryClientProvider client={queryClient}>
        <MemoryRouter>
          <ConversationChatPanel kind="message" id="msg-open" conversation={{ kind: 'message', id: 'msg-open' }} />
        </MemoryRouter>
      </QueryClientProvider>,
    )

    expect(await screen.findByText('Khách Còn Hạn')).toBeInTheDocument()
    expect(screen.queryByTestId('window-24h-alert')).not.toBeInTheDocument()
    const openTextarea = screen.getByTestId('chat-composer-textarea')
    expect(openTextarea).not.toBeDisabled()

    await user.type(openTextarea, 'Chào bạn')
    const openSendBtn = screen.getByTestId('chat-send-btn')
    expect(openSendBtn).not.toBeDisabled()
    await user.click(openSendBtn)
    expect(pageMessageApi.send).toHaveBeenCalledWith('msg-open', 'Chào bạn')
  })
})

describe('ConversationChatPanel — Bình luận (AC 458fbe60 (c))', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    mockHasRole.mockReturnValue(true)
  })

  it('trả lời / ẩn / hiện / xoá (có xác nhận) / duyệt chờ / trạng thái / giao / ghi chú gọi đúng API SocialComment', async () => {
    const user = userEvent.setup()
    commentApi.getThread.mockResolvedValue({
      data: {
        id: 'cmt-1',
        authorName: 'Trần Thị B',
        message: 'Khóa học này học phí bao nhiêu ạ?',
        channelName: 'VNi Fanpage',
        isHidden: false,
        isPending: true,
        inboxStatus: 1,
        capabilities: {
          canReply: true,
          canHide: true,
          canUnhide: true,
          canDelete: true,
          canManagePending: true,
        },
        replies: [],
      },
    })
    commentApi.reply.mockResolvedValue({ data: { success: true } })
    commentApi.hide.mockResolvedValue({ data: { success: true } })
    commentApi.unhide.mockResolvedValue({ data: { success: true } })
    commentApi.remove.mockResolvedValue({ data: { success: true } })
    commentApi.pending.mockResolvedValue({ data: { success: true } })
    commentApi.setStatus.mockResolvedValue({ data: { success: true } })
    commentApi.assign.mockResolvedValue({ data: { success: true } })
    commentApi.note.mockResolvedValue({ data: { success: true } })

    renderComponent({
      kind: 'comment',
      id: 'cmt-1',
      conversation: { kind: 'comment', id: 'cmt-1' },
    })

    expect(await screen.findByText('Khóa học này học phí bao nhiêu ạ?')).toBeInTheDocument()

    // 1. Trả lời bình luận
    const composerTextarea = screen.getByTestId('chat-composer-textarea')
    await user.type(composerTextarea, 'Học phí là 5 triệu bạn nhé')
    const sendBtn = screen.getByTestId('chat-send-btn')
    await user.click(sendBtn)
    expect(commentApi.reply).toHaveBeenCalledWith('cmt-1', 'Học phí là 5 triệu bạn nhé')

    // 2. Ẩn bình luận
    const hideBtn = screen.getByTestId('comment-hide-btn')
    await user.click(hideBtn)
    expect(commentApi.hide).toHaveBeenCalledWith('cmt-1')

    // 3. Xoá bình luận (có xác nhận confirm)
    const confirmSpy = vi.spyOn(window, 'confirm')
    // Nếu người dùng huỷ confirm: không gọi remove
    confirmSpy.mockReturnValueOnce(false)
    const deleteBtn = screen.getByTestId('comment-delete-btn')
    await user.click(deleteBtn)
    expect(commentApi.remove).not.toHaveBeenCalled()

    // Người dùng đồng ý confirm: gọi remove
    confirmSpy.mockReturnValueOnce(true)
    await user.click(deleteBtn)
    expect(commentApi.remove).toHaveBeenCalledWith('cmt-1')

    // 4. Duyệt pending & Bỏ qua pending
    const approveBtn = screen.getByTestId('comment-approve-pending-btn')
    await user.click(approveBtn)
    expect(commentApi.pending).toHaveBeenCalledWith('cmt-1', true)

    const rejectBtn = screen.getByTestId('comment-reject-pending-btn')
    await user.click(rejectBtn)
    expect(commentApi.pending).toHaveBeenCalledWith('cmt-1', false)

    // 5. Đổi trạng thái
    await user.click(screen.getByTestId('status-in-progress-btn'))
    expect(commentApi.setStatus).toHaveBeenCalledWith('cmt-1', 2)

    await user.click(screen.getByTestId('status-ignore-btn'))
    expect(commentApi.setStatus).toHaveBeenCalledWith('cmt-1', 4)

    // 6. Giao người xử lý
    await user.click(screen.getByTestId('toggle-assign-btn'))
    const assigneeInput = screen.getByTestId('workflow-assignee-input')
    await user.type(assigneeInput, 'staff2')
    await user.click(screen.getByTestId('assign-submit-btn'))
    expect(commentApi.assign).toHaveBeenCalledWith('cmt-1', 'staff2')

    // 7. Ghi chú nội bộ
    await user.click(screen.getByTestId('toggle-note-btn'))
    const noteInput = screen.getByTestId('workflow-note-input')
    await user.type(noteInput, 'Đã inbox riêng')
    await user.click(screen.getByTestId('note-submit-btn'))
    expect(commentApi.note).toHaveBeenCalledWith('cmt-1', 'Đã inbox riêng')
  })

  it('Nút ẩn theo capabilities và vai trò (Viewer không thấy nút ghi/thao tác)', async () => {
    // 1. Viewer role: hasRole returns false
    mockHasRole.mockReturnValue(false)
    commentApi.getThread.mockResolvedValueOnce({
      data: {
        id: 'cmt-view-only',
        authorName: 'Khách Xem',
        message: 'Nội dung xem',
        capabilities: { canReply: true, canHide: true, canDelete: true },
      },
    })

    const { rerender } = renderComponent({
      kind: 'comment',
      id: 'cmt-view-only',
      conversation: { kind: 'comment', id: 'cmt-view-only' },
    })

    expect(await screen.findByText('Nội dung xem')).toBeInTheDocument()

    // Viewer không thấy các nút thao tác / kiểm duyệt
    expect(screen.queryByTestId('status-in-progress-btn')).not.toBeInTheDocument()
    expect(screen.queryByTestId('status-ignore-btn')).not.toBeInTheDocument()
    expect(screen.queryByTestId('comment-hide-btn')).not.toBeInTheDocument()
    expect(screen.queryByTestId('comment-delete-btn')).not.toBeInTheDocument()
    expect(screen.queryByTestId('toggle-assign-btn')).not.toBeInTheDocument()
    expect(screen.queryByTestId('toggle-note-btn')).not.toBeInTheDocument()
    expect(screen.queryByTestId('chat-composer-textarea')).not.toBeInTheDocument()
    expect(screen.getByTestId('chat-composer-readonly')).toBeInTheDocument()

    // 2. Admin role nhưng capabilities không cho phép ẩn/xoá
    mockHasRole.mockReturnValue(true)
    commentApi.getThread.mockResolvedValueOnce({
      data: {
        id: 'cmt-no-caps',
        authorName: 'Khách No Caps',
        message: 'Bình luận không cho ẩn',
        isHidden: false,
        capabilities: {
          canReply: true,
          canHide: false, // Không có quyền ẩn
          canDelete: false, // Không có quyền xoá
        },
      },
    })

    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    rerender(
      <QueryClientProvider client={queryClient}>
        <MemoryRouter>
          <ConversationChatPanel kind="comment" id="cmt-no-caps" conversation={{ kind: 'comment', id: 'cmt-no-caps' }} />
        </MemoryRouter>
      </QueryClientProvider>,
    )

    expect(await screen.findByText('Bình luận không cho ẩn')).toBeInTheDocument()
    // canHide = false -> Không hiện nút Ẩn
    expect(screen.queryByTestId('comment-hide-btn')).not.toBeInTheDocument()
    // canDelete = false -> Không hiện nút Xoá
    expect(screen.queryByTestId('comment-delete-btn')).not.toBeInTheDocument()
    // Nhưng các nút trạng thái chung vẫn hiện với Admin
    expect(screen.getByTestId('status-in-progress-btn')).toBeInTheDocument()
  })
})

describe('ConversationChatPanel — AI Gợi ý trả lời (AC 6e976e31 vitest)', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    mockHasRole.mockReturnValue(true)
  })

  it('bấm "AI gợi ý" điền nháp vào ô soạn (không gửi); lỗi thì giữ nội dung đang gõ', async () => {
    const user = userEvent.setup()
    pageMessageApi.get.mockResolvedValue({
      data: {
        id: 'msg-ai-test',
        participantName: 'Khách Cần Tư Vấn',
        isReplyWindowOpen: true,
        messages: [{ id: 'm1', text: 'Tư vấn giúp em với', isFromPage: false, sentAt: '2026-10-08T08:00:00Z' }],
      },
    })
    inboxApi.suggestReply.mockResolvedValueOnce({
      data: {
        draft: 'Chào bạn, cảm ơn bạn đã quan tâm đến chương trình đào tạo của VNi!',
      },
    })

    renderComponent({
      kind: 'message',
      id: 'msg-ai-test',
      conversation: { kind: 'message', id: 'msg-ai-test' },
    })

    expect(await screen.findByText('Tư vấn giúp em với')).toBeInTheDocument()
    const composerTextarea = screen.getByTestId('chat-composer-textarea')
    const aiBtn = screen.getByTestId('ai-suggest-btn')

    // Bấm nút AI gợi ý
    await user.click(aiBtn)
    expect(inboxApi.suggestReply).toHaveBeenCalledWith('message', 'msg-ai-test')

    // Bản nháp được điền vào ô soạn
    await waitFor(() => {
      expect(composerTextarea).toHaveValue('Chào bạn, cảm ơn bạn đã quan tâm đến chương trình đào tạo của VNi!')
    })
    // Không tự động gửi tin
    expect(pageMessageApi.send).not.toHaveBeenCalled()
    expect(toast.success).toHaveBeenCalledWith('Đã tạo bản nháp gợi ý từ AI')

    // Trường hợp lỗi: AI trả về lỗi hoặc timeout
    inboxApi.suggestReply.mockRejectedValueOnce(new Error('AI quá hạn phản hồi. Vui lòng thử lại sau.'))

    // Người dùng gõ thêm vào ô soạn
    await user.clear(composerTextarea)
    await user.type(composerTextarea, 'Nội dung tôi đang tự gõ dở dang...')

    // Bấm lại AI gợi ý gặp lỗi
    await user.click(aiBtn)
    expect(inboxApi.suggestReply).toHaveBeenCalledTimes(2)

    await waitFor(() => {
      expect(toast.error).toHaveBeenCalledWith('AI quá hạn phản hồi. Vui lòng thử lại sau.')
    })
    // Nội dung đang gõ dở dang được giữ nguyên, không bị xoá mất
    expect(composerTextarea).toHaveValue('Nội dung tôi đang tự gõ dở dang...')
    expect(pageMessageApi.send).not.toHaveBeenCalled()
  })

  it('với tin nhắn có cửa sổ 24h đã đóng: vẫn tạo nháp được nhưng không gửi được', async () => {
    const user = userEvent.setup()
    pageMessageApi.get.mockResolvedValue({
      data: {
        id: 'msg-closed-ai',
        participantName: 'Khách Cửa Sổ Đóng',
        isReplyWindowOpen: false,
        messages: [{ id: 'm1', text: 'Tin cũ', isFromPage: false, sentAt: '2026-10-07T08:00:00Z' }],
      },
    })
    inboxApi.suggestReply.mockResolvedValueOnce({
      data: {
        draft: 'Bản nháp gợi ý cho tin nhắn quá hạn',
      },
    })

    renderComponent({
      kind: 'message',
      id: 'msg-closed-ai',
      conversation: { kind: 'message', id: 'msg-closed-ai' },
    })

    expect(await screen.findByText('Khách Cửa Sổ Đóng')).toBeInTheDocument()
    const composerTextarea = screen.getByTestId('chat-composer-textarea')
    const sendBtn = screen.getByTestId('chat-send-btn')
    const aiBtn = screen.getByTestId('ai-suggest-btn')

    // Ô soạn và nút gửi đang disabled do cửa sổ 24h đóng
    expect(composerTextarea).toBeDisabled()
    expect(sendBtn).toBeDisabled()

    // Bấm AI gợi ý vẫn hoạt động và điền bản nháp
    await user.click(aiBtn)
    expect(inboxApi.suggestReply).toHaveBeenCalledWith('message', 'msg-closed-ai')

    await waitFor(() => {
      expect(composerTextarea).toHaveValue('Bản nháp gợi ý cho tin nhắn quá hạn')
    })
    // Nhưng nút gửi vẫn bị disabled, không thể gửi được
    expect(sendBtn).toBeDisabled()
    expect(pageMessageApi.send).not.toHaveBeenCalled()
  })
})
