import { useMemo, useState } from 'react'
import LoadingState from '@/shared/components/LoadingState'
import ErrorState from '@/shared/components/ErrorState'
import { usePermissions } from '@/shared/hooks/usePermissions'
import { getErrorMessage } from '@/shared/utils/apiHelpers'
import { confirmAction, CONFIRM_MESSAGES } from '@/shared/utils/confirmAction'
import { toast } from '@/shared/stores/toastStore'
import { isCoverRole, isImageMime } from '../constants/mediaConstants'
import { useMediaAssetAll } from '../hooks/useMediaAssets'
import {
  useAttachPostMedia,
  useDeletePostMedia,
  usePostMediaByPost,
  useSwapPostCover,
} from '../hooks/usePostMedia'
import AiMediaPickerModal from './AiMediaPickerModal'
import './MediaAssetCard.css'

/**
 * Gallery ảnh của bài viết trong preview: ảnh AI gen (cover) + ảnh chọn từ kho.
 * Cho phép thêm nhiều ảnh (AI gợi ý theo nội dung), gỡ ảnh không ưng, đổi cover.
 */
export default function PostMediaPanel({ postId, post }) {
  const { canManageMedia } = usePermissions()
  const [pickerOpen, setPickerOpen] = useState(false)
  const [actionError, setActionError] = useState('')

  // Đang sinh nội dung nền (bài từ batch) → poll để ảnh AI gen tự hiện khi xong.
  const isGenerating = [2, 3, 12, 14].includes(Number(post?.status))

  const {
    data: postMediaList = [],
    isLoading,
    isError,
    error,
    refetch,
  } = usePostMediaByPost(postId, { refetchInterval: isGenerating ? 4000 : false })

  const { data: allAssets = [] } = useMediaAssetAll()
  const attachMedia = useAttachPostMedia()
  const swapCover = useSwapPostCover()
  const deletePostMedia = useDeletePostMedia()

  const assetMap = useMemo(
    () => Object.fromEntries(allAssets.map((a) => [a.id, a])),
    [allAssets],
  )

  const coverLink = postMediaList.find((item) => isCoverRole(item.mediaRole))
  const attachments = postMediaList
    .filter((item) => !isCoverRole(item.mediaRole))
    .sort((a, b) => (a.sortOrder ?? 0) - (b.sortOrder ?? 0))

  const orderedLinks = coverLink ? [coverLink, ...attachments] : attachments
  const attachedMediaIds = new Set(postMediaList.map((item) => item.mediaId))
  const maxSortOrder = postMediaList.reduce(
    (max, item) => Math.max(max, item.sortOrder ?? 0),
    0,
  )

  // Ảnh đã gắn hiện sẵn dạng "đã chọn" trong picker
  const attachedAssets = postMediaList
    .map((item) => assetMap[item.mediaId])
    .filter(Boolean)

  const aiQuery = (post?.content || post?.title || '').slice(0, 500)

  const handlePickerConfirm = async (selectedAssets) => {
    const newIds = selectedAssets
      .map((asset) => asset.id)
      .filter((id) => !attachedMediaIds.has(id))
    if (newIds.length === 0) return

    setActionError('')
    try {
      await attachMedia.mutateAsync({
        postId,
        mediaIds: newIds,
        hasCover: Boolean(coverLink),
        nextSortOrder: maxSortOrder + 1,
      })
      toast.success(`Đã thêm ${newIds.length} ảnh vào bài viết`)
    } catch (attachError) {
      setActionError(getErrorMessage(attachError))
      toast.error(getErrorMessage(attachError))
    }
  }

  const handleMakeCover = async (link) => {
    setActionError('')
    try {
      await swapCover.mutateAsync({
        postId,
        linkId: link.id,
        currentCoverLinkId: coverLink?.id,
        nextSortOrder: maxSortOrder + 1,
      })
      toast.success('Đã đổi ảnh cover')
    } catch (swapError) {
      setActionError(getErrorMessage(swapError))
      toast.error(getErrorMessage(swapError))
    }
  }

  const handleRemoveLink = async (link) => {
    if (!confirmAction(CONFIRM_MESSAGES.detachMedia())) return
    setActionError('')
    try {
      await deletePostMedia.mutateAsync({ id: link.id, postId })
      toast.success('Đã gỡ ảnh khỏi bài viết')
    } catch (removeError) {
      setActionError(getErrorMessage(removeError))
      toast.error(getErrorMessage(removeError))
    }
  }

  const isBusy = attachMedia.isPending || swapCover.isPending || deletePostMedia.isPending

  return (
    <div className="card card-body" style={{ marginBottom: 16 }}>
      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: 4 }}>
        <h2 style={{ margin: 0, fontSize: '1.05rem' }}>Ảnh bài viết</h2>
        <div style={{ display: 'flex', gap: 8 }}>
          <button
            type="button"
            className="btn btn-secondary btn-sm"
            onClick={() => refetch()}
            disabled={isLoading}
          >
            Làm mới
          </button>
          {canManageMedia && (
            <button
              type="button"
              className="btn btn-primary btn-sm"
              onClick={() => { setActionError(''); setPickerOpen(true) }}
              disabled={isBusy}
            >
              ✨ Chọn media phù hợp
            </button>
          )}
        </div>
      </div>
      <p style={{ margin: '0 0 12px', fontSize: '0.82rem', color: 'var(--color-text-muted)' }}>
        Ảnh AI sinh sẽ là cover. Có thể thêm nhiều ảnh từ kho (AI lọc theo nội dung bài),
        gỡ ảnh không ưng hoặc đổi cover trước khi đăng.
      </p>

      {actionError && <div className="alert alert-error">{actionError}</div>}

      {isLoading && <LoadingState message="Đang tải ảnh bài viết..." />}
      {isError && <ErrorState message={getErrorMessage(error)} onRetry={refetch} />}

      {!isLoading && !isError && (
        orderedLinks.length === 0 ? (
          <p style={{ margin: 0, color: 'var(--color-text-muted)', fontSize: '0.9rem' }}>
            Chưa có ảnh — chờ AI sinh ảnh hoặc nhấn &quot;Chọn media phù hợp&quot; để thêm từ kho.
          </p>
        ) : (
          <div className="post-media-gallery">
            {orderedLinks.map((link) => {
              const asset = assetMap[link.mediaId]
              const isCover = isCoverRole(link.mediaRole)
              return (
                <div key={link.id} className={`post-media-item ${isCover ? 'is-cover' : ''}`}>
                  <div className="post-media-thumb">
                    {asset?.publicUrl && isImageMime(asset.mimeType) ? (
                      <img
                        src={asset.publicUrl}
                        alt={asset.altText || asset.originalFileName || asset.fileName}
                        loading="lazy"
                      />
                    ) : (
                      <span className="post-media-thumb-fallback">Không xem trước được</span>
                    )}
                    {isCover && <span className="post-media-cover-badge">Cover</span>}
                  </div>
                  <div className="post-media-item-name" title={asset?.originalFileName || asset?.fileName}>
                    {asset?.originalFileName || asset?.fileName || link.mediaId}
                  </div>
                  {canManageMedia && (
                    <div className="post-media-item-actions">
                      {!isCover && (
                        <button
                          type="button"
                          className="btn btn-secondary btn-sm"
                          onClick={() => handleMakeCover(link)}
                          disabled={isBusy}
                        >
                          Đặt làm cover
                        </button>
                      )}
                      <button
                        type="button"
                        className="btn btn-danger btn-sm"
                        onClick={() => handleRemoveLink(link)}
                        disabled={isBusy}
                      >
                        Gỡ
                      </button>
                    </div>
                  )}
                </div>
              )
            })}
          </div>
        )
      )}

      <AiMediaPickerModal
        open={pickerOpen}
        onClose={() => setPickerOpen(false)}
        onConfirm={handlePickerConfirm}
        query={aiQuery}
        initialSelected={attachedAssets}
      />
    </div>
  )
}
