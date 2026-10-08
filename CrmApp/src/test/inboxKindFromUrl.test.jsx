import React from 'react'
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, waitFor } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { InboxFeature, kindFromSearchParams } from '../features/inbox/InboxFeature'
import { inboxApi } from '../features/inbox/api/inboxApi'
import { useAuthStore } from '../auth/authStore'

describe('N-a: CrmApp inbox reads ?kind= from URL', () => {
  beforeEach(() => {
    vi.restoreAllMocks()
    useAuthStore.getState().setAuth('mock-token', {
      email: 'admin@vni.local',
      userName: 'AdminUser',
      roles: ['Admin'],
    })
    vi.spyOn(inboxApi, 'listChannels').mockResolvedValue([])
    vi.spyOn(inboxApi, 'listUsers').mockResolvedValue([])
    vi.spyOn(inboxApi, 'listTags').mockResolvedValue([])
  })

  it('kindFromSearchParams maps message→1, comment→2, else null', () => {
    expect(kindFromSearchParams(new URLSearchParams('kind=message'))).toBe(1)
    expect(kindFromSearchParams(new URLSearchParams('kind=comment'))).toBe(2)
    expect(kindFromSearchParams(new URLSearchParams('kind=other'))).toBeNull()
    expect(kindFromSearchParams(new URLSearchParams(''))).toBeNull()
    expect(kindFromSearchParams(null)).toBeNull()
  })

  it('opens /inbox?kind=message and calls filter with kind=1', async () => {
    const filterSpy = vi.spyOn(inboxApi, 'filter').mockResolvedValue({ items: [], totalCount: 0 })

    render(
      <MemoryRouter initialEntries={['/inbox?kind=message']}>
        <InboxFeature />
      </MemoryRouter>,
    )

    await waitFor(() => {
      expect(filterSpy).toHaveBeenCalled()
    })
    expect(filterSpy).toHaveBeenCalledWith(expect.objectContaining({ kind: 1 }))
  })

  it('opens /inbox?kind=comment and calls filter with kind=2', async () => {
    const filterSpy = vi.spyOn(inboxApi, 'filter').mockResolvedValue({ items: [], totalCount: 0 })

    render(
      <MemoryRouter initialEntries={['/inbox?kind=comment']}>
        <InboxFeature />
      </MemoryRouter>,
    )

    await waitFor(() => {
      expect(filterSpy).toHaveBeenCalled()
    })
    expect(filterSpy).toHaveBeenCalledWith(expect.objectContaining({ kind: 2 }))
  })

  it('opens /inbox without kind (or invalid) and calls filter with kind=null', async () => {
    const filterSpy = vi.spyOn(inboxApi, 'filter').mockResolvedValue({ items: [], totalCount: 0 })

    render(
      <MemoryRouter initialEntries={['/inbox?kind=weird']}>
        <InboxFeature />
      </MemoryRouter>,
    )

    await waitFor(() => {
      expect(filterSpy).toHaveBeenCalled()
    })
    expect(filterSpy).toHaveBeenCalledWith(expect.objectContaining({ kind: null }))
  })
})
