import React from 'react'
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { render, screen, fireEvent, waitFor } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import App from '../App'
import { inboxApi } from '../features/inbox/api/inboxApi'
import { customerApi } from '../features/customers/api/customerApi'
import { useAuthStore } from '../auth/authStore'

// Review INBOX-CRM-LAYOUT-01 F1: /customers/:id phải tồn tại trong router thật của App.
describe('F1 — route /customers/:id (App thật)', () => {
  const customer = { id: 'cust-uuid-001', displayName: 'Khách Hồ Sơ', phoneE164: '+84912345678' }
  const panelData = {
    linked: true,
    participant: { displayName: 'Nguyễn Văn A', externalId: 'fb_1', channelName: 'Page', platform: 1 },
    customer: { ...customer, email: null, tagIds: [], identities: [] },
    stats: { messageCount: 1, commentCount: 0 },
    media: [],
    activities: [],
  }

  beforeEach(() => {
    vi.restoreAllMocks()
    localStorage.clear()
    useAuthStore.getState().setAuth('mock-token', {
      id: 'u-admin', email: 'admin@vni.local', userName: 'Admin', roles: ['Admin'],
    })
    vi.spyOn(inboxApi, 'listUsers').mockResolvedValue([])
    vi.spyOn(inboxApi, 'listTags').mockResolvedValue([])
    vi.spyOn(inboxApi, 'listChannels').mockResolvedValue([])
    vi.spyOn(customerApi, 'getTimeline').mockResolvedValue([])
    vi.spyOn(customerApi, 'listNotes').mockResolvedValue([])
    vi.spyOn(customerApi, 'filter').mockResolvedValue({
      items: [{ id: 'cust-list-1', displayName: 'Khách Trong Danh Sách' }],
      total: 1,
    })
  })

  afterEach(() => {
    vi.restoreAllMocks()
  })

  const mockInbox = () => {
    vi.spyOn(inboxApi, 'filter').mockResolvedValue({
      items: [{
        id: 'conv-1', kind: 1, displayName: 'Nguyễn Văn A', snippet: 'hi', channelName: 'Page',
        lastCustomerActivityAt: '2026-10-07T03:15:00Z', status: 1, canReply: true, tags: [],
      }],
      total: 1,
    })
    vi.spyOn(inboxApi, 'getMessage').mockResolvedValue({
      kind: 1,
      conversation: { id: 'conv-1', channelName: 'Page', participantName: 'Nguyễn Văn A', messages: [] },
      tags: [],
      replyEndpoint: '/api/PageMessage/conv-1/send',
    })
    vi.spyOn(inboxApi, 'getCustomer').mockResolvedValue(panelData)
  }

  it('"Mở hồ sơ" trong inbox mở CustomerProfile của đúng id (customerApi.get)', async () => {
    mockInbox()
    const getSpy = vi.spyOn(customerApi, 'get').mockResolvedValue(customer)

    render(<MemoryRouter initialEntries={['/inbox']}><App /></MemoryRouter>)

    await waitFor(() => expect(screen.getByTestId('btn-open-customer-profile')).toBeInTheDocument())
    fireEvent.click(screen.getByTestId('btn-open-customer-profile'))

    await waitFor(() => expect(screen.getByTestId('profile-display-name')).toHaveTextContent('Khách Hồ Sơ'))
    expect(getSpy).toHaveBeenCalledWith('cust-uuid-001')
    expect(screen.queryByTestId('inbox-feature')).not.toBeInTheDocument()
  })

  it('vào thẳng /customers/:id (refresh) mở hồ sơ; Quay lại về danh sách /customers', async () => {
    const getSpy = vi.spyOn(customerApi, 'get').mockResolvedValue(customer)

    render(<MemoryRouter initialEntries={['/customers/cust-uuid-001']}><App /></MemoryRouter>)

    await waitFor(() => expect(screen.getByTestId('profile-display-name')).toHaveTextContent('Khách Hồ Sơ'))
    expect(getSpy).toHaveBeenCalledWith('cust-uuid-001')

    fireEvent.click(screen.getByTestId('btn-profile-back'))
    await waitFor(() => expect(screen.getByTestId('customer-row-cust-list-1')).toBeInTheDocument())
  })

  it('chọn khách trong danh sách điều hướng sang /customers/:id', async () => {
    const getSpy = vi.spyOn(customerApi, 'get').mockResolvedValue({ ...customer, id: 'cust-list-1', displayName: 'Khách Trong Danh Sách' })

    render(<MemoryRouter initialEntries={['/customers']}><App /></MemoryRouter>)

    await waitFor(() => expect(screen.getByTestId('customer-name-cust-list-1')).toBeInTheDocument())
    fireEvent.click(screen.getByTestId('customer-name-cust-list-1'))

    await waitFor(() => expect(screen.getByTestId('profile-display-name')).toBeInTheDocument())
    expect(getSpy).toHaveBeenCalledWith('cust-list-1')
  })

  it('id không tồn tại (404) hiện "Không tìm thấy khách" + nút về danh sách, không crash', async () => {
    vi.spyOn(customerApi, 'get').mockRejectedValue({ response: { status: 404 }, message: 'Request failed' })

    render(<MemoryRouter initialEntries={['/customers/khong-ton-tai']}><App /></MemoryRouter>)

    await waitFor(() => expect(screen.getByTestId('customer-profile-error')).toHaveTextContent('Không tìm thấy khách'))

    fireEvent.click(screen.getByTestId('btn-profile-back-list'))
    await waitFor(() => expect(screen.getByTestId('customer-row-cust-list-1')).toBeInTheDocument())
  })
})
