import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { FEATURES } from '@/shared/config/features'
import BulkCreatePage from '../pages/BulkCreatePage'

const h = vi.hoisted(() => ({
  state: { templates: [], contexts: [] },
  createFn: vi.fn(),
  importFn: vi.fn(),
  toastError: vi.fn(),
}))

vi.mock('@/shared/stores/toastStore', () => ({
  toast: { success: vi.fn(), error: h.toastError, warning: vi.fn() },
}))
vi.mock('@/modules/social-channels/hooks/useSocialChannels', () => ({
  useSocialChannelAll: () => ({
    data: [
      { id: 'page-ready', pageName: 'Page Có Context', platform: 1 },
      { id: 'page-missing', pageName: 'Page Thiếu Context', platform: 1 },
    ],
    isLoading: false,
    isError: false,
    refetch: vi.fn(),
  }),
}))
vi.mock('@/modules/social-channels/hooks/useChannelGroups', () => ({
  useChannelGroupAll: () => ({ data: [], isLoading: false }),
}))
vi.mock('@/modules/prompt-templates/hooks/usePromptTemplates', () => ({
  usePromptTemplateList: () => ({ data: { items: h.state.templates }, isLoading: false }),
}))
vi.mock('@/modules/categories/hooks/useCategories', () => ({
  useCategoryList: () => ({ data: { items: [] }, isLoading: false }),
}))
vi.mock('@/modules/page-contexts/hooks/usePageContexts', () => ({
  usePageContextList: () => ({ data: { items: h.state.contexts }, isLoading: false }),
}))
vi.mock('../hooks/useBulk', () => ({
  useBulkCreate: () => ({ mutateAsync: h.createFn, isPending: false }),
  useBulkImport: () => ({ mutateAsync: h.importFn, isPending: false }),
}))

const DEFAULT_TPL = { id: 'tpl-default', name: 'Mặc định chung', isDefault: true }
const OTHER_TPL = { id: 'tpl-other', name: 'Danh mục khác', isDefault: false }
const READY_CONTEXT = { socialChannelId: 'page-ready', defaultTextTemplateId: 'tpl-own' }
const OPTION_1 = 'Dùng mặc định PageContext từng page'
const DEFAULTS = { ...FEATURES }

function renderPage() {
  const user = userEvent.setup()
  render(<MemoryRouter><BulkCreatePage /></MemoryRouter>)
  return user
}

