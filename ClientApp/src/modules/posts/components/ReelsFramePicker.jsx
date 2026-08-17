import { useEffect, useMemo, useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { unwrapApiData, getErrorMessage } from '@/shared/utils/apiHelpers'
import { toast } from '@/shared/stores/toastStore'
import { mediaFolderApi } from '@/modules/media/services/mediaFolderApi'
import { mediaAssetApi } from '@/modules/media/services/mediaAssetApi'
import { postMediaApi, postMediaQueryKeys } from '@/modules/media/services/postMediaApi'
import { useMusicTrackAll } from '@/modules/music/hooks/useMusicTracks'
import { useSetReelsFrames, useConvertToReels } from '../hooks/usePosts'

const COVER_ROLE = 4
const ATTACHMENT_ROLE = 3
const TEMPLATE_SOURCE_ROLE = 5

/**
 * Đăng Reels giờ là lựa chọn ở bước ĐĂNG (không phải flow riêng lúc tạo bài) — áp dụng cho MỌI
 * post đã có ảnh, dù sinh bằng FullAI (1 ảnh AI) hay Template (1 hoặc nhiều ảnh). Mặc định dùng
 * đúng ảnh bài đang có (Cover/Attachment) để "biến ảnh đang có thành video"; nếu page có thư mục
 * Template, người dùng có thể chọn thêm/bớt/sắp xếp trước khi ghép — ảnh mới thêm sẽ tự được ghép
 * chữ cho nhất quán với các khung hình còn lại.
 */
export default function ReelsFramePicker({ post }) {
  const socialChannelId = post.socialChannelId
  const postId = post.id

  const currentQuery = useQuery({
    queryKey: postMediaQueryKeys.byPost(postId),
    queryFn: async () => unwrapApiData(await postMediaApi.getByPost(postId)),
    enabled: Boolean(postId),
  })

  const folderQuery = useQuery({
    queryKey: ['media-folders', 'by-channel', socialChannelId],
    queryFn: async () => unwrapApiData(await mediaFolderApi.filter({
      socialChannelId, index: 1, size: 1,
    })),
    enabled: Boolean(socialChannelId),
  })
  const folder = folderQuery.data?.items?.[0]

  const assetsQuery = useQuery({
    queryKey: ['media-assets', 'by-folder', folder?.id],
    queryFn: async () => unwrapApiData(await mediaAssetApi.filter({
      folderId: folder.id, index: 1, size: 200,
    })),
    enabled: Boolean(folder?.id),
  })
  const folderImages = useMemo(
    () => (assetsQuery.data?.items ?? []).filter((m) => m.mimeType?.startsWith('image/')),
    [assetsQuery.data],
  )

  // Ảnh bài đang có (đã ghép chữ) — nguồn "asset" cho hiển thị, kể cả khi không nằm trong thư mục
  // Template (vd ảnh AI của flow FullAI).
  const currentAssets = useMemo(() => {
    const rows = (currentQuery.data ?? [])
      .filter((pm) => pm.mediaRole === COVER_ROLE || pm.mediaRole === ATTACHMENT_ROLE)
      .sort((a, b) => a.sortOrder - b.sortOrder)
    return rows.map((pm) => ({
      id: pm.mediaId,
      previewUrl: pm.previewUrl,
      publicUrl: pm.publicUrl,
      altText: pm.altText,
      originalFileName: pm.originalFileName,
      mimeType: pm.mimeType,
    }))
  }, [currentQuery.data])

  const assetById = useMemo(() => {
    const map = new Map()
    for (const a of currentAssets) map.set(a.id, a)
    for (const a of folderImages) map.set(a.id, a)
    return map
  }, [currentAssets, folderImages])

  // Bài đã đăng dạng Reels trước đó thì currentAssets chỉ còn đúng 1 video — không dùng được làm
  // khung hình (FFmpeg cần ảnh), nên tách riêng ra để hiển thị indicator + loại khỏi lựa chọn mặc định.
  const videoAsset = useMemo(
    () => currentAssets.find((a) => a.mimeType?.startsWith('video/')),
    [currentAssets],
  )
  const imageCurrentAssets = useMemo(
    () => currentAssets.filter((a) => !a.mimeType?.startsWith('video/')),
    [currentAssets],
  )

  const [selected, setSelected] = useState([])
  const [initialized, setInitialized] = useState(false)
  const [musicTrackId, setMusicTrackId] = useState('')

  const { data: musicTracks = [] } = useMusicTrackAll()

  useEffect(() => {
    if (initialized || !currentQuery.data) return
    // Đã có lần chỉnh tay trước (TemplateSource) → dùng lại đúng lựa chọn đó; không thì mặc định
    // dùng đúng ảnh bài đang có (trừ video, nếu đã lỡ đăng Reels trước đó), không cần chỉnh gì cả.
    const templateSource = currentQuery.data
      .filter((pm) => pm.mediaRole === TEMPLATE_SOURCE_ROLE)
      .sort((a, b) => a.sortOrder - b.sortOrder)
      .map((pm) => pm.mediaId)
    setSelected(templateSource.length > 0 ? templateSource : imageCurrentAssets.map((a) => a.id))
    setInitialized(true)
  }, [currentQuery.data, imageCurrentAssets, initialized])

  const setFrames = useSetReelsFrames()
  const convertToReels = useConvertToReels()

  // Chưa sinh xong bài (chưa có ảnh nào) — không có gì để chuyển thành Reels.
  if (!currentQuery.isLoading && currentAssets.length === 0) return null

  const toggle = (mediaId) => {
    setSelected((prev) => (
      prev.includes(mediaId) ? prev.filter((id) => id !== mediaId) : [...prev, mediaId]
    ))
  }

  const move = (index, delta) => {
    setSelected((prev) => {
      const next = [...prev]
      const target = index + delta
      if (target < 0 || target >= next.length) return prev
      ;[next[index], next[target]] = [next[target], next[index]]
      return next
    })
  }

  const selectedAssets = selected.map((id) => assetById.get(id)).filter(Boolean)

  const handleSave = async () => {
    if (selected.length === 0) {
      toast.error('Cần chọn ít nhất 1 khung hình')
      return
    }
    try {
      await setFrames.mutateAsync({ id: postId, mediaIds: selected })
      toast.success(`Đã lưu ${selected.length} khung hình`)
    } catch (error) {
      toast.error(getErrorMessage(error))
    }
  }

  const handleConvert = async () => {
    if (selected.length === 0) {
      toast.error('Cần chọn ít nhất 1 khung hình')
      return
    }
    try {
      await convertToReels.mutateAsync({ id: postId, mediaIds: selected, musicTrackId: musicTrackId || null })
      toast.success('Đã đăng dạng Reels')
    } catch (error) {
      toast.error(getErrorMessage(error))
    }
  }

  const isLoading = currentQuery.isLoading || folderQuery.isLoading || assetsQuery.isLoading
  const isBusy = setFrames.isPending || convertToReels.isPending

  return (
    <div className="card card-body" style={{ marginBottom: 16 }}>
      <h2 style={{ margin: '0 0 4px', fontSize: '1.05rem' }}>
        {videoAsset ? '🎬 Bài đăng dạng Reels' : '🎬 Muốn đăng Reels thay vì bài ảnh thường?'}
      </h2>
      <p style={{ margin: '0 0 12px', fontSize: '0.85rem', color: 'var(--color-text-muted)' }}>
        {videoAsset
          ? 'Bài đã được dựng thành video — đăng lên sẽ ra Facebook Reels, không phải bài ảnh. Muốn đổi khung hình khác thì chọn ảnh mới từ thư mục bên dưới rồi dựng lại.'
          : 'Mặc định dùng đúng ảnh bài đang có, ghép thành video ngắn rồi đăng Facebook Reels. Có thể chọn thêm/bớt/sắp xếp lại trước khi ghép nếu muốn.'}
      </p>

      {videoAsset && (
        <div
          style={{
            display: 'flex', alignItems: 'center', gap: 10, marginBottom: 12,
            padding: '8px 12px', borderRadius: 8, background: 'rgba(124, 58, 237, 0.12)',
            border: '1px solid rgba(124, 58, 237, 0.4)', fontSize: '0.85rem',
          }}
        >
          <video
            src={videoAsset.previewUrl || videoAsset.publicUrl}
            muted
            style={{ width: 56, height: 56, objectFit: 'cover', borderRadius: 6, background: '#000' }}
          />
          <span>
            <strong style={{ color: '#7c3aed' }}>REELS</strong> — đây là bài dạng video, không phải bài ảnh thường.
          </span>
        </div>
      )}

      {isLoading && <p style={{ fontSize: '0.85rem' }}>Đang tải...</p>}

      {!isLoading && selectedAssets.length > 0 && (
        <div style={{ marginBottom: 12 }}>
          <strong style={{ fontSize: '0.85rem' }}>Thứ tự khung hình ({selectedAssets.length}):</strong>
          <div style={{ display: 'flex', flexWrap: 'wrap', gap: 8, marginTop: 8 }}>
            {selectedAssets.map((asset, index) => (
              <div
                key={asset.id}
                style={{ border: '1px solid var(--color-border, #e5e7eb)', borderRadius: 6, padding: 4, width: 100 }}
              >
                <img
                  src={asset.previewUrl || asset.publicUrl}
                  alt={asset.altText || asset.originalFileName}
                  style={{ width: '100%', height: 70, objectFit: 'cover', borderRadius: 4 }}
                />
                <div style={{ display: 'flex', justifyContent: 'space-between', marginTop: 4, fontSize: '0.72rem' }}>
                  <button type="button" className="btn btn-ghost" style={{ padding: '0 4px' }} disabled={index === 0} onClick={() => move(index, -1)}>↑</button>
                  <span>{index + 1}</span>
                  <button type="button" className="btn btn-ghost" style={{ padding: '0 4px' }} disabled={index === selectedAssets.length - 1} onClick={() => move(index, 1)}>↓</button>
                </div>
              </div>
            ))}
          </div>
        </div>
      )}

      {!isLoading && folderImages.length > 0 && (
        <div>
          <strong style={{ fontSize: '0.85rem' }}>Chọn thêm từ thư mục Template ({folderImages.length} ảnh):</strong>
          <div style={{ display: 'flex', flexWrap: 'wrap', gap: 8, marginTop: 8 }}>
            {folderImages.map((asset) => {
              const isSelected = selected.includes(asset.id)
              return (
                <label
                  key={asset.id}
                  style={{
                    display: 'block',
                    border: isSelected ? '2px solid var(--color-primary, #2563eb)' : '1px solid var(--color-border, #e5e7eb)',
                    borderRadius: 6,
                    padding: 4,
                    width: 90,
                    cursor: 'pointer',
                  }}
                >
                  <input
                    type="checkbox"
                    checked={isSelected}
                    onChange={() => toggle(asset.id)}
                    style={{ marginBottom: 4 }}
                  />
                  <img
                    src={asset.previewUrl || asset.publicUrl}
                    alt={asset.altText || asset.originalFileName}
                    style={{ width: '100%', height: 60, objectFit: 'cover', borderRadius: 4 }}
                  />
                </label>
              )
            })}
          </div>
          <p style={{ margin: '6px 0 0', fontSize: '0.78rem', color: 'var(--text-muted, #888)' }}>
            Ảnh mới thêm từ đây chưa có chữ — hệ thống sẽ tự ghép chữ giống các khung hình khác trước khi dựng video.
          </p>
        </div>
      )}

      <div className="form-group" style={{ marginTop: 12, marginBottom: 0, maxWidth: 320 }}>
        <label htmlFor="reels-music-track">Nhạc nền</label>
        <select
          id="reels-music-track"
          value={musicTrackId}
          onChange={(event) => setMusicTrackId(event.target.value)}
        >
          <option value="">Nhạc mặc định hệ thống</option>
          {musicTracks.map((track) => (
            <option key={track.id} value={track.id}>{track.displayName}</option>
          ))}
        </select>
      </div>

      <div style={{ display: 'flex', gap: 8, marginTop: 12 }}>
        <button type="button" className="btn btn-secondary" disabled={isBusy} onClick={handleSave}>
          {setFrames.isPending ? 'Đang lưu...' : 'Lưu khung hình'}
        </button>
        <button type="button" className="btn btn-primary" disabled={isBusy} onClick={handleConvert}>
          {convertToReels.isPending ? 'Đang dựng video...' : (videoAsset ? '🎬 Dựng lại Reels' : '🎬 Đăng dạng Reels')}
        </button>
      </div>
    </div>
  )
}
