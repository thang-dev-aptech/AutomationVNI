import { useMemo, useState } from 'react'
import LoadingState from '@/shared/components/LoadingState'
import ErrorState from '@/shared/components/ErrorState'
import StatusBadge from '@/shared/components/StatusBadge'
import { usePermissions } from '@/shared/hooks/usePermissions'
import { getErrorMessage } from '@/shared/utils/apiHelpers'
import { confirmAction, CONFIRM_MESSAGES } from '@/shared/utils/confirmAction'
import { toast } from '@/shared/stores/toastStore'
import { COVER_ROLE, getMediaRoleLabel, isImageMime } from '../constants/mediaConstants'
import { useMediaAssetAll } from '../hooks/useMediaAssets'
import {
  useDeletePostMedia,
  usePostMediaByPost,
  useSetPostCover,
} from '../hooks/usePostMedia'
import MediaPickerModal from './MediaPickerModal'
import './MediaAssetCard.css'

export default function PostMediaPanel({ postId }) {
  const { canManageMedia } = usePermissions()
  const [pickerOpen, setPickerOpen] = useState(false)
  const [actionError, setActionError] = useState('')

  const {
    data: postMediaList = [],
    isLoading,
    isError,
    error,
    refetch,
  } = usePostMediaByPost(postId)

  const { data: allAssets = [] } = useMediaAssetAll()
  const setCover = useSetPostCover()
  const deletePostMedia = useDeletePostMedia()

  const assetMap = useMemo(
    () => Object.fromEntries(allAssets.map((a) => [a.id, a])),
    [allAssets],
  )

  const coverLink = postMediaList.find((item) => item.mediaRole === COVER_ROLE)
  const coverAsset = coverLink ? assetMap[coverLink.mediaId] : null

  const otherMedia = postMediaList.filter((item) => item.mediaRole !== COVER_ROLE)

  const handleSelectCover = async (asset) => {
    setActionError('')
    try {
      await setCover.mutateAsync({
        postId,
        mediaId: asset.id,
        existingCoverId: coverLink?.id,
      })
      setPickerOpen(false)
      toast.success('Đã gắn media cover')
    } catch (selectError) {
      setActionError(getErrorMessage(selectError))
    }
  }

  const handleRemoveLink = async (link) => {
    if (!confirmAction(CONFIRM_MESSAGES.detachMedia())) return
    setActionError('')
    try {
      await deletePostMedia.mutateAsync({ id: link.id, postId })
      toast.success('Đã gỡ media khỏi bài viết')
    } catch (removeError) {
      setActionError(getErrorMessage(removeError))
      toast.error(getErrorMessage(removeError))
    }
  }

  const isBusy = setCover.isPending || deletePostMedia.isPending

  return (
    <div className="card card-body" style={{ marginBottom: 16 }}>
      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: 12 }}>
        <h2 style={{ margin: 0, fontSize: '1.05rem' }}>Media bài viết</h2>
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
              Chọn media
            </button>
          )}
        </div>
      </div>

      {actionError && <div className="alert alert-error">{actionError}</div>}

      {isLoading && <LoadingState message="Đang tải media bài viết..." />}
      {isError && <ErrorState message={getErrorMessage(error)} onRetry={refetch} />}

      {!isLoading && !isError && (
        <>
          <div style={{ marginBottom: 16 }}>
            <div style={{ fontSize: '0.8rem', color: 'var(--color-text-muted)', marginBottom: 8 }}>
              Ảnh cover (Primary)
            </div>
            {coverAsset?.publicUrl && isImageMime(coverAsset.mimeType) ? (
              <div style={{ maxWidth: 320 }}>
                <img
                  src={coverAsset.publicUrl}
                  alt={coverAsset.altText || coverAsset.fileName}
                  style={{ width: '100%', borderRadius: 8, border: '1px solid var(--color-border)' }}
                />
                <div style={{ marginTop: 8, display: 'flex', gap: 8, alignItems: 'center' }}>
                  <span style={{ fontSize: '0.85rem' }}>
                    {coverAsset.originalFileName || coverAsset.fileName}
                  </span>
                  {coverLink && canManageMedia && (
                    <button
                      type="button"
                      className="btn btn-danger btn-sm"
                      onClick={() => handleRemoveLink(coverLink)}
                      disabled={isBusy}
                    >
                      Gỡ cover
                    </button>
                  )}
                </div>
              </div>
            ) : (
              <p style={{ margin: 0, color: 'var(--color-text-muted)', fontSize: '0.9rem' }}>
                Chưa có ảnh cover — nhấn &quot;Chọn media&quot; hoặc chờ AI sinh ảnh.
              </p>
            )}
          </div>

          {otherMedia.length > 0 && (
            <div>
              <div style={{ fontSize: '0.8rem', color: 'var(--color-text-muted)', marginBottom: 8 }}>
                Media khác
              </div>
              <ul style={{ margin: 0, padding: 0, listStyle: 'none' }}>
                {otherMedia.map((link) => {
                  const asset = assetMap[link.mediaId]
                  return (
                    <li
                      key={link.id}
                      style={{
                        display: 'flex',
                        alignItems: 'center',
                        justifyContent: 'space-between',
                        padding: '8px 0',
                        borderBottom: '1px solid var(--color-border)',
                      }}
                    >
                      <div style={{ display: 'flex', alignItems: 'center', gap: 10 }}>
                        {asset?.publicUrl && isImageMime(asset.mimeType) && (
                          <img
                            src={asset.publicUrl}
                            alt=""
                            style={{ width: 48, height: 48, objectFit: 'cover', borderRadius: 6 }}
                          />
                        )}
                        <div>
                          <div style={{ fontWeight: 600, fontSize: '0.9rem' }}>
                            {asset?.originalFileName || asset?.fileName || link.mediaId}
                          </div>
                          <StatusBadge
                            label={getMediaRoleLabel(link.mediaRole)}
                            tone="neutral"
                          />
                        </div>
                      </div>
                      {canManageMedia && (
                        <button
                          type="button"
                          className="btn btn-danger btn-sm"
                          onClick={() => handleRemoveLink(link)}
                          disabled={isBusy}
                        >
                          Gỡ
                        </button>
                      )}
                    </li>
                  )
                })}
              </ul>
            </div>
          )}
        </>
      )}

      <MediaPickerModal
        open={pickerOpen}
        onClose={() => setPickerOpen(false)}
        onSelect={handleSelectCover}
        isSelecting={setCover.isPending}
      />
    </div>
  )
}
