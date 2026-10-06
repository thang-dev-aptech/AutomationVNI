import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import ChannelGroupImportModal from '../components/ChannelGroupImportModal'

const {
  previewMutateAsync,
  commitMutateAsync,
  toastSuccess,
  toastError,
} = vi.hoisted(() => ({
  previewMutateAsync: vi.fn(),
  commitMutateAsync: vi.fn(),
  toastSuccess: vi.fn(),
  toastError: vi.fn(),
}))

vi.mock('@/shared/stores/toastStore', () => ({
  toast: {
    success: (...args) => toastSuccess(...args),
    error: (...args) => toastError(...args),
  },
}))

vi.mock('@/shared/utils/apiHelpers', async (importOriginal) => {
  const actual = await importOriginal()
  return {
    ...actual,
    getErrorMessage: (err) => err?.message || 'Lỗi',
  }
})

vi.mock('../hooks/useChannelGroups', () => ({
  usePreviewChannelGroupImport: () => ({
    mutateAsync: previewMutateAsync,
    isPending: false,
  }),
  useCommitChannelGroupImport: () => ({
    mutateAsync: commitMutateAsync,
    isPending: false,
  }),
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

import { channelGroupApi } from '../services/channelGroupApi'

const PREVIEW = {
  mode: 'merge',
  validRowCount: 2,
  groups: [
    {
      name: 'Miền Bắc',
      isNew: false,
      existingGroupId: 'g1',
      channelsAdded: 1,
      channelsRemoved: 0,
    },
    {
      name: 'Mới',
      isNew: true,
      channelsAdded: 1,
      channelsRemoved: 0,
    },
  ],
  errors: [
    { line: 3, reason: 'ID Page không tìm thấy' },
  ],
}

describe('ChannelGroupImportModal (channel-group-import-ui-test)', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    previewMutateAsync.mockResolvedValue(PREVIEW)
    commitMutateAsync.mockResolvedValue({ groupsCreated: 1, groupsUpdated: 1 })
    const bom = new Uint8Array([0xef, 0xbb, 0xbf])
    const body = new TextEncoder().encode('Tên nhóm,ID Page,Mô tả\r\n')
    const bytes = new Uint8Array(bom.length + body.length)
    bytes.set(bom, 0)
    bytes.set(body, bom.length)
    vi.spyOn(channelGroupApi, 'downloadTemplate').mockResolvedValue({
      data: new Blob([bytes], { type: 'text/csv;charset=utf-8' }),
    })
    vi.spyOn(URL, 'createObjectURL').mockReturnValue('blob:mock')
    vi.spyOn(URL, 'revokeObjectURL').mockImplementation(() => {})
  })

  it('Tải file mẫu downloads UTF-8 BOM CSV with required headers', async () => {
    const user = userEvent.setup()
    const clickSpy = vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => {})
    render(<ChannelGroupImportModal open onClose={vi.fn()} />)

    await user.click(screen.getByRole('button', { name: /Tải file mẫu/ }))

    await waitFor(() => expect(channelGroupApi.downloadTemplate).toHaveBeenCalled())
    const { data: blob } = await channelGroupApi.downloadTemplate.mock.results[0].value
    const buf = new Uint8Array(await blob.arrayBuffer())
    expect(buf[0]).toBe(0xef)
    expect(buf[1]).toBe(0xbb)
    expect(buf[2]).toBe(0xbf)
    const text = new TextDecoder('utf-8').decode(buf)
    expect(text).toMatch(/Tên nhóm/i)
    expect(text).toMatch(/ID Page/i)
    expect(text).toMatch(/Mô tả/i)
    expect(clickSpy).toHaveBeenCalled()
    clickSpy.mockRestore()
  })

  it('selecting file + mode calls preview and shows groups/errors', async () => {
    const user = userEvent.setup()
    render(<ChannelGroupImportModal open onClose={vi.fn()} />)

    const file = new File(['Tên nhóm,ID Page\nA,1\n'], 'groups.csv', { type: 'text/csv' })
    await user.upload(screen.getByTestId('channel-group-import-file'), file)

    await waitFor(() => {
      expect(previewMutateAsync).toHaveBeenCalledWith({
        file,
        mode: 'merge',
      })
    })

    expect(await screen.findByTestId('channel-group-import-preview')).toBeInTheDocument()
    expect(screen.getByText('Miền Bắc')).toBeInTheDocument()
    expect(screen.getByText('Tạo mới')).toBeInTheDocument()
    expect(screen.getByText('Cập nhật')).toBeInTheDocument()
    expect(screen.getByTestId('channel-group-import-errors')).toHaveTextContent(
      'Dòng 3: ID Page không tìm thấy',
    )
  })

  it('changing mode re-runs preview', async () => {
    const user = userEvent.setup()
    render(<ChannelGroupImportModal open onClose={vi.fn()} />)

    const file = new File(['x'], 'g.csv', { type: 'text/csv' })
    await user.upload(screen.getByTestId('channel-group-import-file'), file)
    await waitFor(() => expect(previewMutateAsync).toHaveBeenCalledTimes(1))

    await user.click(screen.getByRole('radio', { name: /Thay thế/ }))
    await waitFor(() => {
      expect(previewMutateAsync).toHaveBeenLastCalledWith({ file, mode: 'replace' })
    })
  })

  it('Xác nhận disabled without valid rows; enabled then commits and closes', async () => {
    const user = userEvent.setup()
    const onClose = vi.fn()
    previewMutateAsync.mockResolvedValueOnce({
      validRowCount: 0,
      groups: [],
      errors: [{ line: 2, reason: 'Tên nhóm trống' }],
    })

    render(<ChannelGroupImportModal open onClose={onClose} />)
    const file = new File(['bad'], 'bad.csv', { type: 'text/csv' })
    await user.upload(screen.getByTestId('channel-group-import-file'), file)
    await screen.findByTestId('channel-group-import-errors')

    expect(screen.getByRole('button', { name: /^Xác nhận$/ })).toBeDisabled()

    previewMutateAsync.mockResolvedValue(PREVIEW)
    const file2 = new File(['ok'], 'ok.csv', { type: 'text/csv' })
    await user.upload(screen.getByTestId('channel-group-import-file'), file2)
    await waitFor(() => {
      expect(screen.getByRole('button', { name: /^Xác nhận$/ })).not.toBeDisabled()
    })

    await user.click(screen.getByRole('button', { name: /^Xác nhận$/ }))
    await waitFor(() => {
      expect(commitMutateAsync).toHaveBeenCalledWith({ file: file2, mode: 'merge' })
      expect(onClose).toHaveBeenCalled()
    })
  })

  it('shows backend 400 message on preview failure', async () => {
    const user = userEvent.setup()
    previewMutateAsync.mockRejectedValueOnce(new Error('Dòng tiêu đề bắt buộc có cột "Tên nhóm" và "ID Page".'))
    render(<ChannelGroupImportModal open onClose={vi.fn()} />)

    const file = new File(['x'], 'bad.csv', { type: 'text/csv' })
    await user.upload(screen.getByTestId('channel-group-import-file'), file)

    expect(await screen.findByRole('alert')).toHaveTextContent(
      'Dòng tiêu đề bắt buộc có cột "Tên nhóm" và "ID Page".',
    )
  })

  it('shows Excel UTF-8 hint in modal', () => {
    render(<ChannelGroupImportModal open onClose={vi.fn()} />)
    expect(screen.getByText(/Lưu thành → CSV UTF-8/i)).toBeInTheDocument()
  })
})
