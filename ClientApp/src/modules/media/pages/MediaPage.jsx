import { useEffect, useMemo, useState } from 'react'
import PageHeader from '@/shared/components/PageHeader'
import Modal from '@/shared/components/Modal'
import StatusBadge from '@/shared/components/StatusBadge'
import ContextMenu from '@/shared/components/ContextMenu'
import { usePermissions } from '@/shared/hooks/usePermissions'
import { formatDateTime, formatFileSize, getErrorMessage } from '@/shared/utils/apiHelpers'
import { confirmAction, CONFIRM_MESSAGES } from '@/shared/utils/confirmAction'
import { toast } from '@/shared/stores/toastStore'
import MediaBrowserGrid from '../components/MediaBrowserGrid'
import MediaUploadForm from '../components/MediaUploadForm'
import MediaFolderSearchBox from '../components/MediaFolderSearchBox'
import MediaFolderFormModal from '../components/MediaFolderFormModal'
import MoveMediaFolderModal from '../components/MoveMediaFolderModal'
import AiBackgroundPromptModal from '../components/AiBackgroundPromptModal'
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
  useAnalyzeLayoutFolder,
  useAnalyzeLayout,
} from '../hooks/useMediaAssets'
import { useCategoryList } from '@/modules/categories/hooks/useCategories'
import { useSocialChannelAll } from '@/modules/social-channels/hooks/useSocialChannels'
import {
  useCreateMediaFolder,
  useCreateMediaFolderAcrossPages,
  useDeleteMediaFolder,
  useUpdateMediaFolder,
} from '../hooks/useMediaFolders'
import { useMediaBrowser } from '../hooks/useMediaBrowser'
import {
  MEDIA_SOURCE_OPTIONS,
  getMediaSourceMeta,
  hasTemplateLayout,
  isImageMime,
} from '../constants/mediaConstants'
import './MediaPage.css'

