import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { describe, expect, it, vi } from 'vitest'
import PlatformsPage from '../pages/PlatformsPage'

vi.mock('@/shared/hooks/usePermissions', () => ({
  usePermissions: () => ({ canManageChannels: false, hasRole: () => false }),
}))
vi.mock('@/shared/stores/toastStore', () => ({
  toast: { success: vi.fn(), error: vi.fn(), warning: vi.fn() },
}))

const { mutation } = vi.hoisted(() => ({
  mutation: () => ({ mutate: () => {}, mutateAsync: () => {}, isPending: false }),
}))
vi.mock('../hooks/useSocialChannels', () => ({
  useCreateSocialChannel: mutation,
  useDeleteSocialChannel: mutation,
  useDisconnectSocialConnection: mutation,
  useMetaConnectUrl: mutation,
  useThreadsConnectUrl: mutation,
  useTikTokConnectUrl: mutation,
  useUpdateSocialChannel: mutation,
  useSocialConnections: () => ({ data: [], isLoading: false, isError: false, refetch: vi.fn() }),
  useSocialChannelAll: () => ({
    data: [
      { id: 'z', pageName: 'Zeta' },
      { id: 'v1', pageName: 'VNi Hà Nội' },
      { id: 'a', pageName: 'Alpha' },
      { id: 'attached', pageName: 'VNi Đã Gắn Tài Khoản', socialConnectionId: 'conn-1' },
    ],
    isLoading: false,
    isError: false,
    refetch: vi.fn(),
  }),
}))
vi.mock('../components/ConnectionCard', () => ({ default: () => null }))
vi.mock('../components/SocialChannelFormModal', () => ({ default: () => null }))
vi.mock('../components/SocialChannelTable', () => ({
  default: ({ items }) => <ol data-testid="orphans">{items.map((item) => <li key={item.id}>{item.pageName}</li>)}</ol>,
}))
vi.mock('../components/ChannelGroupTab', () => ({
  default: () => <div data-testid="channel-group-tab">ChannelGroupTab</div>,
}))

describe('PlatformsPage orphan channel order', () => {
  it('lists manual channels VNi first then A→Z and keeps attached channels out', () => {
    render(<MemoryRouter><PlatformsPage /></MemoryRouter>)

    const names = [...screen.getByTestId('orphans').querySelectorAll('li')].map((li) => li.textContent)
    expect(names).toEqual(['VNi Hà Nội', 'Alpha', 'Zeta'])
  })

  it('has Nhóm kênh tab that mounts ChannelGroupTab', async () => {
    const user = userEvent.setup()
    render(<MemoryRouter><PlatformsPage /></MemoryRouter>)

    expect(screen.getByRole('tab', { name: 'Nhóm kênh' })).toBeInTheDocument()
    expect(screen.queryByTestId('channel-group-tab')).not.toBeInTheDocument()

    await user.click(screen.getByRole('tab', { name: 'Nhóm kênh' }))
    expect(screen.getByTestId('channel-group-tab')).toBeInTheDocument()
  })
})
