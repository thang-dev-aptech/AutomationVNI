import React from 'react'
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, fireEvent, waitFor } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { InboxDetail } from '../features/inbox/components/InboxDetail'
import { InboxFeature } from '../features/inbox/InboxFeature'
import { inboxApi } from '../features/inbox/api/inboxApi'
import { useAuthStore } from '../auth/authStore'
import * as toastModule from '../shared/utils/toast'

describe('AC 523b968c-e303-4f88-92c2-f7df13146095 — CrmApp Inbox Safety & AI Suggest Reply', () => {
  const mockMessageItem = {
    id: 'msg-item-1',
    kind: 1, // message
    displayName: 'Nguyễn Văn An',
    channelName: 'VNI Fanpage Tuyển sinh',
    snippet: 'Tư vấn giúp em khóa học',
    canReply: false,
    status: 1,
  }

  /** List item từ backend thường có canReply=true — F1: N2 phải khoá khi detail chưa sẵn sàng. */
  const mockMessageItemCanReplyTrue = {
    ...mockMessageItem,
    id: 'msg-item-can-reply',
    canReply: true,
  }

  const mockCommentItem = {
    id: 'cmt-item-2',
    kind: 2, // comment
    displayName: 'Trần Thị Bích',
    channelName: 'VNI Fanpage Tuyển sinh',
    snippet: 'Học phí bao nhiêu ạ?',
    canReply: false,
    status: 1,
  }

  beforeEach(() => {
    vi.restoreAllMocks()
    useAuthStore.getState().setAuth('mock-token', {
      email: 'admin@vni.local',
      userName: 'AdminUser',
      roles: ['Admin'],
    })
  })

  // =========================================================================
  // (a) Tin nhắn: khi dữ liệu chi tiết chưa tải xong hoặc lỗi -> ô soạn và nút gửi disabled
  // =========================================================================
  describe('(a) Message detail loading or error locks composer and send button', () => {
    it('disables reply input and send button when detail is loading (loading=true)', () => {
      render(
        <InboxDetail
          item={mockMessageItem}
          detail={null}
          loading={true}
        />,
      )

      const replyInput = screen.getByTestId('reply-input')
      const sendBtn = screen.getByTestId('btn-send-reply')

      expect(replyInput).toBeDisabled()
      expect(sendBtn).toBeDisabled()
    })

    it('disables reply input and send button when detail failed / is null (error state)', () => {
      render(
        <InboxDetail
          item={mockMessageItem}
          detail={null}
          loading={false}
        />,
      )

      const replyInput = screen.getByTestId('reply-input')
      const sendBtn = screen.getByTestId('btn-send-reply')

      expect(replyInput).toBeDisabled()
      expect(sendBtn).toBeDisabled()
    })

    it('disables reply input and send button in InboxFeature when API fails (loadDetail error)', async () => {
      vi.spyOn(inboxApi, 'filter').mockResolvedValue({
        items: [mockMessageItem],
        totalCount: 1,
      })
      vi.spyOn(inboxApi, 'getMessage').mockRejectedValue(new Error('Network error'))

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
        expect(screen.getByTestId('reply-input')).toBeDisabled()
        expect(screen.getByTestId('btn-send-reply')).toBeDisabled()
      })
    })

    // F1 / N2: item.canReply=true (production list) — không được fallback mở ô soạn khi thiếu detail
    it('F1: locks composer when item.canReply=true and loading=true / detail=null', () => {
      render(
        <InboxDetail
          item={mockMessageItemCanReplyTrue}
          detail={null}
          loading={true}
        />,
      )
      expect(screen.getByTestId('reply-input')).toBeDisabled()
      expect(screen.getByTestId('btn-send-reply')).toBeDisabled()
    })

    it('F1: locks composer when item.canReply=true, loading=false, detail=null', () => {
      render(
        <InboxDetail
          item={mockMessageItemCanReplyTrue}
          detail={null}
          loading={false}
        />,
      )
      expect(screen.getByTestId('reply-input')).toBeDisabled()
      expect(screen.getByTestId('btn-send-reply')).toBeDisabled()
    })

    it('F1: locks composer in InboxFeature when getMessage rejects and list item has canReply=true', async () => {
      vi.spyOn(inboxApi, 'filter').mockResolvedValue({
        items: [mockMessageItemCanReplyTrue],
        totalCount: 1,
      })
      const getMessageSpy = vi
        .spyOn(inboxApi, 'getMessage')
        .mockRejectedValue(new Error('Network error'))

      render(
        <MemoryRouter>
          <InboxFeature />
        </MemoryRouter>,
      )

      await waitFor(() => {
        expect(screen.getByTestId(`conv-item-${mockMessageItemCanReplyTrue.id}`)).toBeInTheDocument()
      })

      fireEvent.click(screen.getByTestId(`conv-item-${mockMessageItemCanReplyTrue.id}`))

      // Chờ getMessage settle (không assert lúc loading) — sau lỗi vẫn phải khoá dù item.canReply=true
      await waitFor(() => {
        expect(getMessageSpy).toHaveBeenCalled()
      })
      await waitFor(() => {
        expect(screen.getByTestId('reply-input')).toBeDisabled()
        expect(screen.getByTestId('btn-send-reply')).toBeDisabled()
      })
      // Giữ khoá sau khi load xong (không chỉ khoá tạm lúc loading)
      await new Promise((r) => setTimeout(r, 30))
      expect(screen.getByTestId('reply-input')).toBeDisabled()
      expect(screen.getByTestId('btn-send-reply')).toBeDisabled()
    })
  })

  // =========================================================================
  // (b) Dữ liệu báo cửa sổ đóng -> disabled + banner. Báo mở -> bật.
  // =========================================================================
  describe('(b) 24h window closed -> disabled + banner; window open -> enabled without banner', () => {
    it('when 24h window is closed (isReplyWindowOpen: false), disables composer and displays 24h banner', () => {
      const closedDetail = {
        kind: 1,
        conversation: {
          id: mockMessageItem.id,
          canReply: false,
          isReplyWindowOpen: false,
          messages: [],
        },
      }

      render(
        <InboxDetail
          item={mockMessageItem}
          detail={closedDetail}
          loading={false}
        />,
      )

      expect(screen.getByTestId('reply-input')).toBeDisabled()
      expect(screen.getByTestId('btn-send-reply')).toBeDisabled()
      expect(screen.getByTestId('reply-locked-24h')).toBeInTheDocument()
      expect(screen.getByTestId('reply-locked-24h')).toHaveTextContent('Quá 24 giờ')
    })

    it('when 24h window is open (canReply: true, isReplyWindowOpen: true), enables composer and hides banner', () => {
      const openDetail = {
        kind: 1,
        conversation: {
          id: mockMessageItem.id,
          canReply: true,
          isReplyWindowOpen: true,
          messages: [],
        },
      }

      render(
        <InboxDetail
          item={mockMessageItem}
          detail={openDetail}
          loading={false}
        />,
      )

      expect(screen.getByTestId('reply-input')).not.toBeDisabled()
      expect(screen.getByTestId('btn-send-reply')).not.toBeDisabled()
      expect(screen.queryByTestId('reply-locked-24h')).not.toBeInTheDocument()
    })
  })

  // =========================================================================
  // (c) Bình luận chưa có capabilities -> disabled. canReply = true -> bật.
  // =========================================================================
  describe('(c) Comment capabilities: unconfirmed/missing -> disabled; canReply=true -> enabled', () => {
    it('disables composer and send button when comment capabilities are missing', () => {
      const commentDetailNoCaps = {
        kind: 2,
        thread: {
          id: mockCommentItem.id,
          comment: { text: 'Bình luận không có capabilities' },
          replies: [],
          // capabilities missing
        },
      }

      render(
        <InboxDetail
          item={mockCommentItem}
          detail={commentDetailNoCaps}
          loading={false}
        />,
      )

      expect(screen.getByTestId('reply-input')).toBeDisabled()
      expect(screen.getByTestId('btn-send-reply')).toBeDisabled()
    })

    it('disables composer and send button when comment capabilities.canReply is false', () => {
      const commentDetailDisabled = {
        kind: 2,
        thread: {
          id: mockCommentItem.id,
          comment: { text: 'Bình luận bị khóa' },
          replies: [],
          capabilities: { canReply: false },
        },
      }

      render(
        <InboxDetail
          item={mockCommentItem}
          detail={commentDetailDisabled}
          loading={false}
        />,
      )

      expect(screen.getByTestId('reply-input')).toBeDisabled()
      expect(screen.getByTestId('btn-send-reply')).toBeDisabled()
    })

    it('enables composer and send button when comment capabilities.canReply is true', () => {
      const commentDetailCanReply = {
        kind: 2,
        thread: {
          id: mockCommentItem.id,
          comment: { text: 'Bình luận có thể trả lời' },
          replies: [],
          capabilities: { canReply: true },
        },
      }

      render(
        <InboxDetail
          item={mockCommentItem}
          detail={commentDetailCanReply}
          loading={false}
        />,
      )

      expect(screen.getByTestId('reply-input')).not.toBeDisabled()
      expect(screen.getByTestId('btn-send-reply')).not.toBeDisabled()
    })
  })

  // =========================================================================
  // (d) AI gợi ý: Admin/ContentManager/Reviewer thấy nút; Viewer không thấy.
  // =========================================================================
  describe('(d) AI suggest button role visibility', () => {
    const detail = {
      kind: 1,
      conversation: {
        id: mockMessageItem.id,
        canReply: true,
        isReplyWindowOpen: true,
        messages: [],
      },
    }

    it('shows AI suggest button for Admin', () => {
      useAuthStore.getState().setAuth('token', {
        email: 'admin@vni.local',
        userName: 'Admin',
        roles: ['Admin'],
      })

      render(<InboxDetail item={mockMessageItem} detail={detail} loading={false} />)
      expect(screen.getByTestId('btn-ai-suggest')).toBeInTheDocument()
      expect(screen.getByTestId('btn-ai-suggest')).toHaveTextContent('AI gợi ý')
    })

    it('shows AI suggest button for ContentManager', () => {
      useAuthStore.getState().setAuth('token', {
        email: 'cm@vni.local',
        userName: 'ContentManager',
        roles: ['ContentManager'],
      })

      render(<InboxDetail item={mockMessageItem} detail={detail} loading={false} />)
      expect(screen.getByTestId('btn-ai-suggest')).toBeInTheDocument()
    })

    it('shows AI suggest button for Reviewer', () => {
      useAuthStore.getState().setAuth('token', {
        email: 'reviewer@vni.local',
        userName: 'Reviewer',
        roles: ['Reviewer'],
      })

      render(<InboxDetail item={mockMessageItem} detail={detail} loading={false} />)
      expect(screen.getByTestId('btn-ai-suggest')).toBeInTheDocument()
    })

    it('hides AI suggest button for Viewer (read-only)', () => {
      useAuthStore.getState().setAuth('token', {
        email: 'viewer@vni.local',
        userName: 'Viewer',
        roles: ['Viewer'],
      })

      render(<InboxDetail item={mockMessageItem} detail={detail} loading={false} />)
      expect(screen.queryByTestId('btn-ai-suggest')).not.toBeInTheDocument()
      expect(screen.getByTestId('viewer-readonly-notice')).toBeInTheDocument()
    })

    // N-b: không dựa vào footer Viewer — user không role / canCare=false vẫn ẩn nút
    it('N-b: hides AI suggest when user has no roles (canCare=false, isReadOnly=false)', () => {
      useAuthStore.getState().setAuth('token', {
        email: 'nobody@vni.local',
        userName: 'Nobody',
        roles: [],
      })

      render(<InboxDetail item={mockMessageItem} detail={detail} loading={false} />)
      expect(screen.queryByTestId('btn-ai-suggest')).not.toBeInTheDocument()
      expect(screen.queryByTestId('viewer-readonly-notice')).not.toBeInTheDocument()
    })

    it('N-b: hides AI suggest when canCare=false and isReadOnly=false are passed as props', () => {
      render(
        <InboxDetail
          item={mockMessageItem}
          detail={detail}
          loading={false}
          canCare={false}
          isReadOnly={false}
        />,
      )
      expect(screen.queryByTestId('btn-ai-suggest')).not.toBeInTheDocument()
    })
  })

  // =========================================================================
  // (e) Bấm -> gọi POST suggest-reply, điền draft, KHÔNG gọi API gửi / trả lời.
  // =========================================================================
  describe('(e) Clicking AI suggest calls suggestReply API, fills draft, does NOT send', () => {
    it('calls suggestReply, populates draft into input, and does not send', async () => {
      const detail = {
        kind: 1,
        conversation: {
          id: mockMessageItem.id,
          canReply: true,
          isReplyWindowOpen: true,
          messages: [],
        },
      }

      const suggestSpy = vi.spyOn(inboxApi, 'suggestReply').mockResolvedValue({
        draft: 'Dạ chào bạn, VNI xin được tư vấn về lộ trình khóa học cho bạn nhé!',
      })
      const sendMsgSpy = vi.spyOn(inboxApi, 'sendMessage').mockResolvedValue({})
      const onSendReplyMock = vi.fn()

      render(
        <InboxDetail
          item={mockMessageItem}
          detail={detail}
          loading={false}
          onSendReply={onSendReplyMock}
        />,
      )

      const aiButton = screen.getByTestId('btn-ai-suggest')
      const input = screen.getByTestId('reply-input')

      expect(input.value).toBe('')

      fireEvent.click(aiButton)

      await waitFor(() => {
        expect(suggestSpy).toHaveBeenCalledWith(mockMessageItem.kind, mockMessageItem.id)
      })

      await waitFor(() => {
        expect(input.value).toBe('Dạ chào bạn, VNI xin được tư vấn về lộ trình khóa học cho bạn nhé!')
      })

      // Must NOT send automatically
      expect(sendMsgSpy).not.toHaveBeenCalled()
      expect(onSendReplyMock).not.toHaveBeenCalled()
    })

    it('calls suggestReply with normalized comment kind for comment items', async () => {
      const commentDetail = {
        kind: 2,
        thread: {
          id: mockCommentItem.id,
          comment: { text: 'Học phí thế nào?' },
          replies: [],
          capabilities: { canReply: true },
        },
      }

      const suggestSpy = vi.spyOn(inboxApi, 'suggestReply').mockResolvedValue({
        draft: 'Chào bạn, học phí khóa học là 3.000.000đ ạ.',
      })
      const replyCommentSpy = vi.spyOn(inboxApi, 'replyComment').mockResolvedValue({})
      const onSendReplyMock = vi.fn()

      render(
        <InboxDetail
          item={mockCommentItem}
          detail={commentDetail}
          loading={false}
          onSendReply={onSendReplyMock}
        />,
      )

      fireEvent.click(screen.getByTestId('btn-ai-suggest'))

      await waitFor(() => {
        expect(suggestSpy).toHaveBeenCalledWith(mockCommentItem.kind, mockCommentItem.id)
      })

      await waitFor(() => {
        expect(screen.getByTestId('reply-input').value).toBe('Chào bạn, học phí khóa học là 3.000.000đ ạ.')
      })

      expect(replyCommentSpy).not.toHaveBeenCalled()
      expect(onSendReplyMock).not.toHaveBeenCalled()
    })
  })

  // =========================================================================
  // (f) Lỗi -> toast, giữ nội dung đang gõ.
  // =========================================================================
  describe('(f) AI suggest failure shows toast and preserves user typed content', () => {
    it('shows toast error and preserves typed input when suggestReply rejects', async () => {
      const detail = {
        kind: 1,
        conversation: {
          id: mockMessageItem.id,
          canReply: true,
          isReplyWindowOpen: true,
          messages: [],
        },
      }

      vi.spyOn(inboxApi, 'suggestReply').mockRejectedValue(
        new Error('AI provider service temporarily unavailable'),
      )
      const toastErrorSpy = vi.spyOn(toastModule.toast, 'error')

      render(
        <InboxDetail
          item={mockMessageItem}
          detail={detail}
          loading={false}
        />,
      )

      const input = screen.getByTestId('reply-input')

      // User types some message
      fireEvent.change(input, { target: { value: 'Nội dung người dùng đang gõ dở dang' } })
      expect(input.value).toBe('Nội dung người dùng đang gõ dở dang')

      // Click AI suggest
      fireEvent.click(screen.getByTestId('btn-ai-suggest'))

      // Toast error should be triggered
      await waitFor(() => {
        expect(toastErrorSpy).toHaveBeenCalledWith('AI provider service temporarily unavailable')
      })

      // Toast banner in UI should appear
      await waitFor(() => {
        expect(screen.getByTestId('ai-suggest-toast')).toBeInTheDocument()
      })
      expect(screen.getByTestId('ai-suggest-toast')).toHaveTextContent(
        'AI provider service temporarily unavailable',
      )

      // Typed content must be preserved
      expect(input.value).toBe('Nội dung người dùng đang gõ dở dang')
    })
  })
})
