import { render, screen, within } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { describe, expect, it, vi } from 'vitest'
import CommentsInboxPage from '../pages/CommentsInboxPage'

vi.mock('@/shared/hooks/usePermissions', () => ({ usePermissions: () => ({ hasRole: () => false }) }))
vi.mock('@/shared/stores/toastStore', () => ({ toast: { success: vi.fn(), error: vi.fn() } }))
vi.mock('@/modules/social-channels/hooks/useSocialChannels', () => ({
  useSocialChannelAll: () => ({
    data: [
      { id: 'z', pageName: 'Zeta' },
      { id: 'v1', pageName: 'VNi Hà Nội' },
      { id: 'a', pageName: 'Alpha' },
    ],
  }),
}))

const mutation = () => ({ mutate: vi.fn(), mutateAsync: vi.fn(), isPending: false })
vi.mock('../hooks/useComments', () => ({
  useAssignComment: () => mutation(),
  useCommentActions: () => ({ data: [] }),
  useCommentInbox: () => ({
    data: { items: [], total: 0, index: 1, size: 20 }, isLoading: false, isError: false, refetch: vi.fn(),
  }),
  useCommentModeration: () => mutation(),
  useCommentNote: () => mutation(),
  useCommentSummary: () => ({ data: {} }),
  useCommentThread: () => ({ data: null, isLoading: false }),
  useReplyComment: () => mutation(),
  useSetCommentStatus: () => mutation(),
  useSyncComments: () => mutation(),
}))

describe('CommentsInboxPage channel filter order', () => {
  it('lists VNi channels first, then A→Z', () => {
    render(<MemoryRouter><CommentsInboxPage /></MemoryRouter>)

    const options = within(screen.getByLabelText('Kênh')).getAllByRole('option').map((o) => o.textContent)
    expect(options).toEqual(['Tất cả', 'VNi Hà Nội', 'Alpha', 'Zeta'])
  })
})
