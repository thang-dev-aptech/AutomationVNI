import { useCallback, useMemo, useState } from 'react'
import { toast } from '@/shared/stores/toastStore'
import { isImageMime } from '../constants/mediaConstants'
import { MEDIA_POST_SELECTION_LIMIT } from '../constants.js'

const toImageMap = (items) => new Map(
  (items ?? [])
    .filter((asset) => asset?.id && isImageMime(asset.mimeType))
    .map((asset) => [asset.id, asset]),
)

export function useMediaSelection({
  limit = MEDIA_POST_SELECTION_LIMIT,
  initial = [],
} = {}) {
  const [selected, setSelected] = useState(() => toImageMap(initial))

  const toggle = useCallback((asset) => {
    if (!asset?.id || !isImageMime(asset.mimeType)) return
    setSelected((prev) => {
      if (prev.has(asset.id)) {
        const next = new Map(prev)
        next.delete(asset.id)
        return next
      }
      if (limit <= 1) return new Map([[asset.id, asset]])
      if (prev.size >= limit) {
        toast.warning(`Chỉ chọn tối đa ${limit} ảnh`)
        return prev
      }
      const next = new Map(prev)
      next.set(asset.id, asset)
      return next
    })
  }, [limit])

  const clear = useCallback(() => setSelected(new Map()), [])
  const reset = useCallback((items) => setSelected(toImageMap(items)), [])
  const isSelected = useCallback((id) => selected.has(id), [selected])
  const items = useMemo(() => [...selected.values()], [selected])

  return {
    items,
    count: items.length,
    limit,
    isSelected,
    toggle,
    clear,
    reset,
  }
}