export default function MediaPage() {
  const { canManageMedia } = usePermissions()
  const [keyword, setKeyword] = useState('')
  const [source, setSource] = useState('')
  const [uploadOpen, setUploadOpen] = useState(false)
  const [aiPromptOpen, setAiPromptOpen] = useState(false)
  const [editingAsset, setEditingAsset] = useState(null)
  const [viewingAsset, setViewingAsset] = useState(null)
  const [detailsAsset, setDetailsAsset] = useState(null)
  const [formError, setFormError] = useState('')
  const [folderModal, setFolderModal] = useState(null) // { mode, editing, defaultParentId, defaultSocialChannelId } | null
  const [moveModal, setMoveModal] = useState(null) // { folder } | null
  const [folderContextMenu, setFolderContextMenu] = useState(null) // { x, y, folder } | null
  const [fileContextMenu, setFileContextMenu] = useState(null) // { x, y, asset } | null
  const [gridContextMenu, setGridContextMenu] = useState(null) // { x, y } | null
  const [filePageIndex, setFilePageIndex] = useState(1)

  const { data: channels = [] } = useSocialChannelAll()
  const browser = useMediaBrowser()
  const { currentFolderId, derivedPageId, selection, items } = browser

  useEffect(() => {
    setFilePageIndex(1)
  }, [keyword, source, selection])

  const params = useMemo(
    () => ({
      keyword,
      index: filePageIndex,
      size: 48,
      source: source ? Number(source) : undefined,
      folderId: selection !== 'all' && selection !== 'unassigned' ? selection : undefined,
      unassigned: selection === 'unassigned' ? true : undefined,
    }),
    [keyword, source, selection, filePageIndex],
  )

  const { data, isLoading, isError, error, refetch } = useMediaAssets(params)
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
  const createFolderAcrossPagesMutation = useCreateMediaFolderAcrossPages()
  const updateFolderMutation = useUpdateMediaFolder()
  const deleteFolderMutation = useDeleteMediaFolder()
  const analyzeLayoutMutation = useAnalyzeLayoutFolder()
  const analyzeLayoutSingleMutation = useAnalyzeLayout()
  const [analyzingId, setAnalyzingId] = useState(null)
  const [analyzingLayoutId, setAnalyzingLayoutId] = useState(null)

  const fileItems = data?.items ?? []
  const filePageSize = data?.size > 0 ? data.size : 48
  const totalFilePages = Math.max(1, Math.ceil((data?.total ?? 0) / filePageSize))
  const browserFolders = items

  useEffect(() => {
    if (currentFolderId !== null || items.length > 0) return
    // Auto-load root if no folders yet
    browser.openRoot()
  }, []) // eslint-disable-line react-hooks/exhaustive-deps

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

  const handleAnalyzeLayoutSingle = async (asset) => {
    setAnalyzingLayoutId(asset.id)
    try {
      const result = await analyzeLayoutSingleMutation.mutateAsync(asset.id)
      toast.success('Đã quét Vùng An Toàn')
      // Cập nhật popup đang mở (Chi tiết / Xem ảnh) để huy hiệu/tags mới hiện ngay.
      setDetailsAsset((prev) => (prev && prev.id === asset.id ? { ...prev, ...result } : prev))
      setViewingAsset((prev) => (prev && prev.id === asset.id ? { ...prev, ...result } : prev))
    } catch (error) {
      toast.error(getErrorMessage(error))
    } finally {
      setAnalyzingLayoutId(null)
    }
  }

  const handleAnalyzeLayoutFolder = async () => {
    if (!currentFolderId) return
    if (!confirmAction('GPT sẽ quét tất cả ảnh trong thư mục này để tìm Vùng An Toàn cho chữ. Tiếp tục?')) return

    try {
      const result = await analyzeLayoutMutation.mutateAsync(currentFolderId)
      toast.success(
        `Đã quét layout ${result?.analyzed ?? 0} ảnh`
        + (result?.failed ? ` · lỗi ${result.failed}` : ''),
      )
    } catch (error) {
      toast.error(getErrorMessage(error))
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

  const handleGridFileDrop = async (formData) => {
    // Append current folder to the FormData if inside a folder
    if (currentFolderId) {
      formData.append('folderId', currentFolderId)
    }
    await handleCreate(formData)
  }

  const handleFolderContextMenu = (event, folder) => {
    event.preventDefault()
    setFileContextMenu(null)
    setGridContextMenu(null)
    setFolderContextMenu({
      x: event.clientX,
      y: event.clientY,
      folder,
    })
  }

  const handleFileContextMenu = (event, asset) => {
    event.preventDefault()
    setFolderContextMenu(null)
    setGridContextMenu(null)
    setFileContextMenu({
      x: event.clientX,
      y: event.clientY,
      asset,
    })
  }

  const handleGridContextMenu = (event) => {
    event.preventDefault()
    setFolderContextMenu(null)
    setFileContextMenu(null)
    setGridContextMenu({
      x: event.clientX,
      y: event.clientY,
    })
  }

  const handleGridContextMenuSelect = (action) => {
    setGridContextMenu(null)
    setFormError('')
    if (action === 'folder') {
      if (currentFolderId === null) {
        setFolderModal({ mode: 'page-roots' })
      } else {
        setFolderModal({
          mode: 'create-child',
          defaultParentId: currentFolderId,
          defaultSocialChannelId: derivedPageId,
        })
      }
      return
    }
    if (action === 'media') {
      setUploadOpen(true)
    }
  }

  const handleFolderContextMenuSelect = async (action, folder) => {
    setFolderContextMenu(null)
    switch (action) {
      case 'open':
        browser.openFolder(folder)
        break
      case 'create':
        setFormError('')
        setFolderModal({
          mode: 'create-child',
          defaultParentId: folder.id,
          defaultSocialChannelId: folder.socialChannelId,
        })
        break
      case 'rename':
        setFormError('')
        setFolderModal({ mode: 'rename', editing: folder })
        break
      case 'move':
        setFormError('')
        setMoveModal({ folder })
        break
      case 'delete':
        await handleDeleteFolder(folder)
        break
      default:
        break
    }
  }

  const handleFileContextMenuSelect = async (action, asset) => {
    setFileContextMenu(null)
    switch (action) {
      case 'view':
        setViewingAsset(asset)
        break
      case 'details':
        setDetailsAsset(asset)
        break
      case 'delete':
        await handleDelete(asset)
        break
      default:
        break
    }
  }

  const handleFolderSubmit = async (payload) => {
    try {
      setFormError('')

      if (folderModal?.mode === 'rename') {
        // Rename mode: update folder name
        await updateFolderMutation.mutateAsync({
          id: folderModal.editing.id,
          payload: {
            name: payload.name,
            parentFolderId: payload.parentFolderId,
            socialChannelId: payload.socialChannelId,
          },
        })
        toast.success('Đã cập nhật thư mục')
      } else if (folderModal?.mode === 'create-child') {
        // Create child mode: create single child in fixed parent
        await createFolderMutation.mutateAsync({
          name: payload.name,
          parentFolderId: payload.parentFolderId,
          socialChannelId: payload.socialChannelId,
        })
        toast.success('Đã tạo thư mục')
      } else if (folderModal?.mode === 'page-roots' && payload.items) {
        // Page-roots mode: create roots for selected Pages
        const result = await createFolderAcrossPagesMutation.mutateAsync({
          description: null,
          items: payload.items,
        })
        if (result.totalFailed > 0) {
          const nameById = new Map(payload.items.map((item) => [item.socialChannelId, item.name]))
          const failedItems = result.results.filter((item) => !item.success)
          const preview = failedItems
            .slice(0, 5)
            .map((item) => nameById.get(item.socialChannelId) ?? item.socialChannelId)
            .join(', ')
          const more = failedItems.length > 5 ? ` và ${failedItems.length - 5} Page khác` : ''
          toast.warning(
            `Đã tạo ${result.totalSucceeded}/${result.totalRequested} thư mục. Lỗi ở Page: ${preview}${more}`,
          )
        } else {
          toast.success(`Đã tạo ${result.totalSucceeded} thư mục`)
        }
      }
      setFolderModal(null)
    } catch (folderError) {
      setFormError(getErrorMessage(folderError))
    }
  }

  const handleMoveFolder = async (payload) => {
    try {
      setFormError('')
      await updateFolderMutation.mutateAsync({
        id: payload.folderId,
        payload: {
          name: moveModal.folder.name,
          parentFolderId: payload.parentFolderId,
          socialChannelId: moveModal.folder.socialChannelId,
        },
      })
      toast.success('Đã di chuyển thư mục')
      setMoveModal(null)
    } catch (folderError) {
      setFormError(getErrorMessage(folderError))
    }
  }

  const handleDeleteFolder = async (folder) => {
    if (!confirmAction(`Xóa thư mục "${folder.name}"? Ảnh bên trong sẽ đưa về "Chưa phân loại".`)) return
    try {
      await deleteFolderMutation.mutateAsync(folder.id)
      toast.success('Đã xóa thư mục')
      if (selection === folder.id || currentFolderId === folder.id) browser.openRoot()
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
                onClick={() => { setFormError(''); setFolderModal({ mode: 'page-roots' }) }}
              >
                📁 Tạo thư mục
              </button>
              <button
                type="button"
                className="btn btn-secondary"
                onClick={() => setAiPromptOpen(true)}
              >
                ✨ Tạo prompt ảnh nền AI
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

      <div className="media-main-content">
        <MediaFolderSearchBox
          onOpenFolder={(folderId, pageId) => {
            // useMediaBrowser.openFolder cần cả socialChannelId để suy ra Page —
            // search box chỉ trả 2 tham số rời, phải tự dựng lại thành object.
            browser.openFolder({ id: folderId, socialChannelId: pageId })
          }}
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
          <div style={{ display: 'flex', gap: 8 }}>
            <button
              type="button"
              className={`btn ${selection === 'all' ? 'btn-primary' : 'btn-ghost'}`}
              onClick={browser.selectAll}
            >
              Tất cả
            </button>
            <button
              type="button"
              className={`btn ${selection === 'unassigned' ? 'btn-primary' : 'btn-ghost'}`}
              onClick={browser.selectUnassigned}
            >
              Chưa phân loại
            </button>
          </div>
          {currentFolderId && (
            <div className="media-page-filters-actions">
              <button
                type="button"
                className="btn btn-secondary"
                disabled={analyzeLayoutMutation.isPending}
                onClick={handleAnalyzeLayoutFolder}
              >
                {analyzeLayoutMutation.isPending ? '⏳ Đang quét...' : '✨ Quét Vùng An Toàn'}
              </button>
            </div>
          )}
        </div>

        <div className="card card-body">
          <MediaBrowserGrid
            folders={browserFolders}
            files={fileItems}
            isLoading={browser.isLoading}
            isError={browser.isError}
            error={browser.error}
            onRetry={browser.refetch}
            onFolderClick={browser.openFolder}
            onFolderContextMenu={handleFolderContextMenu}
            onFolderDrop={handleMoveAsset}
            onFileDrop={handleCreate}
            onFileView={(asset) => setViewingAsset(asset)}
            onFileDetails={(asset) => setDetailsAsset(asset)}
            onFileDelete={handleDelete}
            onFileContextMenu={handleFileContextMenu}
            canManage={canManageMedia}
            isRootLevel={currentFolderId === null}
            onGridContextMenu={handleGridContextMenu}
            onGridFileDrop={handleGridFileDrop}
            pageIndex={browser.pageIndex}
            totalPages={browser.totalPages}
            onPageChange={browser.goToPage}
            filePageIndex={filePageIndex}
            totalFilePages={totalFilePages}
            onFilePageChange={setFilePageIndex}
            folderBreadcrumb={(
              <nav className="media-folder-breadcrumb" aria-label="Đường dẫn thư mục">
                <button
                  type="button"
                  className={`media-folder-breadcrumb-item${!currentFolderId ? ' is-current' : ''}`}
                  onClick={browser.openRoot}
                >
                  Thư mục gốc
                </button>
                {browser.ancestors.map((item, index) => (
                  <span key={item.id}>
                    <span className="media-folder-breadcrumb-sep">/</span>
                    <button
                      type="button"
                      className={`media-folder-breadcrumb-item${index === browser.ancestors.length - 1 ? ' is-current' : ''}`}
                      onClick={() => browser.openBreadcrumb(item)}
                    >
                      {item.name}
                    </button>
                  </span>
                ))}
              </nav>
            )}
          />
        </div>
      </div>

      {folderContextMenu && (
        <ContextMenu
          x={folderContextMenu.x}
          y={folderContextMenu.y}
          items={[
            { label: 'Mở', onSelect: () => handleFolderContextMenuSelect('open', folderContextMenu.folder) },
            ...(canManageMedia ? [
              { label: 'Tạo con', onSelect: () => handleFolderContextMenuSelect('create', folderContextMenu.folder) },
              { label: 'Đổi tên', onSelect: () => handleFolderContextMenuSelect('rename', folderContextMenu.folder) },
              { label: 'Di chuyển tới', onSelect: () => handleFolderContextMenuSelect('move', folderContextMenu.folder) },
              { label: 'Xóa', onSelect: () => handleFolderContextMenuSelect('delete', folderContextMenu.folder), danger: true },
            ] : []),
          ]}
          onDismiss={() => setFolderContextMenu(null)}
        />
      )}

      {fileContextMenu && (
        <ContextMenu
          x={fileContextMenu.x}
          y={fileContextMenu.y}
          items={[
            { label: 'Xem', onSelect: () => handleFileContextMenuSelect('view', fileContextMenu.asset) },
            { label: 'Chi tiết', onSelect: () => handleFileContextMenuSelect('details', fileContextMenu.asset) },
            ...(canManageMedia ? [
              { label: 'Xóa', onSelect: () => handleFileContextMenuSelect('delete', fileContextMenu.asset), danger: true },
            ] : []),
          ]}
          onDismiss={() => setFileContextMenu(null)}
        />
      )}

      {gridContextMenu && (
        <ContextMenu
          x={gridContextMenu.x}
          y={gridContextMenu.y}
          items={[
            { label: 'Tạo thư mục', onSelect: () => handleGridContextMenuSelect('folder') },
            { label: 'Thêm media', onSelect: () => handleGridContextMenuSelect('media') },
          ]}
          onDismiss={() => setGridContextMenu(null)}
        />
      )}

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
        footer={(
          canManageMedia && viewingAsset && isImageMime(viewingAsset.mimeType) && (
            <button
              type="button"
              className="btn btn-secondary"
              disabled={analyzingLayoutId === viewingAsset.id}
              onClick={() => handleAnalyzeLayoutSingle(viewingAsset)}
            >
              {analyzingLayoutId === viewingAsset.id ? '⏳ Đang quét...' : '📐 Quét Vùng An Toàn'}
            </button>
          )
        )}
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

            <div className="media-details-labels">
              <div className="media-details-labels-head">
                <span className="ai-media-keyword-label">
                  Vùng An Toàn (Template)
                  {hasTemplateLayout(detailsAsset.tags) && ' — ✅ đã quét'}
                </span>
                {canManageMedia && isImageMime(detailsAsset.mimeType) && (
                  <button
                    type="button"
                    className="btn btn-ghost btn-sm"
                    disabled={analyzingLayoutId === detailsAsset.id}
                    onClick={() => handleAnalyzeLayoutSingle(detailsAsset)}
                  >
                    {analyzingLayoutId === detailsAsset.id
                      ? '⏳ Đang quét...'
                      : (hasTemplateLayout(detailsAsset.tags) ? '📐 Quét lại' : '📐 Quét Vùng An Toàn')}
                  </button>
                )}
              </div>
              {!hasTemplateLayout(detailsAsset.tags) && (
                <p className="media-details-empty">
                  Chưa quét — cần quét trước khi dùng ảnh này cho bài Template.
                </p>
              )}
            </div>
          </div>
        )}
      </Modal>

      <MediaFolderFormModal
        open={Boolean(folderModal)}
        mode={folderModal?.mode ?? 'rename'}
        editing={folderModal?.editing ?? null}
        defaultParentId={folderModal?.defaultParentId ?? null}
        defaultSocialChannelId={folderModal?.defaultSocialChannelId ?? null}
        onClose={() => setFolderModal(null)}
        onSubmit={handleFolderSubmit}
        isSubmitting={createFolderMutation.isPending || updateFolderMutation.isPending || createFolderAcrossPagesMutation.isPending}
        errorMessage={formError}
      />

      <MoveMediaFolderModal
        open={Boolean(moveModal)}
        folder={moveModal?.folder ?? null}
        onClose={() => setMoveModal(null)}
        onSubmit={handleMoveFolder}
        isSubmitting={updateFolderMutation.isPending}
        errorMessage={formError}
      />

      <MediaUploadForm
        open={uploadOpen}
        onClose={() => setUploadOpen(false)}
        onSubmit={handleCreate}
        isSubmitting={createMutation.isPending || uploadMutation.isPending || uploadBatchMutation.isPending}
        errorMessage={formError}
        folders={browser.folderOptions}
        categories={categories}
        defaultFolderId={currentFolderId}
      />

      <AiBackgroundPromptModal
        open={aiPromptOpen}
        onClose={() => setAiPromptOpen(false)}
      />
    </section>
  )
}
