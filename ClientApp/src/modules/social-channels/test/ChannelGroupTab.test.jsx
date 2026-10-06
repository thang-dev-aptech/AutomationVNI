import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import ChannelGroupTab from '../components/ChannelGroupTab'

const {
  createMutateAsync,
  updateMutateAsync,
  deleteMutateAsync,
  confirmMock,
  toastError,
  toastSuccess,
} = vi.hoisted(() => ({
  createMutateAsync: vi.fn(),
  updateMutateAsync: vi.fn(),
  deleteMutateAsync: vi.fn(),
  confirmMock: vi.fn(),
  toastError: vi.fn(),
  toastSuccess: vi.fn(),
}))

vi.mock('@/shared/hooks/usePermissions', () => ({
  usePermissions: vi.fn(),
}))

vi.mock('@/shared/stores/toastStore', () => ({
  toast: { success: (...args) => toastSuccess(...args), error: (...args) => toastError(...args) },
}))

vi.mock('@/shared/utils/confirmAction', () => ({
  confirmAction: (...args) => confirmMock(...args),
}))

vi.mock('@/shared/utils/apiHelpers', async (importOriginal) => {
  const actual = await importOriginal()
  return {
    ...actual,
    getErrorMessage: (err) => err?.message || 'Lỗi',
  }
})

vi.mock('../hooks/useChannelGroups', () => ({
  useChannelGroupAll: vi.fn(),
  useCreateChannelGroup: () => ({
    mutateAsync: createMutateAsync,
    isPending: false,
  }),
  useUpdateChannelGroup: () => ({
    mutateAsync: updateMutateAsync,
    isPending: false,
  }),
  useDeleteChannelGroup: () => ({
    mutateAsync: deleteMutateAsync,
    isPending: false,
  }),
}))

vi.mock('../hooks/useSocialChannels', () => ({
  useSocialChannelAll: () => ({
    data: [
      { id: 'ch-1', pageName: 'VNi Hà Nội' },
      { id: 'ch-2', pageName: 'Alpha Page' },
    ],
    isLoading: false,
  }),
}))

vi.mock('@/shared/components/ChannelMultiSelect', () => ({
  default: function MockChannelMultiSelect({ value, onChange, label }) {
    return (
      <div>
        <span>{label}</span>
        <button type="button" onClick={() => onChange(['ch-1', 'ch-2'])}>
          Chọn 2 kênh
        </button>
        <span data-testid="selected-count">{value?.length ?? 0}</span>
      </div>
    )
  },
}))

vi.mock('@/shared/components/Modal', () => ({
  default: function MockModal({ open, title, children, footer, onClose }) {
    if (!open) return null
    return (
      <div role="dialog" aria-label={title}>
        <h2>{title}</h2>
        {children}
        <div>{footer}</div>
        <button type="button" onClick={onClose}>Đóng modal</button>
      </div>
    )
  },
}))

vi.mock('@/shared/components/LoadingState', () => ({ default: () => <div>Loading</div> }))
vi.mock('@/shared/components/ErrorState', () => ({ default: () => <div>Error</div> }))
vi.mock('@/shared/components/EmptyState', () => ({
  default: ({ message }) => <div>{message}</div>,
}))

import { usePermissions } from '@/shared/hooks/usePermissions'
import { useChannelGroupAll } from '../hooks/useChannelGroups'

const GROUPS = [
  {
    id: 'g1',
    name: 'Miền Bắc',
    description: 'FB miền Bắc',
    channelCount: 2,
    channels: [{ id: 'ch-1' }, { id: 'ch-2' }],
  },
  {
    id: 'g2',
    name: 'TikTok',
    channelCount: 1,
    channels: [{ id: 'ch-2' }],
  },
]

