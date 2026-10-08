import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import CustomerInfoPanel from '../components/CustomerInfoPanel'

// Mock services
vi.mock('@/modules/messages/services/pageMessageApi', () => ({
  pageMessageApi: {
    assign: vi.fn(),
    note: vi.fn(),
  },
}))

vi.mock('@/modules/comments/services/commentApi', () => ({
  commentApi: {
    assign: vi.fn(),
    note: vi.fn(),
  },
}))

vi.mock('../services/inboxApi', () => ({
  inboxApi: {
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

const mockProfileData = {
  kind: 1, // Message
  id: 'conv-101',
  participantName: 'Trần Thị B',
  participantAvatarUrl: 'https://example.com/avatar.jpg',
  participantExternalId: 'fb-user-123',
  socialChannelId: 'chan-1',
  channelName: 'Page VNi Tuyển Sinh',
  platform: 1, // Facebook
  assignedUserId: 'user-1',
  assignedTo: 'nhanvien_cskh',
  internalNote: 'Khách quan tâm khóa học React nâng cao',
  inboxStatus: 2, // Đang xử lý
  isReplyWindowOpen: true,
  replyWindowClosesAt: '2026-10-09T10:00:00Z',
  stats: {
    customerCount: 14,
    pageCount: 9,
    firstAt: '2026-10-01T08:00:00Z',
    lastAt: '2026-10-08T09:30:00Z',
  },
  media: [
    {
      type: 'image/png',
      url: 'https://example.com/receipt.png',
      sentAt: '2026-10-08T09:00:00Z',
    },
    {
      type: 'image/jpeg',
      url: 'https://example.com/screenshot.jpg',
      sentAt: '2026-10-08T09:15:00Z',
    },
  ],
  activities: [
    {
      source: 'MessageAction',
      actionType: 'status',
      actorUserName: 'admin_vni',
      success: true,
      detail: 'Chuyển sang Đang xử lý',
      createdAt: '2026-10-08T08:30:00Z',
    },
    {
      source: 'MessageAction',
      actionType: 'assign',
      actorUserName: 'admin_vni',
      success: true,
      detail: 'Gán cho nhanvien_cskh',
      createdAt: '2026-10-08T08:35:00Z',
    },
  ],
  otherConversations: [
    {
      kind: 1,
      id: 'conv-102',
      participantName: 'Trần Thị B',
      snippet: 'Dạ em cảm ơn page nhiều',
      lastActivityAt: '2026-10-05T14:00:00Z',
      inboxStatus: 3,
    },
  ],
}

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
        <CustomerInfoPanel {...props} />
      </MemoryRouter>
    </QueryClientProvider>,
  )
}

describe('CustomerInfoPanel — Khung thông tin khách hàng (AC 541307bf vitest)', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    mockHasRole.mockReturnValue(true) // Admin / ContentManager / Reviewer
    localStorage.clear()
  })

  it('panel hiện đủ 6 phần: thông tin khách, nhân viên + ghi chú, thống kê + cửa sổ 24h, ảnh/video, hoạt động, hội thoại khác', async () => {
    const user = userEvent.setup()
    inboxApi.getProfile.mockResolvedValue({
      data: mockProfileData,
    })

    renderComponent({
      kind: 'message',
      id: 'conv-101',
      conversation: { kind: 'message', id: 'conv-101' },
    })

    // 1. Thông tin khách
    expect(await screen.findByTestId('customer-name')).toHaveTextContent('Trần Thị B')
    expect(screen.getByText('Page VNi Tuyển Sinh')).toBeInTheDocument()
    expect(screen.getByText('Facebook')).toBeInTheDocument()
    expect(screen.getByText('Tin nhắn')).toBeInTheDocument()
    expect(screen.getByText('Đang xử lý')).toBeInTheDocument()

    // 2. Nhân viên phụ trách + Ghi chú nội bộ
    expect(screen.getByTestId('info-assignee-input')).toHaveValue('nhanvien_cskh')
    expect(screen.getByTestId('info-note-input')).toHaveValue('Khách quan tâm khóa học React nâng cao')

    // 3. Thống kê tương tác & Cửa sổ 24h
    expect(screen.getByText('14')).toBeInTheDocument() // Customer count
    expect(screen.getByText('Khách gửi')).toBeInTheDocument()
    expect(screen.getByText('9')).toBeInTheDocument() // Page count
    expect(screen.getByText('Page gửi')).toBeInTheDocument()
    expect(screen.getByText('Còn hạn 24 giờ')).toBeInTheDocument()

    // 4. Ảnh / Video & Lightbox
    expect(screen.getByText('Ảnh / Video (2)')).toBeInTheDocument()
    const mediaThumb0 = screen.getByTestId('media-item-0')
    expect(mediaThumb0).toBeInTheDocument()

    // Bấm xem ảnh lớn qua lightbox
    await user.click(mediaThumb0)
    const lightbox = screen.getByTestId('info-lightbox')
    expect(lightbox).toBeInTheDocument()
    // Đóng lightbox
    const lightboxCloseBtn = screen.getByLabelText('Đóng xem ảnh')
    await user.click(lightboxCloseBtn)
    expect(screen.queryByTestId('info-lightbox')).not.toBeInTheDocument()

    // 5. Hoạt động (Timeline)
    expect(screen.getByText('Lịch sử hoạt động (2)')).toBeInTheDocument()
    expect(screen.getByText('Đổi trạng thái')).toBeInTheDocument()
    expect(screen.getByText('Gán người xử lý')).toBeInTheDocument()
    expect(screen.getAllByText(/admin_vni/).length).toBe(2)

    // 6. Hội thoại khác trên cùng page
    expect(screen.getByText('Hội thoại khác trên kênh này (1)')).toBeInTheDocument()
    expect(screen.getByText('Dạ em cảm ơn page nhiều')).toBeInTheDocument()
  })

  it('sửa ghi chú / giao gọi đúng API hiện có (pageMessageApi cho message, commentApi cho comment)', async () => {
    const user = userEvent.setup()

    // A. Với tin nhắn (Message): gọi pageMessageApi.assign và pageMessageApi.note
    inboxApi.getProfile.mockResolvedValue({
      data: mockProfileData,
    })
    pageMessageApi.assign.mockResolvedValue({ data: { success: true } })
    pageMessageApi.note.mockResolvedValue({ data: { success: true } })

    const { rerender } = renderComponent({
      kind: 'message',
      id: 'conv-101',
    })

    expect(await screen.findByTestId('customer-name')).toBeInTheDocument()

    // Sửa người xử lý
    const assigneeInput = screen.getByTestId('info-assignee-input')
    await user.clear(assigneeInput)
    await user.type(assigneeInput, 'supporter_moi')
    const assignBtn = screen.getByTestId('info-assign-save-btn')
    await user.click(assignBtn)
    expect(pageMessageApi.assign).toHaveBeenCalledWith('conv-101', 'supporter_moi')
    await waitFor(() => expect(toast.success).toHaveBeenCalledWith('Đã cập nhật người xử lý'))

    // Sửa ghi chú nội bộ
    const noteInput = screen.getByTestId('info-note-input')
    await user.clear(noteInput)
    await user.type(noteInput, 'Đã tư vấn qua Zalo')
    const noteBtn = screen.getByTestId('info-note-save-btn')
    await user.click(noteBtn)
    expect(pageMessageApi.note).toHaveBeenCalledWith('conv-101', 'Đã tư vấn qua Zalo')
    await waitFor(() => expect(toast.success).toHaveBeenCalledWith('Đã lưu ghi chú nội bộ'))

    // B. Với bình luận (Comment): gọi commentApi.assign và commentApi.note
    inboxApi.getProfile.mockResolvedValue({
      data: {
        ...mockProfileData,
        kind: 2, // Comment
        id: 'cmt-202',
      },
    })
    commentApi.assign.mockResolvedValue({ data: { success: true } })
    commentApi.note.mockResolvedValue({ data: { success: true } })

    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    rerender(
      <QueryClientProvider client={queryClient}>
        <MemoryRouter>
          <CustomerInfoPanel kind="comment" id="cmt-202" />
        </MemoryRouter>
      </QueryClientProvider>,
    )

    expect(await screen.findByTestId('customer-name')).toBeInTheDocument()

    // Sửa người xử lý comment
    const cmtAssigneeInput = screen.getByTestId('info-assignee-input')
    await user.clear(cmtAssigneeInput)
    await user.type(cmtAssigneeInput, 'staff_comment')
    await user.click(screen.getByTestId('info-assign-save-btn'))
    expect(commentApi.assign).toHaveBeenCalledWith('cmt-202', 'staff_comment')

    // Sửa ghi chú comment
    const cmtNoteInput = screen.getByTestId('info-note-input')
    await user.clear(cmtNoteInput)
    await user.type(cmtNoteInput, 'Ghi chú bình luận Facebook')
    await user.click(screen.getByTestId('info-note-save-btn'))
    expect(commentApi.note).toHaveBeenCalledWith('cmt-202', 'Ghi chú bình luận Facebook')
  })

  it('vai trò Viewer: hiển thị dạng chỉ đọc (không có nút Gán hay Lưu ghi chú)', async () => {
    mockHasRole.mockReturnValue(false) // Viewer (read-only)
    inboxApi.getProfile.mockResolvedValue({
      data: mockProfileData,
    })

    renderComponent({
      kind: 'message',
      id: 'conv-101',
    })

    expect(await screen.findByTestId('customer-name')).toBeInTheDocument()

    // Không có ô input và nút submit
    expect(screen.queryByTestId('info-assignee-input')).not.toBeInTheDocument()
    expect(screen.queryByTestId('info-assign-save-btn')).not.toBeInTheDocument()
    expect(screen.queryByTestId('info-note-input')).not.toBeInTheDocument()
    expect(screen.queryByTestId('info-note-save-btn')).not.toBeInTheDocument()

    // Có chữ chỉ đọc
    expect(screen.getByTestId('info-readonly-assignee')).toHaveTextContent('nhanvien_cskh')
    expect(screen.getByTestId('info-readonly-note')).toHaveTextContent('Khách quan tâm khóa học React nâng cao')
  })

  it('bấm vào hội thoại khác trên cùng page gọi onSelectConversation và chuyển hội thoại', async () => {
    const user = userEvent.setup()
    const onSelectMock = vi.fn()
    inboxApi.getProfile.mockResolvedValue({
      data: mockProfileData,
    })

    renderComponent({
      kind: 'message',
      id: 'conv-101',
      onSelectConversation: onSelectMock,
    })

    expect(await screen.findByText('Dạ em cảm ơn page nhiều')).toBeInTheDocument()

    const otherConvBtn = screen.getByTestId('other-conversation-conv-102')
    await user.click(otherConvBtn)

    expect(onSelectMock).toHaveBeenCalledWith('conv-102', 'message')
  })

  it('đóng mở được và lưu trạng thái vào localStorage', async () => {
    const user = userEvent.setup()
    const onCloseMock = vi.fn()
    inboxApi.getProfile.mockResolvedValue({
      data: mockProfileData,
    })

    renderComponent({
      kind: 'message',
      id: 'conv-101',
      onClose: onCloseMock,
    })

    expect(await screen.findByTestId('customer-name')).toBeInTheDocument()

    const closeBtn = screen.getByTestId('close-info-btn')
    await user.click(closeBtn)

    expect(onCloseMock).toHaveBeenCalled()
    expect(localStorage.getItem('crm_customer_info_panel_open')).toBe('false')
  })

  it('hiển thị Loading, Error và Empty states chính xác', async () => {
    // 1. Empty state khi id null
    const { rerender } = renderComponent({
      kind: 'message',
      id: null,
    })
    expect(screen.getByText('Chưa chọn hội thoại để xem thông tin khách.')).toBeInTheDocument()

    // 2. Error state khi API lỗi
    inboxApi.getProfile.mockRejectedValueOnce(new Error('Hội thoại không tồn tại'))
    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })

    rerender(
      <QueryClientProvider client={queryClient}>
        <MemoryRouter>
          <CustomerInfoPanel kind="message" id="conv-error" />
        </MemoryRouter>
      </QueryClientProvider>,
    )

    expect(await screen.findByText('Hội thoại không tồn tại')).toBeInTheDocument()
  })
})
