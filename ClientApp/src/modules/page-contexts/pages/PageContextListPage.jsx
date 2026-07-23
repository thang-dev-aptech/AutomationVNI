import { useMemo, useState } from 'react'
import PageHeader from '@/shared/components/PageHeader'
import LoadingState from '@/shared/components/LoadingState'
import ErrorState from '@/shared/components/ErrorState'
import EmptyState from '@/shared/components/EmptyState'
import { formatDateTime, getErrorMessage } from '@/shared/utils/apiHelpers'
import { useSocialChannelAll } from '@/modules/social-channels/hooks/useSocialChannels'
import { usePromptTemplateList } from '@/modules/prompt-templates/hooks/usePromptTemplates'
import { toast } from '@/shared/stores/toastStore'
import Modal from '@/shared/components/Modal'
import PageContextFormModal from '../components/PageContextFormModal'
import {
  useCreatePageContext,
  useDeletePageContext,
  useImportPageContexts,
  usePageContextList,
  useUpdatePageContext,
} from '../hooks/usePageContexts'

const IMPORT_SAMPLE = `[
  {
    "channelName": "VNI Education",
    "brandName": "VNI Education",
    "toneOfVoice": "Chuyên nghiệp, tin cậy, truyền cảm hứng học tập",
    "hotline": "1900 xxxx",
    "website": "vnieducation.edu.vn",
    "brandColors": "#1565C0, #F59E0B, #22C55E",
    "ctaText": "Inbox ngay để được tư vấn khóa học 📩"
  }
]`

