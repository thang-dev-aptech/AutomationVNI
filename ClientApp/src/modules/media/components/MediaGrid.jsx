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
  onView,
  onDetails,
  onDelete,
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
          onView={onView}
          onDetails={onDetails}
          onDelete={onDelete}
          canManage={canManage}
        />
      ))}
    </div>
  )
}