const categorySelect = () => screen.getByLabelText(/Danh mục \(tuỳ chọn/)
const createButton = () => screen.getByRole('button', { name: /^Tạo \d+ bài/ })

async function pickPage(user, pageName) {
  const root = screen.getByText('Kênh đăng (fan-out)').closest('.channel-multi-select')
  await user.click(within(root).getByRole('button', { name: 'Chọn page' }))
  await user.click(within(root).getByLabelText(pageName))
}

async function typeIdea(user, text = 'Ý tưởng thử') {
  await user.type(screen.getAllByPlaceholderText('Ý tưởng bài viết...')[0], text)
}

async function uploadCsv(user, pageId) {
  const content = `idea,page_id\nBài từ CSV,${pageId}`
  const file = new File([content], 'ideas.csv', { type: 'text/csv' })
  file.text = () => Promise.resolve(content)
  await user.upload(document.querySelector('input[type="file"]'), file)
}

describe('BulkCreatePage category select (R-030)', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    h.state.templates = [OTHER_TPL, DEFAULT_TPL]
    h.state.contexts = [READY_CONTEXT]
    h.createFn.mockResolvedValue({ created: 1, batchId: 'batch-1' })
    h.importFn.mockResolvedValue({ created: 1, batchId: 'batch-2' })
  })

  afterEach(() => {
    Object.assign(FEATURES, DEFAULTS)
  })

  it('(a) offers exactly two options and no other category', () => {
    renderPage()

    const options = within(categorySelect()).getAllByRole('option').map((o) => o.textContent)
    expect(options).toEqual([OPTION_1, 'Dùng danh mục mặc định: Mặc định chung ⭐'])
    expect(screen.queryByRole('option', { name: /Danh mục khác/ })).not.toBeInTheDocument()
    expect(categorySelect()).toHaveValue('pagecontext')
  })

  it('(b) option 1 sends promptTemplateId null, option 2 sends the default category id (create)', async () => {
    const user = renderPage()
    await pickPage(user, 'Page Có Context')
    await typeIdea(user)

    await user.click(createButton())
    expect(h.createFn).toHaveBeenLastCalledWith(expect.objectContaining({
      promptTemplateId: null, channelIds: ['page-ready'],
    }))

    await user.selectOptions(categorySelect(), 'default')
    await user.click(createButton())
    expect(h.createFn).toHaveBeenLastCalledWith(expect.objectContaining({ promptTemplateId: 'tpl-default' }))
  })

  it('(b) the same two payloads for CSV import', async () => {
    const user = renderPage()

    await uploadCsv(user, 'page-ready')
    await waitFor(() => expect(h.importFn).toHaveBeenCalledTimes(1))
    expect(h.importFn.mock.calls[0][0]).toMatchObject({ promptTemplateId: null })

    await user.selectOptions(categorySelect(), 'default')
    await uploadCsv(user, 'page-ready')
    await waitFor(() => expect(h.importFn).toHaveBeenCalledTimes(2))
    expect(h.importFn.mock.calls[1][0]).toMatchObject({ promptTemplateId: 'tpl-default' })
  })

  it('(c) without a default category option 2 is disabled and explained', () => {
    h.state.templates = [OTHER_TPL]
    renderPage()

    const options = within(categorySelect()).getAllByRole('option')
    expect(options).toHaveLength(2)
    expect(options[1]).toBeDisabled()
    expect(screen.getByText(/Chưa có danh mục mặc định/)).toBeInTheDocument()
  })

  it('(d) a page without PageContext blocks Create under option 1 and unblocks with option 2', async () => {
    const user = renderPage()
    await pickPage(user, 'Page Thiếu Context')
    await typeIdea(user)

    expect(createButton()).toBeDisabled()
    expect(screen.getByText(/1 page chưa có PageContext — chọn "Dùng danh mục mặc định: Mặc định chung"/)).toBeInTheDocument()

    await user.selectOptions(categorySelect(), 'default')
    expect(createButton()).toBeEnabled()
    expect(screen.getByText(/sẽ dùng danh mục mặc định "Mặc định chung"/)).toBeInTheDocument()
    await user.click(createButton())
    expect(h.createFn).toHaveBeenCalledWith(expect.objectContaining({
      promptTemplateId: 'tpl-default', channelIds: ['page-missing'],
    }))
  })

  it('(d) CSV import for a page without PageContext is blocked under option 1 with the new hint, allowed under option 2', async () => {
    const user = renderPage()

    await uploadCsv(user, 'page-missing')
    await waitFor(() => expect(h.toastError).toHaveBeenCalled())
    expect(h.toastError.mock.calls[0][0]).toMatch(/1 page chưa có PageContext — chọn "Dùng danh mục mặc định: Mặc định chung"/)
    expect(h.importFn).not.toHaveBeenCalled()

    await user.selectOptions(categorySelect(), 'default')
    await uploadCsv(user, 'page-missing')
    await waitFor(() => expect(h.importFn).toHaveBeenCalledTimes(1))
    expect(h.importFn.mock.calls[0][0]).toMatchObject({ promptTemplateId: 'tpl-default' })
  })

  it('(d) with no default category the hint says so and Create stays blocked', async () => {
    h.state.templates = [OTHER_TPL]
    const user = renderPage()
    await pickPage(user, 'Page Thiếu Context')
    await typeIdea(user)

    expect(createButton()).toBeDisabled()
    expect(screen.getByText(/chưa có danh mục mặc định \(⭐\) — setup Page Context/)).toBeInTheDocument()
  })

  it('(e) Full AI stays the default method and the payload keeps the Full AI flow', async () => {
    const user = renderPage()
    expect(screen.getByRole('button', { name: /Sinh toàn bộ bằng AI/ })).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /AI sinh text, ghép vào ảnh mẫu/ })).not.toBeInTheDocument()

    await pickPage(user, 'Page Có Context')
    await typeIdea(user)
    await user.click(createButton())

    expect(h.createFn).toHaveBeenCalledWith(expect.objectContaining({ generationFlow: 1, categoryId: null }))
    expect(FEATURES).toEqual(DEFAULTS)
  })
})
