import { useMemo, useState } from 'react'
import PageHeader from '@/shared/components/PageHeader'
import Modal from '@/shared/components/Modal'
import { usePermissions } from '@/shared/hooks/usePermissions'
import { getErrorMessage } from '@/shared/utils/apiHelpers'
import { confirmAction, CONFIRM_MESSAGES } from '@/shared/utils/confirmAction'
import { toast } from '@/shared/stores/toastStore'
import MediaGrid from '../components/MediaGrid'
import MediaUploadForm from '../components/MediaUploadForm'
import {
  useAnalyzeAllMediaAssets,
  useAnalyzeMediaAsset,
  useCreateMediaAsset,
  useDeleteMediaAsset,
  useMediaAssets,
  useUpdateMediaAsset,
  useUploadMediaAsset,
} from '../hooks/useMediaAssets'
import { MEDIA_SOURCE_OPTIONS } from '../constants/mediaConstants'
import './MediaPage.css'

export default function MediaPage() {
  const { canManageMedia } = usePermissions()
  const [keyword, setKeyword] = useState('')
  const [source, setSource] = useState('')
  const [uploadOpen, setUploadOpen] = useState(false)
  const [editingAsset, setEditingAsset] = useState(null)
  const [formError, setFormError] = useState('')

  const params = useMemo(
    () => ({
      keyword,
      index: 1,
      size: 48,
      source: source ? Number(source) : undefined,
    }),
    [keyword, source],
  )

  const { data, isLoading, isError, error, refetch } = useMediaAssets(params)
  const createMutation = useCreateMediaAsset()
  const uploadMutation = useUploadMediaAsset()
  const updateMutation = useUpdateMediaAsset()
  const deleteMutation = useDeleteMediaAsset()
  const analyzeMutation = useAnalyzeMediaAsset()
  const analyzeAllMutation = useAnalyzeAllMediaAssets()
  const [analyzingId, setAnalyzingId] = useState(null)

  const items = data?.items ?? []

  const handleCreate = async (payload) => {
    try {
      setFormError('')
      if (payload instanceof FormData) {
        await uploadMutation.mutateAsync(payload)
        toast.success('Đã upload — AI đang gắn keyword cho ảnh')
      } else {
        await createMutation.mutateAsync(payload)
        toast.success('Đã thêm media')
      }
      setUploadOpen(false)
    } catch (createError) {
      setFormError(getErrorMessage(createError))
    }
  }

  const handleAnalyze = async (asset) => {
    setAnalyzingId(asset.id)
    try {
      const result = await analyzeMutation.mutateAsync(asset.id)
      const kws = result?.keywords?.join(', ')
      toast.success(kws ? `AI gắn nhãn: ${kws}` : 'AI đã phân tích ảnh')
    } catch (analyzeError) {
      toast.error(getErrorMessage(analyzeError))
    } finally {
      setAnalyzingId(null)
    }
  }

  const handleAnalyzeAll = async () => {
    if (!confirmAction(
      'GPT sẽ lần lượt phân tích tất cả ảnh chưa có keyword. Quá trình có thể mất vài phút. Tiếp tục?',
    )) return

    try {
      const result = await analyzeAllMutation.mutateAsync({ force: false })
      toast.success(
        `Đã gắn nhãn ${result?.analyzed ?? 0} ảnh · bỏ qua ${result?.skipped ?? 0}`
        + (result?.failed ? ` · lỗi ${result.failed}` : ''),
      )
    } catch (analyzeError) {
      toast.error(getErrorMessage(analyzeError))
    }
  }

  const handleUpdate = async (event) => {
    event.preventDefault()
    if (!editingAsset) return
    const formData = new FormData(event.target)
    try {
      setFormError('')
      await updateMutation.mutateAsync({
        id: editingAsset.id,
        payload: {
          publicUrl: formData.get('publicUrl') || null,
          altText: formData.get('altText') || null,
          description: formData.get('description') || null,
        },
      })
      setEditingAsset(null)
      toast.success('Đã cập nhật media')
    } catch (updateError) {
      setFormError(getErrorMessage(updateError))
    }
  }

  const handleDelete = async (asset) => {
    const name = asset.originalFileName || asset.fileName
    if (!confirmAction(CONFIRM_MESSAGES.deleteMedia(name))) return
    try {
      await deleteMutation.mutateAsync(asset.id)
      toast.success('Đã xóa media')
    } catch (deleteError) {
      toast.error(getErrorMessage(deleteError))
    }
  }

  return (
    <section className="media-page">
      <PageHeader
        title="Media"
        description="Kho ảnh nội bộ và media do AI sinh ra"
        actions={
          canManageMedia ? (
            <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap' }}>
              <button
                type="button"
                className="btn btn-secondary"
                disabled={analyzeAllMutation.isPending}
                onClick={handleAnalyzeAll}
              >
                {analyzeAllMutation.isPending ? '⏳ Đang gắn nhãn...' : '✨ Gắn nhãn tất cả'}
              </button>
              <button
                type="button"
                className="btn btn-primary"
                onClick={() => { setFormError(''); setUploadOpen(true) }}
              >
                Thêm media
              </button>
            </div>
          ) : null
        }
      />

      <div className="card card-body media-page-filters">
        <div className="form-group" style={{ marginBottom: 0 }}>
          <label htmlFor="media-keyword">Tìm kiếm</label>
          <input
            id="media-keyword"
            value={keyword}
            onChange={(event) => setKeyword(event.target.value)}
            placeholder="Tên file, alt text, tags..."
          />
        </div>
        <div className="form-group" style={{ marginBottom: 0 }}>
          <label htmlFor="media-source">Nguồn</label>
          <select
            id="media-source"
            value={source}
            onChange={(event) => setSource(event.target.value)}
          >
            <option value="">Tất cả</option>
            {MEDIA_SOURCE_OPTIONS.map((option) => (
              <option key={option.value} value={option.value}>
                {option.label}
              </option>
            ))}
          </select>
        </div>
      </div>

      <div className="card card-body">
        <MediaGrid
          items={items}
          isLoading={isLoading}
          isError={isError}
          error={error}
          onRetry={refetch}
          onEdit={(asset) => { setFormError(''); setEditingAsset(asset) }}
          onDelete={handleDelete}
          onAnalyze={handleAnalyze}
          analyzingId={analyzingId}
          canManage={canManageMedia}
        />
      </div>

      <MediaUploadForm
        open={uploadOpen}
        onClose={() => setUploadOpen(false)}
        onSubmit={handleCreate}
        isSubmitting={createMutation.isPending || uploadMutation.isPending}
        errorMessage={formError}
      />

      <Modal
        open={Boolean(editingAsset)}
        title="Sửa media"
        onClose={() => setEditingAsset(null)}
        footer={(
          <>
            <button type="button" className="btn btn-secondary" onClick={() => setEditingAsset(null)}>
              Hủy
            </button>
            <button
              type="submit"
              form="media-edit-form"
              className="btn btn-primary"
              disabled={updateMutation.isPending}
            >
              {updateMutation.isPending ? 'Đang lưu...' : 'Lưu'}
            </button>
          </>
        )}
      >
        {editingAsset && (
          <form id="media-edit-form" onSubmit={handleUpdate}>
            {formError && <div className="alert alert-error">{formError}</div>}
            <div className="form-group">
              <label htmlFor="edit-url">Public URL</label>
              <input
                id="edit-url"
                name="publicUrl"
                defaultValue={editingAsset.publicUrl || ''}
              />
            </div>
            <div className="form-group">
              <label htmlFor="edit-alt">Alt text</label>
              <input
                id="edit-alt"
                name="altText"
                defaultValue={editingAsset.altText || ''}
              />
            </div>
            <div className="form-group">
              <label htmlFor="edit-desc">Mô tả</label>
              <textarea
                id="edit-desc"
                name="description"
                defaultValue={editingAsset.description || ''}
                rows={3}
              />
            </div>
          </form>
        )}
      </Modal>
    </section>
  )
}
