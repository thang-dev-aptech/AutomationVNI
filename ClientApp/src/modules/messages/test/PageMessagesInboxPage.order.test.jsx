import { render, screen, within } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import PageMessagesInboxPage from '../pages/PageMessagesInboxPage'

vi.mock('@/shared/hooks/usePermissions', () => ({ usePermissions: () => ({ hasRole: () => false }) }))
vi.mock('@/shared/stores/toastStore', () => ({ toast: { success: vi.fn(), error: vi.fn() } }))
vi.mock('@/modules/social-channels/hooks/useSocialChannels', () => ({
  useSocialChannelAll: () => ({
    data: [
      { id: 'z', pageName: 'Zeta', platform: 1, channelType: 1 },
      { id: 'v1', pageName: 'VNi Hà Nội', platform: 1, channelType: 1 },
      { id: 'th', pageName: 'VNi Threads', platform: 5, channelType: 1 },
      { id: 'a', pageName: 'Alpha', platform: 1, channelType: 1 },
    ],
  }),
}))

const mutation = () => ({ mutate: vi.fn(), mutateAsync: vi.fn(), isPending: false })
vi.mock('../hooks/usePageMessages', () => ({
  usePageConversation: () => ({ data: null, isLoading: false }),
  usePageMessageList: () => ({
    data: { items: [], total: 0, index: 1, size: 20 }, isLoading: false, isError: false, refetch: vi.fn(),
  }),
  usePageMessageSummary: () => ({ data: {} }),
  usePageMessageWorkflow: () => mutation(),
  useSendPageMessage: () => mutation(),
  useSubscribePageMessages: () => mutation(),
  useSyncPageMessages: () => mutation(),
}))

describe('PageMessagesInboxPage Facebook Page filter order', () => {
  it('lists only Facebook pages, VNi first then A→Z', () => {
    render(<PageMessagesInboxPage />)

    const options = within(screen.getByLabelText('Facebook Page')).getAllByRole('option').map((o) => o.textContent)
    expect(options).toEqual(['Tất cả Page', 'VNi Hà Nội', 'Alpha', 'Zeta'])
  })
})
