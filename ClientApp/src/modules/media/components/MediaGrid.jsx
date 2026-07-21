import LoadingState from '@/shared/components/LoadingState'
import ErrorState from '@/shared/components/ErrorState'
import EmptyState from '@/shared/components/EmptyState'
import { getErrorMessage } from '@/shared/utils/apiHelpers'
import MediaAssetCard from './MediaAssetCard'

export default function MediaGrid({
  items,
  isLoading,
  isError,
  error,
  onRetry,
  onEdit,
  onDelete,
  onAnalyze,
  analyzingId = null,
  canManage = false,
  emptyMessage = 'Chưa có media nào',
}) {
  if (isLoading) return <LoadingState />
  if (isError) return <ErrorState message={getErrorMessage(error)} onRetry={onRetry} />
  if (items.length === 0) return <EmptyState message={emptyMessage} />

  return (
    <div className="media-grid">
      {items.map((asset) => (
        <MediaAssetCard
          key={asset.id}
          asset={asset}
          onEdit={onEdit}
          onDelete={onDelete}
          onAnalyze={onAnalyze}
          isAnalyzing={analyzingId === asset.id}
          canManage={canManage}
        />
      ))}
    </div>
  )
}
