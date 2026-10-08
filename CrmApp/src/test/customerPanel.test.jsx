import React from 'react'
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { render, screen, fireEvent, waitFor, act } from '@testing-library/react'
import { MemoryRouter, Routes, Route } from 'react-router-dom'
import { CustomerPanel } from '../features/inbox/components/CustomerPanel'
import InboxFeature from '../features/inbox/InboxFeature'
import { inboxApi } from '../features/inbox/api/inboxApi'
import { useAuthStore } from '../auth/authStore'

describe('AC e2810e98 (f)-(i) & AC e68ba436 (b) — CrmApp Customer Panel SO9', () => {
  const mockTags = [
    { id: 'tag-1', name: 'VIP Gold', colorHex: '#eab308' },
    { id: 'tag-2', name: 'Quan tâm tuyển sinh', colorHex: '#3b82f6' },
  ]

  const mockCustomerDataLinked = {
    linked: true,
    participant: {
      displayName: 'Nguyễn Văn A',
      externalId: 'fb_1001',
      avatarUrl: 'https://cdn.example.com/avatars/user1.jpg',
      channelName: 'VNI Tuyển sinh',
      platform: 1, // Facebook
    },
    customer: {
      id: 'cust-uuid-001',
      displayName: 'Nguyễn Văn A (Hồ sơ)',
      phoneE164: '+84912345678',
      email: 'nguyenvana@example.com',
      tagIds: ['tag-1', 'tag-2'],
      noteCount: 2,
      reminderCount: 1,
      identities: [
        {
          platform: 1,
          socialChannelId: '00000000-0000-0000-0000-000000000001',
          channelName: 'VNI Fanpage Tuyển sinh',
          externalId: 'fb_1001',
          displayName: 'Nguyễn Văn A',
          avatarUrl: 'https://cdn.example.com/avatars/user1.jpg',
        },
        {
          platform: 6, // Zalo
          socialChannelId: '00000000-0000-0000-0000-000000000002',
          channelName: 'Zalo Tuyển sinh VNI',
          externalId: 'zalo_2002',
          displayName: 'Anh Nguyễn',
          avatarUrl: null,
        },
      ],
    },
    stats: {
      messageCount: 18,
      commentCount: 4,
      firstInteractionAt: '2026-09-15T08:30:00Z',
      lastInteractionAt: '2026-10-07T14:20:00Z',
      isReplyWindowOpen: true,
      replyWindowClosesAt: '2026-10-08T14:20:00Z',
      assignedUserId: 'u-reviewer',
      assignedTo: 'Chuyên viên Hỗ trợ',
      inboxStatus: 2,
    },
    media: [
      { url: 'https://cdn.example.com/media/pic1.jpg', type: 'image', sentAt: '2026-10-07T10:00:00Z' },
      { url: 'https://cdn.example.com/media/pic2.jpg', type: 'image', sentAt: '2026-10-07T11:00:00Z' },
    ],
    activities: [
      {
        id: 'act-1',
        title: 'Khách hàng gửi tin nhắn',
        at: '2026-10-07T14:20:00Z',
        actor: 'Nguyễn Văn A',
      },
      {
        id: 'act-2',
        title: 'Gán người xử lý cho Chuyên viên Hỗ trợ',
        at: '2026-10-07T14:25:00Z',
        actor: 'Admin',
      },
    ],
  }

  beforeEach(() => {
    vi.restoreAllMocks()
    localStorage.clear()
    useAuthStore.getState().setAuth('mock-token', {
      id: 'u-reviewer',
      email: 'reviewer@vni.local',
      userName: 'Reviewer Care',
      roles: ['Reviewer'],
    })

    vi.spyOn(inboxApi, 'listUsers').mockResolvedValue([])
    vi.spyOn(inboxApi, 'listTags').mockResolvedValue(mockTags)
    vi.spyOn(inboxApi, 'listChannels').mockResolvedValue([])
  })

  afterEach(() => {
    vi.restoreAllMocks()
  })

  describe('AC e2810e98 (f) — Render 5 sections in SO9 order and collapsible', () => {
    it('renders all 5 sections in strict SO9 order: Thông tin khách, Kênh liên lạc, Thống kê tương tác, Ảnh/Video, Hoạt động', async () => {
      vi.spyOn(inboxApi, 'getCustomer').mockResolvedValue(mockCustomerDataLinked)

      render(
        <MemoryRouter>
          <CustomerPanel
            item={{ id: 'conv-101', kind: 1, displayName: 'Nguyễn Văn A' }}
            tags={mockTags}
            isReadOnly={false}
          />
        </MemoryRouter>,
      )

      await waitFor(() => {
        expect(screen.getByTestId('customer-section-info')).toBeInTheDocument()
        expect(screen.getByTestId('customer-section-channels')).toBeInTheDocument()
        expect(screen.getByTestId('customer-section-stats')).toBeInTheDocument()
        expect(screen.getByTestId('customer-section-media')).toBeInTheDocument()
        expect(screen.getByTestId('customer-section-activities')).toBeInTheDocument()
      })

      // Verify header titles and their order in the DOM
      const sectionHeadings = screen.getAllByRole('heading', { level: 4 })
      expect(sectionHeadings).toHaveLength(5)
      expect(sectionHeadings[0]).toHaveTextContent('Thông tin khách')
      expect(sectionHeadings[1]).toHaveTextContent('Kênh liên lạc')
      expect(sectionHeadings[2]).toHaveTextContent('Thống kê tương tác')
      expect(sectionHeadings[3]).toHaveTextContent('Ảnh/Video')
      expect(sectionHeadings[4]).toHaveTextContent('Hoạt động')

      // Verify Section 1 details
      expect(screen.getByTestId('customer-display-name')).toHaveTextContent('Nguyễn Văn A (Hồ sơ)')
      expect(screen.getByTestId('customer-phone')).toHaveTextContent('+84912345678')
      expect(screen.getByTestId('customer-email')).toHaveTextContent('nguyenvana@example.com')
      expect(screen.getByTestId('customer-tags')).toHaveTextContent('VIP Gold')
      expect(screen.getByTestId('customer-tags')).toHaveTextContent('Quan tâm tuyển sinh')

      // Verify Section 2 details (identities)
      expect(screen.getByTestId('customer-identities-list')).toBeInTheDocument()
      expect(screen.getByTestId('identity-item-0')).toHaveTextContent('VNI Fanpage Tuyển sinh')
      expect(screen.getByTestId('identity-item-1')).toHaveTextContent('Zalo Tuyển sinh VNI')

      // Verify Section 3 details (stats & 24h window)
      expect(screen.getByTestId('stat-messages')).toHaveTextContent('18')
      expect(screen.getByTestId('stat-comments')).toHaveTextContent('4')
      expect(screen.getByTestId('badge-reply-window')).toHaveTextContent('Còn hạn 24h')
      expect(screen.getByTestId('stat-assignee')).toHaveTextContent('Chuyên viên Hỗ trợ')
      expect(screen.getByTestId('stat-inbox-status')).toHaveTextContent('Đang xử lý')

      // Verify Section 4 details (media thumbnails)
      expect(screen.getByTestId('customer-media-grid')).toBeInTheDocument()
      expect(screen.getByTestId('media-thumb-0')).toBeInTheDocument()
      expect(screen.getByTestId('media-thumb-1')).toBeInTheDocument()

      // Verify Section 5 details (timeline)
      expect(screen.getByTestId('customer-timeline')).toBeInTheDocument()
      expect(screen.getByTestId('activity-item-0')).toHaveTextContent('Khách hàng gửi tin nhắn')
    })

    it('collapses and expands sections when clicking their headers', async () => {
      vi.spyOn(inboxApi, 'getCustomer').mockResolvedValue(mockCustomerDataLinked)

      render(
        <MemoryRouter>
          <CustomerPanel
            item={{ id: 'conv-101', kind: 1, displayName: 'Nguyễn Văn A' }}
            tags={mockTags}
          />
        </MemoryRouter>,
      )

      await waitFor(() => {
        expect(screen.getByTestId('customer-display-name')).toBeInTheDocument()
      })

      // Toggle Section 1
      const toggleInfoBtn = screen.getByTestId('toggle-section-info')
      fireEvent.click(toggleInfoBtn)
      expect(screen.queryByTestId('customer-display-name')).not.toBeInTheDocument()

      // Toggle Section 1 back open
      fireEvent.click(toggleInfoBtn)
      expect(screen.getByTestId('customer-display-name')).toBeInTheDocument()
    })

    it('opens lightbox modal when clicking media thumbnail and closes on close button', async () => {
      vi.spyOn(inboxApi, 'getCustomer').mockResolvedValue(mockCustomerDataLinked)

      render(
        <MemoryRouter>
          <CustomerPanel
            item={{ id: 'conv-101', kind: 1 }}
            tags={mockTags}
          />
        </MemoryRouter>,
      )

      await waitFor(() => {
        expect(screen.getByTestId('media-thumb-0')).toBeInTheDocument()
      })

      // Click thumbnail to open lightbox
      fireEvent.click(screen.getByTestId('media-thumb-0'))
      expect(screen.getByTestId('customer-lightbox')).toBeInTheDocument()
      expect(screen.getByRole('dialog', { name: 'Xem ảnh lớn' })).toBeInTheDocument()

      // Click close button
      fireEvent.click(screen.getByTestId('btn-close-lightbox'))
      expect(screen.queryByTestId('customer-lightbox')).not.toBeInTheDocument()
    })
  })

  describe('AC e2810e98 (g) — Missing email/phone shows "—" and linked=false shows "Chưa liên kết hồ sơ"', () => {
    it('shows "—" when phone and email are null on linked customer', async () => {
      const dataWithNullContact = {
        ...mockCustomerDataLinked,
        customer: {
          ...mockCustomerDataLinked.customer,
          phoneE164: null,
          email: null,
        },
      }
      vi.spyOn(inboxApi, 'getCustomer').mockResolvedValue(dataWithNullContact)

      render(
        <MemoryRouter>
          <CustomerPanel
            item={{ id: 'conv-101', kind: 1 }}
            tags={mockTags}
          />
        </MemoryRouter>,
      )

      await waitFor(() => {
        expect(screen.getByTestId('customer-phone')).toHaveTextContent('—')
        expect(screen.getByTestId('customer-email')).toHaveTextContent('—')
      })
      expect(screen.queryByTestId('badge-unlinked')).not.toBeInTheDocument()
    })

    it('shows badge "Chưa liên kết hồ sơ" and participant details when linked=false', async () => {
      const unlinkedData = {
        linked: false,
        participant: {
          displayName: 'Khách lạ Facebook',
          externalId: 'fb_unknown_999',
          avatarUrl: null,
          channelName: 'VNI Tuyển sinh',
          platform: 1,
        },
        customer: null,
        stats: {
          messageCount: 1,
          commentCount: 0,
          firstInteractionAt: '2026-10-08T02:00:00Z',
          lastInteractionAt: '2026-10-08T02:00:00Z',
          isReplyWindowOpen: false,
          replyWindowClosesAt: null,
          assignedTo: null,
          inboxStatus: 1,
        },
        media: [],
        activities: [],
      }
      vi.spyOn(inboxApi, 'getCustomer').mockResolvedValue(unlinkedData)

      render(
        <MemoryRouter>
          <CustomerPanel
            item={{ id: 'conv-102', kind: 1 }}
            tags={mockTags}
          />
        </MemoryRouter>,
      )

      await waitFor(() => {
        expect(screen.getByTestId('badge-unlinked')).toBeInTheDocument()
        expect(screen.getByTestId('badge-unlinked')).toHaveTextContent('Chưa liên kết hồ sơ')
      })

      expect(screen.getByTestId('customer-display-name')).toHaveTextContent('Khách lạ Facebook')
      expect(screen.getByTestId('customer-phone')).toHaveTextContent('—')
      expect(screen.getByTestId('customer-email')).toHaveTextContent('—')
      expect(screen.getByTestId('badge-reply-window')).toHaveTextContent('Hết hạn')
      // "Mở hồ sơ" button must not exist when unlinked
      expect(screen.queryByTestId('btn-open-customer-profile')).not.toBeInTheDocument()
    })
  })

  describe('AC e2810e98 (h) — Viewer role has no edit or open buttons', () => {
    it('does not render "Mở hồ sơ" button and has no edit controls when isReadOnly=true (Viewer)', async () => {
      vi.spyOn(inboxApi, 'getCustomer').mockResolvedValue(mockCustomerDataLinked)

      render(
        <MemoryRouter>
          <CustomerPanel
            item={{ id: 'conv-101', kind: 1 }}
            tags={mockTags}
            isReadOnly={true}
          />
        </MemoryRouter>,
      )

      await waitFor(() => {
        expect(screen.getByTestId('customer-display-name')).toBeInTheDocument()
      })

      // No open profile button
      expect(screen.queryByTestId('btn-open-customer-profile')).not.toBeInTheDocument()

      // No edit controls or form inputs inside panel content
      const content = screen.getByTestId('customer-panel-content')
      expect(content.querySelector('input')).toBeNull()
      expect(content.querySelector('textarea')).toBeNull()
      expect(content.querySelector('select')).toBeNull()
    })

    it('renders "Mở hồ sơ" button when not read-only and customer is linked', async () => {
      vi.spyOn(inboxApi, 'getCustomer').mockResolvedValue(mockCustomerDataLinked)

      render(
        <MemoryRouter initialEntries={['/inbox']}>
          <Routes>
            <Route
              path="/inbox"
              element={
                <CustomerPanel
                  item={{ id: 'conv-101', kind: 1 }}
                  tags={mockTags}
                  isReadOnly={false}
                />
              }
            />
            <Route path="/customers/:id" element={<div data-testid="target-customer-page">Customer Details</div>} />
          </Routes>
        </MemoryRouter>,
      )

      await waitFor(() => {
        expect(screen.getByTestId('btn-open-customer-profile')).toBeInTheDocument()
      })

      fireEvent.click(screen.getByTestId('btn-open-customer-profile'))

      await waitFor(() => {
        expect(screen.getByTestId('target-customer-page')).toBeInTheDocument()
      })
    })
  })

  describe('AC e2810e98 (i) — Changing conversation re-fetches and does not show old customer data during loading', () => {
    it('clears previous customer data immediately, shows skeleton, and loads new customer on conversation change', async () => {
      let resolveSecondCustomer
      const secondPromise = new Promise((resolve) => {
        resolveSecondCustomer = resolve
      })

      const getCustomerSpy = vi.spyOn(inboxApi, 'getCustomer').mockImplementation((kind, id) => {
        if (id === 'conv-1') {
          return Promise.resolve({
            ...mockCustomerDataLinked,
            customer: {
              ...mockCustomerDataLinked.customer,
              id: 'cust-1',
              displayName: 'Khách Hàng Một',
            },
          })
        }
        if (id === 'conv-2') {
          return secondPromise
        }
        return Promise.reject(new Error('Unknown id'))
      })

      const { rerender } = render(
        <MemoryRouter>
          <CustomerPanel
            item={{ id: 'conv-1', kind: 1 }}
            tags={mockTags}
          />
        </MemoryRouter>,
      )

      // 1. Wait for First customer to display
      await waitFor(() => {
        expect(screen.getByTestId('customer-display-name')).toHaveTextContent('Khách Hàng Một')
      })

      // 2. Switch conversation to conv-2
      rerender(
        <MemoryRouter>
          <CustomerPanel
            item={{ id: 'conv-2', kind: 1 }}
            tags={mockTags}
          />
        </MemoryRouter>,
      )

      // 3. Immediately during loading, old customer data must NOT be visible and skeleton must be shown
      expect(screen.queryByText('Khách Hàng Một')).not.toBeInTheDocument()
      expect(screen.getByTestId('customer-panel-skeleton')).toBeInTheDocument()

      // 4. Resolve second customer
      await act(async () => {
        resolveSecondCustomer({
          ...mockCustomerDataLinked,
          customer: {
            ...mockCustomerDataLinked.customer,
            id: 'cust-2',
            displayName: 'Khách Hàng Hai',
          },
        })
      })

      // 5. Verify new customer is loaded and API was called with conv-2
      await waitFor(() => {
        expect(screen.getByTestId('customer-display-name')).toHaveTextContent('Khách Hàng Hai')
      })
      expect(getCustomerSpy).toHaveBeenCalledWith(1, 'conv-2')
    })

    it('shows error notice and "Thử lại" button when getCustomer fails without crashing', async () => {
      const getCustomerSpy = vi.spyOn(inboxApi, 'getCustomer')
        .mockRejectedValueOnce(new Error('Mạng không ổn định'))
        .mockResolvedValueOnce(mockCustomerDataLinked)

      render(
        <MemoryRouter>
          <CustomerPanel
            item={{ id: 'conv-fail', kind: 1 }}
            tags={mockTags}
          />
        </MemoryRouter>,
      )

      await waitFor(() => {
        expect(screen.getByTestId('customer-panel-error')).toBeInTheDocument()
      })
      expect(screen.getByTestId('customer-panel-error')).toHaveTextContent('Mạng không ổn định')

      // Click "Thử lại"
      const retryBtn = screen.getByTestId('btn-retry-customer')
      fireEvent.click(retryBtn)

      await waitFor(() => {
        expect(screen.getByTestId('customer-display-name')).toHaveTextContent('Nguyễn Văn A (Hồ sơ)')
      })
      expect(getCustomerSpy).toHaveBeenCalledTimes(2)
    })
  })

  describe('AC e68ba436 (b) — Integration in InboxFeature renders customer-panel with real CustomerPanel', () => {
    it('renders real CustomerPanel inside customer-panel and links "Xem hồ sơ khách" only when linked', async () => {
      vi.spyOn(inboxApi, 'filter').mockResolvedValue({
        items: [
          {
            id: 'conv-item-1',
            kind: 1,
            channelName: 'Page VNI',
            displayName: 'Nguyễn Văn A',
            snippet: 'Em muốn tư vấn khóa học',
            lastCustomerActivityAt: '2026-10-07T03:15:00Z',
            status: 1,
            assignedUserId: null,
            assignedTo: null,
            unreadCount: 0,
            canReply: true,
            tags: [],
          },
        ],
        total: 1,
      })

      vi.spyOn(inboxApi, 'getMessage').mockResolvedValue({
        kind: 1,
        conversation: {
          id: 'conv-item-1',
          channelName: 'Page VNI',
          participantName: 'Nguyễn Văn A',
          messages: [
            {
              id: 'm1',
              text: 'Em muốn tư vấn khóa học',
              isFromPage: false,
              sentAt: '2026-10-07T03:15:00Z',
            },
          ],
        },
        tags: [],
        replyEndpoint: '/api/PageMessage/conv-item-1/send',
      })

      vi.spyOn(inboxApi, 'getCustomer').mockResolvedValue(mockCustomerDataLinked)

      render(
        <MemoryRouter>
          <InboxFeature />
        </MemoryRouter>,
      )

      // Check all 3 columns exist
      await waitFor(() => {
        expect(screen.getByTestId('conversation-list')).toBeInTheDocument()
        expect(screen.getByTestId('inbox-detail-pane')).toBeInTheDocument()
        expect(screen.getByTestId('customer-panel')).toBeInTheDocument()
      })

      // Verify real CustomerPanel is rendered inside customer-panel
      await waitFor(() => {
        expect(screen.getByTestId('customer-section-info')).toBeInTheDocument()
        expect(screen.getByTestId('customer-section-stats')).toBeInTheDocument()
      })

      // Verify InboxDetail has "Xem hồ sơ khách →" because customer is linked
      await waitFor(() => {
        expect(screen.getByTestId('btn-view-customer-profile')).toBeInTheDocument()
      })
    })
  })
})
