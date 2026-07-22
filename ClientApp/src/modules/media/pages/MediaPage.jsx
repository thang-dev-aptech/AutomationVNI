import { useMemo, useState } from 'react'
import PageHeader from '@/shared/components/PageHeader'
import Modal from '@/shared/components/Modal'
import StatusBadge from '@/shared/components/StatusBadge'
import { usePermissions } from '@/shared/hooks/usePermissions'
import { formatDateTime, formatFileSize, getErrorMessage } from '@/shared/utils/apiHelpers'
import { confirmAction, CONFIRM_MESSAGES } from '@/shared/utils/confirmAction'
import { toast } from '@/shared/stores/toastStore'
import MediaGrid from '../components/MediaGrid'
import MediaUploadForm from '../components/MediaUploadForm'
import MediaFolderTree from '../components/MediaFolderTree'
import MediaFolderFormModal from '../components/MediaFolderFormModal'
import {
  useAnalyzeAllMediaAssets,
  useAnalyzeMediaAsset,
  useCreateMediaAsset,
  useDeleteMediaAsset,
  useMediaAssets,
  useMoveMediaAssets,
  useUpdateMediaAsset,
  useUploadMediaAsset,
  useUploadMediaBatch,
} from '../hooks/useMediaAssets'
import { useCategoryList } from '@/modules/categories/hooks/useCategories'
import {
  useCreateMediaFolder,
  useDeleteMediaFolder,
  useMediaFolderTree,
  useUpdateMediaFolder,
} from '../hooks/useMediaFolders'
import {
  MEDIA_SOURCE_OPTIONS,
  getMediaSourceMeta,
  isImageMime,
} from '../constants/mediaConstants'
import './MediaPage.css'

