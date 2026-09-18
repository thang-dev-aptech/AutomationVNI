import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import MediaFolderFormModal from '../components/MediaFolderFormModal'
import { CHANNELS, PAGE_A } from './mediaFolderExplorerFixtures'

vi.mock('../hooks/useMediaFolders', async (importOriginal) => {
  const actual = await importOriginal()
  return {
    ...actual,
    useWritableMediaFolderPages: vi.fn(),
  }
})

const PAGE_WITHOUT_ROOT_1 = { ...CHANNELS[0], id: 'page-no-root-1', pageName: 'Campaign A' }
const PAGE_WITHOUT_ROOT_2 = { ...CHANNELS[1], id: 'page-no-root-2', pageName: 'Campaign B' }

function renderModal(props = {}) {
  const onClose = props.onClose ?? vi.fn()
  const onSubmit = props.onSubmit ?? vi.fn()
  const user = userEvent.setup()
  const view = render(
    <MediaFolderFormModal
      open
      onClose={onClose}
      onSubmit={onSubmit}
      {...props}
    />
  )
  return { user, onClose, onSubmit, ...view }
}

describe('MediaFolderFormModal three modes', () => {
  const { useWritableMediaFolderPages } = require('../hooks/useMediaFolders')

  beforeEach(() => {
    vi.clearAllMocks()
    useWritableMediaFolderPages.mockReturnValue({
      data: [PAGE_WITHOUT_ROOT_1, PAGE_WITHOUT_ROOT_2],
      isLoading: false,
    })
  })

  describe('page-roots mode', () => {
    it('displays checklist of Pages without roots', () => {
      renderModal({ mode: 'page-roots' })

      expect(screen.getByText(/Chọn Page để tạo/)).toBeInTheDocument()
      expect(screen.getByLabelText(PAGE_WITHOUT_ROOT_1.pageName)).toBeInTheDocument()
      expect(screen.getByLabelText(PAGE_WITHOUT_ROOT_2.pageName)).toBeInTheDocument()
    })

    it('submits items array with socialChannelId and page name', async () => {
      const { user, onSubmit } = renderModal({ mode: 'page-roots' })

      await user.click(screen.getByLabelText(PAGE_WITHOUT_ROOT_1.pageName))
      await user.click(screen.getByRole('button', { name: /Lưu/ }))

      expect(onSubmit).toHaveBeenCalledWith(
        expect.objectContaining({
          items: expect.arrayContaining([
            expect.objectContaining({
              socialChannelId: PAGE_WITHOUT_ROOT_1.id,
              name: PAGE_WITHOUT_ROOT_1.pageName,
            }),
          ]),
        })
      )
    })

    it('disables submit when no Pages selected', () => {
      renderModal({ mode: 'page-roots' })

      const submitButton = screen.getByRole('button', { name: /Lưu/ })
      expect(submitButton).toBeDisabled()
    })

    it('supports select/deselect all', async () => {
      const { user } = renderModal({ mode: 'page-roots' })

      await user.click(screen.getByRole('button', { name: /Chọn tất cả/ }))

      expect(screen.getByLabelText(PAGE_WITHOUT_ROOT_1.pageName)).toBeChecked()
      expect(screen.getByLabelText(PAGE_WITHOUT_ROOT_2.pageName)).toBeChecked()
    })
  })

  describe('create-child mode', () => {
    it('shows only name field', () => {
      renderModal({
        mode: 'create-child',
        defaultParentId: 'parent-123',
        defaultSocialChannelId: PAGE_A,
      })

      expect(screen.getByLabelText(/Tên thư mục/)).toBeInTheDocument()
      expect(screen.queryByText(/Chọn Page/)).not.toBeInTheDocument()
    })

    it('submits with fixed parent and page', async () => {
      const { user, onSubmit } = renderModal({
        mode: 'create-child',
        defaultParentId: 'parent-123',
        defaultSocialChannelId: PAGE_A,
      })

      await user.type(screen.getByLabelText(/Tên thư mục/), 'New Folder')
      await user.click(screen.getByRole('button', { name: /Lưu/ }))

      expect(onSubmit).toHaveBeenCalledWith({
        name: 'New Folder',
        parentFolderId: 'parent-123',
        socialChannelId: PAGE_A,
      })
    })

    it('disables submit when name is empty', () => {
      renderModal({
        mode: 'create-child',
        defaultParentId: 'parent-123',
      })

      const submitButton = screen.getByRole('button', { name: /Lưu/ })
      expect(submitButton).toBeDisabled()
    })
  })

  describe('rename mode', () => {
    it('shows only name field', () => {
      const folder = { id: 'folder-1', name: 'Original Name', socialChannelId: PAGE_A }
      renderModal({ mode: 'rename', editing: folder })

      expect(screen.getByDisplayValue('Original Name')).toBeInTheDocument()
      expect(screen.queryByText(/Chọn Page/)).not.toBeInTheDocument()
    })

    it('submits with updated name', async () => {
      const folder = {
        id: 'folder-1',
        name: 'Original Name',
        socialChannelId: PAGE_A,
        parentFolderId: 'parent-123',
      }
      const { user, onSubmit } = renderModal({ mode: 'rename', editing: folder })

      const input = screen.getByDisplayValue('Original Name')
      await user.clear(input)
      await user.type(input, 'Renamed Folder')
      await user.click(screen.getByRole('button', { name: /Lưu/ }))

      expect(onSubmit).toHaveBeenCalledWith({
        name: 'Renamed Folder',
        parentFolderId: 'parent-123',
        socialChannelId: PAGE_A,
      })
    })

    it('disables submit when name is empty', async () => {
      const folder = { id: 'folder-1', name: 'Original' }
      renderModal({ mode: 'rename', editing: folder })

      const input = screen.getByDisplayValue('Original')
      await userEvent.clear(input)

      const submitButton = screen.getByRole('button', { name: /Lưu/ })
      expect(submitButton).toBeDisabled()
    })
  })

  describe('modal closing', () => {
    it('calls onClose when cancel clicked', async () => {
      const { user, onClose } = renderModal({ mode: 'rename' })

      await user.click(screen.getByRole('button', { name: /Hủy/ }))

      expect(onClose).toHaveBeenCalled()
    })
  })
})
