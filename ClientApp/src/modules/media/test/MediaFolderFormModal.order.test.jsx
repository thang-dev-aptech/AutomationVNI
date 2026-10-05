import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'
import MediaFolderFormModal from '../components/MediaFolderFormModal'

vi.mock('../hooks/useMediaFolders', async (importOriginal) => ({
  ...(await importOriginal()),
  useWritableMediaFolderPages: () => ({
    data: [
      { id: 'z', pageName: 'Zeta' },
      { id: 'v1', pageName: 'VNi Hà Nội' },
      { id: 'a', pageName: 'Alpha' },
      { id: 'v2', pageName: 'vni sài gòn' },
    ],
    isLoading: false,
  }),
}))

const itemNames = () => [...document.querySelectorAll('.media-folder-page-multiselect-item')].map((el) => el.textContent)

describe('MediaFolderFormModal page checklist order', () => {
  it('lists Pages without a root VNi first then A→Z', () => {
    render(<MediaFolderFormModal open mode="page-roots" onClose={vi.fn()} onSubmit={vi.fn()} />)

    expect(itemNames()).toEqual(['VNi Hà Nội', 'vni sài gòn', 'Alpha', 'Zeta'])
  })

  it('still submits the chosen pages named after themselves', async () => {
    const onSubmit = vi.fn()
    const user = userEvent.setup()
    render(<MediaFolderFormModal open mode="page-roots" onClose={vi.fn()} onSubmit={onSubmit} />)
    await user.click(screen.getByLabelText('Alpha'))
    await user.click(screen.getByLabelText('VNi Hà Nội'))
    await user.click(screen.getByRole('button', { name: /Lưu/ }))

    const { items } = onSubmit.mock.calls[0][0]
    expect(items).toHaveLength(2)
    expect(items).toEqual(expect.arrayContaining([
      expect.objectContaining({ socialChannelId: 'a', name: 'Alpha' }),
      expect.objectContaining({ socialChannelId: 'v1', name: 'VNi Hà Nội' }),
    ]))
  })
})
