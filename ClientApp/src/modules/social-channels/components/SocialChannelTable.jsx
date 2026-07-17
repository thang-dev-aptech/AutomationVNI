import StatusBadge from '@/shared/components/StatusBadge'
import LoadingState from '@/shared/components/LoadingState'
import ErrorState from '@/shared/components/ErrorState'
import EmptyState from '@/shared/components/EmptyState'
import { formatDateTime, getErrorMessage } from '@/shared/utils/apiHelpers'
import { getSocialPlatformLabel } from '../constants/socialPlatform'

export default function SocialChannelTable({
  items,
  isLoading,
  isError,
  error,
  onRetry,
  onEdit,
  onDelete,
  canManage = false,
  emptyMessage = 'Chưa có kênh nào được kết nối',
}) {
  if (isLoading) {
    return <LoadingState />
  }

  if (isError) {
    return <ErrorState message={getErrorMessage(error)} onRetry={onRetry} />
  }

  if (items.length === 0) {
    return <EmptyState message={emptyMessage} />
  }

  return (
    <table>
      <thead>
        <tr>
          <th>Page</th>
          <th>External ID</th>
          <th>Nền tảng</th>
          <th>Token hết hạn</th>
          <th>Trạng thái</th>
          <th>Ngày tạo</th>
          {canManage && <th />}
        </tr>
      </thead>
      <tbody>
        {items.map((item) => (
          <tr key={item.id}>
            <td>{item.pageName}</td>
            <td>{item.externalPageId}</td>
            <td>{getSocialPlatformLabel(item.platform)}</td>
            <td>{formatDateTime(item.tokenExpiresAt)}</td>
            <td>
              <StatusBadge
                label={item.isActive ? 'Hoạt động' : 'Tắt'}
                tone={item.isActive ? 'success' : 'neutral'}
              />
            </td>
            <td>{formatDateTime(item.createdAt)}</td>
            {canManage && (
              <td>
                <div className="table-actions">
                  <button
                    type="button"
                    className="btn btn-ghost"
                    onClick={() => onEdit(item)}
                  >
                    Sửa
                  </button>
                  <button
                    type="button"
                    className="btn btn-danger"
                    onClick={() => onDelete(item)}
                  >
                    Xóa
                  </button>
                </div>
              </td>
            )}
          </tr>
        ))}
      </tbody>
    </table>
  )
}