export default function MediaPage() {
  const { canManageMedia } = usePermissions()
  const [keyword, setKeyword] = useState('')
  const [source, setSource] = useState('')
  const [uploadOpen, setUploadOpen] = useState(false)
  const [editingAsset, setEditingAsset] = useState(null)
  const [viewingAsset, setViewingAsset] = useState(null)
  const [detailsAsset, setDetailsAsset] = useState(null)
  const [formError, setFormError] = useState('')

  // Folder: selection = 'all' | 'unassigned' | <folderId>
  const [selection, setSelection] = useState('all')
  const [folderModal, setFolderModal] = useState(null) // { editing, defaultParentId } | null

  const params = useMemo(
    () => ({
      keyword,
      index: 1,
      size: 48,
      source: source ? Number(source) : undefined,
      folderId: selection !== 'all' && selection !== 'unassigned' ? selection : undefined,
      unassigned: selection === 'unassigned' ? true : undefined,
    }),
    [keyword, source, selection],
  )

  const { data, isLoading, isError, error, refetch } = useMediaAssets(params)
  const { data: folders = [] } = useMediaFolderTree()
  const createMutation = useCreateMediaAsset()
  const uploadMutation = useUploadMediaAsset()
  const uploadBatchMutation = useUploadMediaBatch()
  const { data: categoryData } = useCategoryList({ index: 1, size: 200 })
  const categories = categoryData?.items ?? []
  const updateMutation = useUpdateMediaAsset()
  const deleteMutation = useDeleteMediaAsset()
  const moveMutation = useMoveMediaAssets()
  const analyzeMutation = useAnalyzeMediaAsset()
  const analyzeAllMutation = useAnalyzeAllMediaAssets()
  const createFolderMutation = useCreateMediaFolder()
  const updateFolderMutation = useUpdateMediaFolder()
  const deleteFolderMutation = useDeleteMediaFolder()
  const [analyzingId, setAnalyzingId] = useState(null)

  const items = data?.items ?? []
  const currentFolderId = selection !== 'all' && selection !== 'unassigned' ? selection : null

  const handleCreate = async (payload) => {
    try {
      setFormError('')
      if (payload instanceof FormData) {
        // Batch endpoint xử lý 1..N ảnh; AI gắn keyword tuần tự.
        const result = await uploadBatchMutation.mutateAsync(payload)
        const uploaded = result?.uploaded ?? 0
        const failed = result?.failed ?? 0
        toast.success(
          failed > 0
            ? `Đã upload ${uploaded} ảnh (lỗi ${failed}) — AI đang gắn keyword`
            : `Đã upload ${uploaded} ảnh — AI đang gắn keyword`,
        )
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
      // Cập nhật popup chi tiết đang mở để nhãn mới hiện ngay, không phải đóng/mở lại.
      setDetailsAsset((prev) => (prev && prev.id === asset.id ? { ...prev, ...result } : prev))
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
      // Đóng popup đang mở nếu là ảnh vừa xóa.
      setDetailsAsset((prev) => (prev?.id === asset.id ? null : prev))
      setViewingAsset((prev) => (prev?.id === asset.id ? null : prev))
    } catch (deleteError) {
      toast.error(getErrorMessage(deleteError))
    }
  }

  const handleMoveAsset = async (assetId, folderId) => {
    try {
      await moveMutation.mutateAsync({ ids: [assetId], folderId })
      toast.success(folderId ? 'Đã chuyển ảnh vào thư mục' : 'Đã đưa ảnh về "Chưa phân loại"')
    } catch (moveError) {
      toast.error(getErrorMessage(moveError))
    }
  }

  const handleFolderSubmit = async ({ name, parentFolderId }) => {
    try {
      setFormError('')
      if (folderModal?.editing) {
        await updateFolderMutation.mutateAsync({
          id: folderModal.editing.id,
          payload: { name, parentFolderId },
        })
        toast.success('Đã cập nhật thư mục')
      } else {
        await createFolderMutation.mutateAsync({ name, parentFolderId })
        toast.success('Đã tạo thư mục')
      }
      setFolderModal(null)
    } catch (folderError) {
      setFormError(getErrorMessage(folderError))
    }
  }

  const handleDeleteFolder = async (folder) => {
    if (!confirmAction(`Xóa thư mục "${folder.name}"? Ảnh bên trong sẽ đưa về "Chưa phân loại".`)) return
    try {
      await deleteFolderMutation.mutateAsync(folder.id)
      toast.success('Đã xóa thư mục')
      if (selection === folder.id) setSelection('all')
    } catch (folderError) {
      toast.error(getErrorMessage(folderError))
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
                className="btn btn-secondary"
                onClick={() => { setFormError(''); setFolderModal({ editing: null, defaultParentId: currentFolderId }) }}
              >
                📁 Tạo thư mục
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

      <div className="media-layout">
        <aside className="card card-body media-sidebar">
          <h3 className="media-sidebar-title">Thư mục</h3>
          <MediaFolderTree
            folders={folders}
            selection={selection}
            onSelect={setSelection}
            canManage={canManageMedia}
            onMoveAsset={handleMoveAsset}
            onCreateChild={(parentId) => { setFormError(''); setFolderModal({ editing: null, defaultParentId: parentId }) }}
            onRename={(folder) => { setFormError(''); setFolderModal({ editing: folder, defaultParentId: null }) }}
            onDelete={handleDeleteFolder}
          />
        </aside>

        <div className="media-main">
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
              onView={(asset) => setViewingAsset(asset)}
              onDetails={(asset) => setDetailsAsset(asset)}
              onDelete={handleDelete}
              canManage={canManageMedia}
            />
          </div>
        </div>
      </div>

      <MediaUploadForm
        open={uploadOpen}
        onClose={() => setUploadOpen(false)}
        onSubmit={handleCreate}
        isSubmitting={createMutation.isPending || uploadMutation.isPending || uploadBatchMutation.isPending}
        errorMessage={formError}
        folders={folders}
        categories={categories}
        defaultFolderId={currentFolderId}
      />

      <MediaFolderFormModal
        open={Boolean(folderModal)}
        editing={folderModal?.editing ?? null}
        defaultParentId={folderModal?.defaultParentId ?? null}
        parentOptions={folders}
        onClose={() => setFolderModal(null)}
        onSubmit={handleFolderSubmit}
        isSubmitting={createFolderMutation.isPending || updateFolderMutation.isPending}
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

      {/* Xem ảnh: popup chỉ hiện ảnh, không rời trang */}
      <Modal
        open={Boolean(viewingAsset)}
        title={viewingAsset?.originalFileName || viewingAsset?.fileName || 'Ảnh'}
        onClose={() => setViewingAsset(null)}
      >
        {viewingAsset && (
          viewingAsset.publicUrl && isImageMime(viewingAsset.mimeType) ? (
            <img
              className="media-lightbox-img"
              src={viewingAsset.publicUrl}
              alt={viewingAsset.altText || viewingAsset.fileName}
            />
          ) : (
            <div className="media-lightbox-fallback">
              Không xem trước được ({viewingAsset.mimeType || 'unknown'})
            </div>
          )
        )}
      </Modal>

      {/* Chi tiết: thông tin + nhãn dán; chưa có nhãn thì gán ngay trong popup */}
      <Modal
        open={Boolean(detailsAsset)}
        title="Chi tiết media"
        onClose={() => setDetailsAsset(null)}
        footer={(
          <>
            {canManageMedia && (
              <button
                type="button"
                className="btn btn-secondary"
                onClick={() => {
                  setFormError('')
                  setEditingAsset(detailsAsset)
                  setDetailsAsset(null)
                }}
              >
                Sửa
              </button>
            )}
            <button type="button" className="btn btn-primary" onClick={() => setDetailsAsset(null)}>
              Đóng
            </button>
          </>
        )}
      >
        {detailsAsset && (
          <div className="media-details">
            {detailsAsset.publicUrl && isImageMime(detailsAsset.mimeType) && (
              <button
                type="button"
                className="media-details-thumb"
                title="Ấn để xem ảnh lớn"
                onClick={() => { setViewingAsset(detailsAsset); setDetailsAsset(null) }}
              >
                <img src={detailsAsset.publicUrl} alt={detailsAsset.altText || detailsAsset.fileName} />
              </button>
            )}

            <dl className="media-details-list">
              <dt>Tên file</dt>
              <dd>{detailsAsset.originalFileName || detailsAsset.fileName}</dd>
              <dt>Nguồn</dt>
              <dd><StatusBadge {...getMediaSourceMeta(detailsAsset.source)} /></dd>
              <dt>Dung lượng</dt>
              <dd>{formatFileSize(detailsAsset.fileSize)}</dd>
              {(detailsAsset.width || detailsAsset.height) && (
                <>
                  <dt>Kích thước</dt>
                  <dd>{detailsAsset.width ?? '?'} × {detailsAsset.height ?? '?'} px</dd>
                </>
              )}
              <dt>Ngày tạo</dt>
              <dd>{formatDateTime(detailsAsset.createdAt)}</dd>
              {detailsAsset.altText && (
                <>
                  <dt>Alt text</dt>
                  <dd>{detailsAsset.altText}</dd>
                </>
              )}
              {detailsAsset.description && (
                <>
                  <dt>Mô tả</dt>
                  <dd>{detailsAsset.description}</dd>
                </>
              )}
            </dl>

            <div className="media-details-labels">
              <div className="media-details-labels-head">
                <span className="ai-media-keyword-label">Nhãn dán</span>
                {canManageMedia && isImageMime(detailsAsset.mimeType) && (
                  <button
                    type="button"
                    className="btn btn-ghost btn-sm"
                    disabled={analyzingId === detailsAsset.id}
                    onClick={() => handleAnalyze(detailsAsset)}
                  >
                    {analyzingId === detailsAsset.id
                      ? '⏳ AI...'
                      : (detailsAsset.keywords?.length ? '✨ Gắn lại nhãn' : '✨ Gán nhãn')}
                  </button>
                )}
              </div>
              {detailsAsset.keywords?.length ? (
                <div className="media-asset-card-keywords">
                  {detailsAsset.keywords.map((kw) => (
                    <span key={kw} className="ai-media-keyword-chip">{kw}</span>
                  ))}
                </div>
              ) : (
                <p className="media-details-empty">Chưa có nhãn nào.</p>
              )}
            </div>
          </div>
        )}
      </Modal>
    </section>
  )
}