export default function PageContextListPage() {
  const [keyword, setKeyword] = useState('')
  const [modalOpen, setModalOpen] = useState(false)
  const [editingItem, setEditingItem] = useState(null)
  const [copyingItem, setCopyingItem] = useState(null)
  const [formError, setFormError] = useState('')
  const [importOpen, setImportOpen] = useState(false)
  const [importText, setImportText] = useState('')

  const params = useMemo(() => ({ keyword, index: 1, size: 50 }), [keyword])
  const { data, isLoading, isError, error, refetch } = usePageContextList(params)
  const { data: channels = [] } = useSocialChannelAll()
  const createMutation = useCreatePageContext()
  const updateMutation = useUpdatePageContext()
  const deleteMutation = useDeletePageContext()
  const importMutation = useImportPageContexts()

  const { data: tplData } = usePromptTemplateList({ isActive: true, index: 1, size: 100 })
  const templateMap = useMemo(
    () => Object.fromEntries((tplData?.items ?? []).map((t) => [t.id, t.name])),
    [tplData],
  )

  const channelMap = useMemo(
    () => Object.fromEntries(channels.map((c) => [c.id, c.pageName])),
    [channels],
  )

  const items = data?.items ?? []

  // Page "sẵn sàng" = có danh mục mặc định hoặc prompt inline → tạo bài không cần chọn danh mục.
  const isReady = (item) =>
    Boolean(item.defaultTextTemplateId || item.defaultImageTemplateId || item.promptTemplateText)

  const getTemplateName = (item) => {
    const tplId = item.defaultTextTemplateId || item.defaultImageTemplateId
    if (tplId) return templateMap[tplId] || 'Danh mục đã xoá?'
    if (item.promptTemplateText) return 'Prompt inline'
    return null
  }

  const missingChannels = useMemo(() => {
    const withContext = new Set(items.map((i) => i.socialChannelId))
    return channels.filter((c) => c.isActive !== false && !withContext.has(c.id))
  }, [channels, items])

  const openCreate = () => {
    setEditingItem(null)
    setCopyingItem(null)
    setFormError('')
    setModalOpen(true)
  }

  const openEdit = (item) => {
    setEditingItem(item)
    setCopyingItem(null)
    setFormError('')
    setModalOpen(true)
  }

  const openCopy = (item) => {
    setEditingItem(null)
    setCopyingItem({
      ...item,
      id: undefined,
      socialChannelId: '',
    })
    setFormError('')
    setModalOpen(true)
  }

  const handleSubmit = async (payload) => {
    try {
      setFormError('')
      if (editingItem) {
        const { socialChannelId: _socialChannelId, ...updatePayload } = payload
        await updateMutation.mutateAsync({ id: editingItem.id, payload: updatePayload })
      } else {
        await createMutation.mutateAsync(payload)
      }
      setModalOpen(false)
      toast.success(
        editingItem
          ? 'Đã cập nhật Page Context'
          : copyingItem
            ? 'Đã sao chép Page Context'
            : 'Đã thêm Page Context',
      )
    } catch (submitError) {
      setFormError(getErrorMessage(submitError))
    }
  }

  const handleImport = async () => {
    let items
    try {
      items = JSON.parse(importText)
    } catch {
      toast.error('JSON không hợp lệ — kiểm tra lại định dạng')
      return
    }
    if (!Array.isArray(items) || items.length === 0) {
      toast.error('Cần một mảng JSON có ít nhất 1 phần tử')
      return
    }
    try {
      const result = await importMutation.mutateAsync({ items })
      const errPart = result?.errors?.length ? ` — lỗi: ${result.errors.slice(0, 3).join('; ')}` : ''
      toast.success(`Đã tạo ${result?.created ?? 0} context; bỏ qua ${result?.skipped ?? 0}${errPart}`)
      setImportOpen(false)
      setImportText('')
    } catch (importError) {
      toast.error(getErrorMessage(importError))
    }
  }

  const handleDelete = async (item) => {
    if (!window.confirm(`Xóa page context "${item.brandName}"?`)) return
    try {
      await deleteMutation.mutateAsync(item.id)
      toast.success('Đã xóa Page Context')
    } catch (deleteError) {
      toast.error(getErrorMessage(deleteError))
    }
  }

  return (
    <section>
      <PageHeader
        title="Page Context"
        description="Branding, danh mục mặc định và CTA cho từng page — page đã cấu hình sẽ tạo bài không cần chọn danh mục"
        actions={(
          <div style={{ display: 'flex', gap: 8 }}>
            <button type="button" className="btn btn-secondary" onClick={() => { setImportText(IMPORT_SAMPLE); setImportOpen(true) }}>
              Import JSON
            </button>
            <button type="button" className="btn btn-primary" onClick={openCreate}>
              Thêm context
            </button>
          </div>
        )}
      />

      {!keyword && missingChannels.length > 0 && (
        <div className="alert alert-warning" style={{ marginBottom: 16 }}>
          {missingChannels.length} page chưa có context:{' '}
          {missingChannels.slice(0, 5).map((c) => c.pageName).join(', ')}
          {missingChannels.length > 5 ? '…' : ''}
          {' — '}các page này khi tạo bài sẽ phải chọn danh mục thủ công.
        </div>
      )}

      <div className="card card-body" style={{ marginBottom: 16 }}>
        <div className="form-group" style={{ marginBottom: 0 }}>
          <label htmlFor="context-keyword">Tìm kiếm</label>
          <input
            id="context-keyword"
            value={keyword}
            onChange={(event) => setKeyword(event.target.value)}
            placeholder="Tên thương hiệu..."
          />
        </div>
      </div>

      <div className="card">
        {isLoading && <LoadingState />}
        {isError && <ErrorState message={getErrorMessage(error)} onRetry={refetch} />}
        {!isLoading && !isError && items.length === 0 && (
          <EmptyState message="Chưa có page context nào" />
        )}
        {!isLoading && !isError && items.length > 0 && (
          <div className="table-container">
            <table>
            <thead>
              <tr>
                <th>Thương hiệu</th>
                <th>Kênh</th>
                <th>Danh mục mặc định</th>
                <th>Trạng thái</th>
                <th>CTA</th>
                <th>Cập nhật</th>
                <th />
              </tr>
            </thead>
            <tbody>
              {items.map((item) => (
                <tr key={item.id}>
                  <td>{item.brandName}</td>
                  <td>{channelMap[item.socialChannelId] || item.socialChannelId}</td>
                  <td>{getTemplateName(item) || '—'}</td>
                  <td>
                    {isReady(item) ? (
                      <span className="badge badge-success">Sẵn sàng</span>
                    ) : (
                      <span className="badge badge-warning">Thiếu danh mục</span>
                    )}
                  </td>
                  <td>{item.ctaText || '—'}</td>
                  <td>{formatDateTime(item.updatedAt || item.createdAt)}</td>
                    <td>
                      <div style={{ display: 'flex', gap: 8, justifyContent: 'flex-end' }}>
                        <button
                          type="button"
                          className="btn btn-ghost"
                          onClick={() => openEdit(item)}
                        >
                          Cập nhật
                        </button>
                        <button
                          type="button"
                          className="btn btn-secondary"
                          onClick={() => openCopy(item)}
                        >
                          Sao chép
                        </button>
                        <button
                          type="button"
                          className="btn btn-danger"
                          onClick={() => handleDelete(item)}
                        >
                          Xóa
                        </button>
                      </div>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>

      <PageContextFormModal
        open={modalOpen}
        onClose={() => setModalOpen(false)}
        initialData={editingItem || copyingItem}
        mode={editingItem ? 'edit' : copyingItem ? 'copy' : 'create'}
        unavailableChannelIds={items.map((item) => item.socialChannelId)}
        onSubmit={handleSubmit}
        isSubmitting={createMutation.isPending || updateMutation.isPending}
        errorMessage={formError}
      />

      <Modal
        open={importOpen}
        title="Import Page Context (JSON)"
        onClose={() => setImportOpen(false)}
        footer={(
          <>
            <button type="button" className="btn btn-secondary" onClick={() => setImportOpen(false)}>
              Hủy
            </button>
            <button
              type="button"
              className="btn btn-primary"
              onClick={handleImport}
              disabled={importMutation.isPending}
            >
              {importMutation.isPending ? 'Đang import...' : 'Import'}
            </button>
          </>
        )}
      >
        <div className="form-group">
          <label htmlFor="pc-import">
            Mảng JSON — mỗi item trỏ kênh bằng <code>channelName</code> (tên page) hoặc <code>socialChannelId</code>.
            Kênh không tồn tại / đã có context sẽ bỏ qua.
          </label>
          <textarea
            id="pc-import"
            rows={14}
            value={importText}
            onChange={(event) => setImportText(event.target.value)}
            style={{ fontFamily: 'monospace', fontSize: '0.85rem' }}
          />
        </div>
      </Modal>
    </section>
  )
}
