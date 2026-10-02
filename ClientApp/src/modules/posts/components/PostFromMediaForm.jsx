import { useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { getErrorMessage } from '@/shared/utils/apiHelpers'
import { toast } from '@/shared/stores/toastStore'
import ChannelMultiSelect from '@/shared/components/ChannelMultiSelect'
import MediaFolderPickerModal from '@/modules/media/components/MediaFolderPickerModal'
import { useGenerateCaption } from '@/modules/media/hooks/useMediaAssets'
import { useWritableMediaFolderPages } from '@/modules/media/hooks/useMediaFolders'
import { useCreatePostFromMedia } from '../hooks/usePosts'
import './PostFromMediaForm.css'

export default function PostFromMediaForm() {
  const navigate = useNavigate()
  const [assets, setAssets] = useState([])
  const [pickerOpen, setPickerOpen] = useState(false)
  const [caption, setCaption] = useState('')
  const [captionEdited, setCaptionEdited] = useState(false)
  const [pageIds, setPageIds] = useState([])
  const [errorMessage, setErrorMessage] = useState('')
  const pagesQuery = useWritableMediaFolderPages()
  const pages = pagesQuery.data ?? []
  const createMutation = useCreatePostFromMedia()
  const captionMutation = useGenerateCaption()

  const cover = assets[0]
  const canSubmit = assets.length > 0 && caption.trim().length > 0 && pageIds.length > 0
    && !createMutation.isPending

  const applySelection = (selected) => {
    setAssets(selected)
    setPickerOpen(false)
    const firstCaption = selected[0]?.caption ?? ''
    if (!captionEdited && firstCaption && caption.trim().length === 0) {
      setCaption(firstCaption)
    }
  }

  const removeAsset = (id) => {
    setAssets((prev) => prev.filter((asset) => asset.id !== id))
  }

  const suggestCaption = async () => {
    if (!cover) return
    const typed = caption
    try {
      const result = await captionMutation.mutateAsync(cover.id)
      const next = result?.caption ?? ''
      if (next) setCaption(next)
      toast.success('Đã gợi ý caption. Caption này cũng được lưu vào ảnh.')
    } catch (error) {
      setCaption(typed)
      toast.error(getErrorMessage(error, 'Không gợi ý được caption'))
    }
  }

  const submit = async (event) => {
    event.preventDefault()
    if (!canSubmit) return
    setErrorMessage('')
    try {
      const created = await createMutation.mutateAsync({
        mediaIds: assets.map((asset) => asset.id),
        socialChannelIds: pageIds,
        content: caption.trim(),
      })
      toast.success('Đã tạo bài từ ảnh đã chọn')
      if (pageIds.length > 1) {
        navigate(`/bulk/${created?.batchId}`)
        return
      }
      navigate(`/posts/${created?.id}`)
    } catch (error) {
      setErrorMessage(getErrorMessage(error))
    }
  }

  return (
    <form className="post-from-media" onSubmit={submit}>
      {errorMessage && <div className="alert alert-error">{errorMessage}</div>}

      <div className="form-group">
        <button type="button" className="btn btn-secondary" onClick={() => setPickerOpen(true)}>
          Chọn ảnh từ Media
        </button>
        {assets.length > 0 && (
          <ul className="post-from-media-thumbs">
            {assets.map((asset, index) => (
              <li key={asset.id}>
                <img src={asset.publicUrl} alt={asset.originalFileName || asset.fileName} />
                {index === 0 && <span>Ảnh bìa</span>}
                <button type="button" className="btn btn-ghost btn-sm" onClick={() => removeAsset(asset.id)}>
                  Bỏ ảnh
                </button>
              </li>
            ))}
          </ul>
        )}
      </div>

      <div className="form-group">
        <label htmlFor="post-from-media-caption">Caption</label>
        <textarea
          id="post-from-media-caption"
          value={caption}
          onChange={(event) => {
            setCaptionEdited(true)
            setCaption(event.target.value)
          }}
          rows={5}
        />
        <button
          type="button"
          className="btn btn-ghost"
          disabled={!cover || captionMutation.isPending}
          onClick={suggestCaption}
        >
          ✨ Gợi ý caption
        </button>
        <p className="post-from-media-note">
          Gợi ý caption cũng lưu caption vào ảnh. Kiểm tra nội dung trước khi đăng.
        </p>
      </div>

      <div className="form-group">
        {/* Nguồn Page là writable-pages (đã lọc quyền), không phải /api/SocialChannel. */}
        <ChannelMultiSelect
          label="Kênh đăng *"
          placeholder="Chọn page"
          channels={pages}
          value={pageIds}
          onChange={setPageIds}
          maxHeight={280}
        />
        {pagesQuery.isLoading && <p className="post-from-media-note">Đang tải Page...</p>}
        {pagesQuery.isError && (
          <p className="post-from-media-note">{getErrorMessage(pagesQuery.error, 'Không tải được Page')}</p>
        )}
        {!pagesQuery.isLoading && !pagesQuery.isError && pages.length === 0 && (
          <p className="post-from-media-note">Chưa có Page nào bạn được phép đăng bài.</p>
        )}
      </div>

      <button type="submit" className="btn btn-primary" disabled={!canSubmit}>
        Tạo bài
      </button>

      <MediaFolderPickerModal
        open={pickerOpen}
        onClose={() => setPickerOpen(false)}
        onConfirm={applySelection}
        initialSelected={assets}
      />
    </form>
  )
}