describe('ChannelGroupTab (channel-group-ui-test)', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    usePermissions.mockReturnValue({
      hasRole: (roles) => roles.includes('Admin') || roles.includes('ContentManager'),
    })
    useChannelGroupAll.mockReturnValue({
      data: GROUPS,
      isLoading: false,
      isError: false,
      error: null,
      refetch: vi.fn(),
    })
    createMutateAsync.mockResolvedValue({})
    updateMutateAsync.mockResolvedValue({})
    deleteMutateAsync.mockResolvedValue({})
    confirmMock.mockReturnValue(true)
  })

  it('lists groups with channel counts', () => {
    render(<ChannelGroupTab />)

    expect(screen.getByText('Miền Bắc')).toBeInTheDocument()
    expect(screen.getByText('TikTok')).toBeInTheDocument()
    expect(screen.getByText('2')).toBeInTheDocument()
    expect(screen.getByText('1')).toBeInTheDocument()
  })

  it('create sends name + channelIds payload', async () => {
    const user = userEvent.setup()
    render(<ChannelGroupTab />)

    await user.click(screen.getByRole('button', { name: /\+ Tạo nhóm/ }))
    await user.type(screen.getByLabelText(/Tên nhóm/), 'Nhóm mới')
    await user.click(screen.getByRole('button', { name: /Chọn 2 kênh/ }))
    await user.click(screen.getByRole('button', { name: /^Lưu$/ }))

    await waitFor(() => {
      expect(createMutateAsync).toHaveBeenCalledWith({
        name: 'Nhóm mới',
        description: null,
        channelIds: ['ch-1', 'ch-2'],
      })
    })
  })

  it('edit sends update payload with name + channelIds', async () => {
    const user = userEvent.setup()
    render(<ChannelGroupTab />)

    await user.click(screen.getAllByRole('button', { name: /^Sửa$/ })[0])
    const nameInput = screen.getByLabelText(/Tên nhóm/)
    await user.clear(nameInput)
    await user.type(nameInput, 'Miền Bắc 2')
    await user.click(screen.getByRole('button', { name: /^Lưu$/ }))

    await waitFor(() => {
      expect(updateMutateAsync).toHaveBeenCalledWith({
        id: 'g1',
        payload: {
          name: 'Miền Bắc 2',
          description: 'FB miền Bắc',
          channelIds: ['ch-1', 'ch-2'],
        },
      })
    })
  })

  it('delete requires confirmation', async () => {
    const user = userEvent.setup()
    confirmMock.mockReturnValueOnce(false)
    render(<ChannelGroupTab />)

    await user.click(screen.getAllByRole('button', { name: /^Xóa$/ })[0])
    expect(confirmMock).toHaveBeenCalled()
    expect(deleteMutateAsync).not.toHaveBeenCalled()

    confirmMock.mockReturnValueOnce(true)
    await user.click(screen.getAllByRole('button', { name: /^Xóa$/ })[0])
    await waitFor(() => {
      expect(deleteMutateAsync).toHaveBeenCalledWith('g1')
    })
  })

  it('Viewer does not see create/edit/delete buttons', () => {
    usePermissions.mockReturnValue({
      hasRole: () => false,
    })
    render(<ChannelGroupTab />)

    expect(screen.queryByRole('button', { name: /\+ Tạo nhóm/ })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /^Sửa$/ })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /^Xóa$/ })).not.toBeInTheDocument()
    expect(screen.getByText('Miền Bắc')).toBeInTheDocument()
  })

  it('shows backend 400 message on create failure', async () => {
    const user = userEvent.setup()
    createMutateAsync.mockRejectedValueOnce(new Error('Tên nhóm đã tồn tại'))
    render(<ChannelGroupTab />)

    await user.click(screen.getByRole('button', { name: /\+ Tạo nhóm/ }))
    await user.type(screen.getByLabelText(/Tên nhóm/), 'Trùng')
    await user.click(screen.getByRole('button', { name: /^Lưu$/ }))

    expect(await screen.findByRole('alert')).toHaveTextContent('Tên nhóm đã tồn tại')
  })
})
