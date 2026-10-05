import { render, screen } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { describe, expect, it, vi } from 'vitest'
import PlatformsPage from '../pages/PlatformsPage'

vi.mock('@/shared/hooks/usePermissions', () => ({
  usePermissions: () => ({ canManageChannels: false, hasRole: () => false }),
}))
vi.mock('@/shared/stores/toastStore', () => ({ toast: { success: vi.fn(), error: vi.fn() } }))

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

describe('PlatformsPage orphan channel order', () => {
  it('lists manual channels VNi first then A→Z and keeps attached channels out', () => {
    render(<MemoryRouter><PlatformsPage /></MemoryRouter>)

    const names = [...screen.getByTestId('orphans').querySelectorAll('li')].map((li) => li.textContent)
    expect(names).toEqual(['VNi Hà Nội', 'Alpha', 'Zeta'])
  })
})
