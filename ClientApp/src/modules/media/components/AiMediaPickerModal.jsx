import { useEffect, useMemo, useState } from 'react'
import Modal from '@/shared/components/Modal'
import LoadingState from '@/shared/components/LoadingState'
import ErrorState from '@/shared/components/ErrorState'
import EmptyState from '@/shared/components/EmptyState'
import { getErrorMessage } from '@/shared/utils/apiHelpers'
import { isImageMime } from '../constants/mediaConstants'
import { useMediaAssets, useMediaRecommendation } from '../hooks/useMediaAssets'
import './MediaAssetCard.css'

/**
 * Modal chọn nhiều ảnh từ kho media với 2 chế độ:
 * - "AI gợi ý": gọi /recommend theo ý tưởng bài viết, xếp hạng theo keyword đã gắn nhãn.
 * - "Tất cả": duyệt/tìm toàn bộ kho.
 * Trả về danh sách asset đã chọn qua onConfirm(assets).
 */
export default function AiMediaPickerModal({
  open,
  onClose,
  onConfirm,
  query = '',
  initialSelected = [],
}) {
  const [mode, setMode] = useState('ai')
  const [searchText, setSearchText] = useState(query)
  const [keyword, setKeyword] = useState('')
  const [selected, setSelected] = useState(new Map())

  useEffect(() => {
    if (!open) return
    setSearchText(query)
    setMode(query.trim() ? 'ai' : 'all')
    setSelected(new Map(initialSelected.map((asset) => [asset.id, asset])))
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open])

  const recommendPayload = useMemo(
    () => ({ query: searchText.trim(), limit: 24 }),
    [searchText],
  )
  const {
    data: recommendData,
    isLoading: recommendLoading,
    isError: recommendError,
    error: recommendErrorObj,
    refetch: refetchRecommend,
  } = useMediaRecommendation(recommendPayload, { enabled: open && mode === 'ai' })

  const listParams = useMemo(
    () => ({ keyword, index: 1, size: 60, mimeType: 'image' }),
    [keyword],
  )
  const {
    data: listData,
    isLoading: listLoading,
    isError: listError,
    error: listErrorObj,
    refetch: refetchList,
  } = useMediaAssets(listParams)

  const isAi = mode === 'ai'
  const isLoading = isAi ? recommendLoading : listLoading
  const isError = isAi ? recommendError : listError
  const errorObj = isAi ? recommendErrorObj : listErrorObj
  const refetch = isAi ? refetchRecommend : refetchList

  const items = useMemo(() => {
    if (isAi) {
      return (recommendData?.items ?? [])
        .filter((item) => item.media?.publicUrl && isImageMime(item.media.mimeType))
        .map((item) => ({
          asset: item.media,
          score: item.score,
          matchedKeywords: item.matchedKeywords ?? [],
        }))
    }
    return (listData?.items ?? [])
      .filter((asset) => asset.publicUrl && isImageMime(asset.mimeType))
      .map((asset) => ({ asset, score: null, matchedKeywords: [] }))
  }, [isAi, recommendData, listData])

  const toggle = (asset) => {
    setSelected((prev) => {
      const next = new Map(prev)
      if (next.has(asset.id)) next.delete(asset.id)
      else next.set(asset.id, asset)
      return next
    })
  }

  const handleConfirm = () => {
    onConfirm([...selected.values()])
    onClose()
  }

  return (
    <Modal
      open={open}
      title="Chọn media phù hợp"
      onClose={onClose}
      footer={(
        <>
          <button type="button" className="btn btn-secondary" onClick={onClose}>
            Hủy
          </button>
          <button
            type="button"
            className="btn btn-primary"
            disabled={selected.size === 0}
            onClick={handleConfirm}
          >
            Dùng {selected.size} ảnh đã chọn
          </button>
        </>
      )}
    >
      <div className="ai-media-picker-tabs">
        <button
          type="button"
          className={`btn btn-sm ${isAi ? 'btn-primary' : 'btn-secondary'}`}
          onClick={() => setMode('ai')}
        >
          ✨ AI gợi ý theo nội dung
        </button>
        <button
          type="button"
          className={`btn btn-sm ${!isAi ? 'btn-primary' : 'btn-secondary'}`}
          onClick={() => setMode('all')}
        >
          Tất cả media
        </button>
      </div>

      {isAi ? (
        <div className="form-group">
          <label htmlFor="ai-picker-query">Nội dung / ý tưởng để AI lọc ảnh</label>
          <textarea
            id="ai-picker-query"
            rows={2}
            value={searchText}
            onChange={(event) => setSearchText(event.target.value)}
            placeholder="Ví dụ: Khuyến mãi hè áo thun nam trẻ trung năng động"
          />
          {(recommendData?.queryKeywords?.length ?? 0) > 0 && (
            <div className="ai-media-keyword-row">
              <span className="ai-media-keyword-label">Keyword AI:</span>
              {recommendData.queryKeywords.map((kw) => (
                <span key={kw} className="ai-media-keyword-chip">{kw}</span>
              ))}
            </div>
          )}
        </div>
      ) : (
        <div className="form-group">
          <label htmlFor="ai-picker-keyword">Tìm kiếm</label>
          <input
            id="ai-picker-keyword"
            value={keyword}
            onChange={(event) => setKeyword(event.target.value)}
            placeholder="Tên file, alt text, keyword..."
          />
        </div>
      )}

      {isAi && !searchText.trim() && (
        <EmptyState message="Nhập nội dung/ý tưởng để AI gợi ý ảnh phù hợp" />
      )}
      {isLoading && <LoadingState message={isAi ? 'AI đang lọc ảnh phù hợp...' : 'Đang tải media...'} />}
      {isError && <ErrorState message={getErrorMessage(errorObj)} onRetry={refetch} />}
      {!isLoading && !isError && (!isAi || searchText.trim()) && items.length === 0 && (
        <EmptyState message="Không tìm thấy ảnh nào phù hợp trong kho media" />
      )}

      {!isLoading && !isError && items.length > 0 && (
        <div className="media-picker-grid">
          {items.map(({ asset, score, matchedKeywords }) => {
            const active = selected.has(asset.id)
            return (
              <button
                key={asset.id}
                type="button"
                className={`media-picker-item ${active ? 'is-selected' : ''}`}
                onClick={() => toggle(asset)}
              >
                <div className="media-picker-thumb">
                  <img
                    src={asset.publicUrl}
                    alt={asset.altText || asset.originalFileName || asset.fileName}
                    loading="lazy"
                  />
                  {active && <span className="media-picker-check">✓</span>}
                  {isAi && score !== null && score > 0 && (
                    <span className="media-picker-score">{Math.round(score * 100)}%</span>
                  )}
                </div>
                <div className="media-picker-item-label">
                  {asset.originalFileName || asset.fileName}
                </div>
                {matchedKeywords.length > 0 && (
                  <div className="media-picker-item-keywords">
                    {matchedKeywords.slice(0, 3).join(' · ')}
                  </div>
                )}
              </button>
            )
          })}
        </div>
      )}
    </Modal>
  )
}
