import { useMemo, useState } from 'react'
import Modal from '@/shared/components/Modal'
import LoadingState from '@/shared/components/LoadingState'
import ErrorState from '@/shared/components/ErrorState'
import EmptyState from '@/shared/components/EmptyState'
import { getErrorMessage } from '@/shared/utils/apiHelpers'
import { isImageMime } from '../constants/mediaConstants'
import { useMediaAssets } from '../hooks/useMediaAssets'
import './MediaAssetCard.css'

export default function MediaPickerModal({
  open,
  onClose,
  onSelect,
  isSelecting,
}) {
  const [keyword, setKeyword] = useState('')

  const params = useMemo(
    () => ({ keyword, index: 1, size: 60, mimeType: 'image' }),
    [keyword],
  )

  const { data, isLoading, isError, error, refetch } = useMediaAssets(params)
  const items = (data?.items ?? []).filter(
    (asset) => asset.publicUrl && isImageMime(asset.mimeType),
  )

  const handleSelect = (asset) => {
    onSelect(asset)
  }

  return (
    <Modal
      open={open}
      title="Chọn media"
      onClose={onClose}
      footer={(
        <button type="button" className="btn btn-secondary" onClick={onClose}>
          Đóng
        </button>
      )}
    >
      <div className="form-group">
        <label htmlFor="picker-keyword">Tìm kiếm</label>
        <input
          id="picker-keyword"
          value={keyword}
          onChange={(event) => setKeyword(event.target.value)}
          placeholder="Tên file, alt text..."
        />
      </div>

      {isLoading && <LoadingState message="Đang tải media..." />}
      {isError && <ErrorState message={getErrorMessage(error)} onRetry={refetch} />}
      {!isLoading && !isError && items.length === 0 && (
        <EmptyState message="Không có ảnh nào trong kho media" />
      )}
      {!isLoading && !isError && items.length > 0 && (
        <div className="media-picker-grid">
          {items.map((asset) => (
            <button
              key={asset.id}
              type="button"
              className="media-picker-item"
              disabled={isSelecting}
              onClick={() => handleSelect(asset)}
            >
              <img
                src={asset.publicUrl}
                alt={asset.altText || asset.originalFileName || asset.fileName}
              />
              <div className="media-picker-item-label">
                {asset.originalFileName || asset.fileName}
              </div>
            </button>
          ))}
        </div>
      )}
    </Modal>
  )
}
