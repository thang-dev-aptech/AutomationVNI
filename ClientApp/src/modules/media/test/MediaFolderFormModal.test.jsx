import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'
import MediaFolderFormModal from '../components/MediaFolderFormModal'
import { CHANNELS, PAGE_A, PAGE_B } from './mediaFolderExplorerFixtures'

vi.mock('@/modules/social-channels/hooks/useSocialChannels', () => ({
  useSocialChannelAll: () => ({ data: CHANNELS }),
}))

vi.mock('../components/MediaFolderPickerTree', () => ({
  default: (props) => <div data-testid="picker-mock" data-social-channel-id={props.socialChannelId ?? ''} />,
}))

function renderModal(props = {}) {
  const user = userEvent.setup()
  const onSubmit = props.onSubmit ?? vi.fn()
  const onClose = props.onClose ?? vi.fn()
  const view = render(
    <MediaFolderFormModal open onClose={onClose} onSubmit={onSubmit} {...props} />,
  )
  return { user, onSubmit, onClose, ...view }
}

describe('MEDIA-06 MediaFolderFormModal (create: multi-Page select)', () => {
  it('create mode with 0 Pages selected: submits a single-folder payload with socialChannelId null', async () => {
    const { user, onSubmit } = renderModal()

    await user.type(screen.getByLabelText('Tên thư mục'), 'Logo')
    await user.click(screen.getByRole('button', { name: 'Lưu' }))

    expect(onSubmit).toHaveBeenCalledWith({ name: 'Logo', parentFolderId: null, socialChannelId: null })
  })

  it('create mode with exactly 1 Page checked: shows the parent picker scoped to it, submits single-folder shape', async () => {
    const { user, onSubmit } = renderModal()

    await user.type(screen.getByLabelText('Tên thư mục'), 'Logo')
    await user.click(screen.getByLabelText(CHANNELS[0].pageName))

    expect(screen.getByTestId('picker-mock')).toHaveAttribute('data-social-channel-id', PAGE_A)

    await user.click(screen.getByRole('button', { name: 'Lưu' }))
    expect(onSubmit).toHaveBeenCalledWith({ name: 'Logo', parentFolderId: null, socialChannelId: PAGE_A })
  })

  it('create mode with 2+ Pages checked: hides the parent picker and submits socialChannelIds instead', async () => {
    const { user, onSubmit } = renderModal()

    await user.type(screen.getByLabelText('Tên thư mục'), 'Logo')
    await user.click(screen.getByLabelText(CHANNELS[0].pageName))
    await user.click(screen.getByLabelText(CHANNELS[1].pageName))

    expect(screen.queryByTestId('picker-mock')).not.toBeInTheDocument()
    expect(screen.getByText(/không chọn được thư mục cha khi chọn nhiều Page/)).toBeInTheDocument()

    await user.click(screen.getByRole('button', { name: 'Lưu' }))
    expect(onSubmit).toHaveBeenCalledWith({ name: 'Logo', socialChannelIds: [PAGE_A, PAGE_B] })
  })

  it('unchecking back down to 1 Page brings the parent picker back', async () => {
    const { user } = renderModal()

    await user.click(screen.getByLabelText(CHANNELS[0].pageName))
    await user.click(screen.getByLabelText(CHANNELS[1].pageName))
    expect(screen.queryByTestId('picker-mock')).not.toBeInTheDocument()

    await user.click(screen.getByLabelText(CHANNELS[1].pageName))
    expect(screen.getByTestId('picker-mock')).toBeInTheDocument()
  })

  it('"Chọn tất cả" selects every Page and toggles to "Bỏ chọn tất cả"', async () => {
    const { user } = renderModal()

    await user.click(screen.getByRole('button', { name: 'Chọn tất cả' }))
    expect(screen.getByLabelText(CHANNELS[0].pageName)).toBeChecked()
    expect(screen.getByLabelText(CHANNELS[1].pageName)).toBeChecked()

    await user.click(screen.getByRole('button', { name: 'Bỏ chọn tất cả' }))
    expect(screen.getByLabelText(CHANNELS[0].pageName)).not.toBeChecked()
  })

  it('edit mode keeps the original single-Page select behavior, unaffected by multi-select', async () => {
    const editing = { id: 'folder-1', name: 'Old Name', parentFolderId: null, socialChannelId: PAGE_A }
    const { user, onSubmit } = renderModal({ editing })

    expect(screen.getByDisplayValue('Old Name')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Chọn tất cả' })).not.toBeInTheDocument()
    expect(screen.getByTestId('picker-mock')).toHaveAttribute('data-social-channel-id', PAGE_A)

    await user.click(screen.getByRole('button', { name: 'Lưu' }))
    expect(onSubmit).toHaveBeenCalledWith({ name: 'Old Name', parentFolderId: null, socialChannelId: PAGE_A })
  })

  it('resets Page selection and name every time the modal is reopened', async () => {
    const { user, rerender } = renderModal()

    await user.type(screen.getByLabelText('Tên thư mục'), 'Sẽ bị xóa')
    await user.click(screen.getByLabelText(CHANNELS[0].pageName))

    rerender(<MediaFolderFormModal open={false} onClose={vi.fn()} onSubmit={vi.fn()} />)
    rerender(<MediaFolderFormModal open onClose={vi.fn()} onSubmit={vi.fn()} />)

    expect(screen.getByLabelText('Tên thư mục')).toHaveValue('')
    expect(screen.getByLabelText(CHANNELS[0].pageName)).not.toBeChecked()
  })
})
