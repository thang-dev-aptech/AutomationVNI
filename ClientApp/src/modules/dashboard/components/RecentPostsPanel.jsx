import { Link } from 'react-router-dom'
import EmptyState from '@/shared/components/EmptyState'
import { formatDateTime } from '@/shared/utils/apiHelpers'
import PostStatusBadge from '@/modules/posts/components/PostStatusBadge'
import DashboardSection from './DashboardSection'
import './DashboardComponents.css'

export default function RecentPostsPanel({
  posts = [],
  title = 'Bài viết gần đây',
  description,
  showOwnerHint = false,
  myRecentCount = 0,
}) {
  const sectionDescription =
    description ||
    (showOwnerHint && myRecentCount > 0
      ? `${myRecentCount} bài của bạn trong nhóm bài gần đây`
      : '5 bài mới nhất trong hệ thống')

  return (
    <DashboardSection
      title={title}
      description={sectionDescription}
      action={(
        <Link to="/posts" className="btn btn-ghost btn-sm">
          Xem tất cả
        </Link>
      )}
    >
      {posts.length === 0 ? (
        <EmptyState message="Chưa có dữ liệu bài viết" />
      ) : (
        <div className="dashboard-table-wrap">
          <table>
            <thead>
              <tr>
                <th>Tiêu đề</th>
                <th>Trạng thái</th>
                <th>Ngày tạo</th>
                <th />
              </tr>
            </thead>
            <tbody>
              {posts.map((post) => (
                <tr key={post.id}>
                  <td className="dashboard-table-title">{post.title}</td>
                  <td><PostStatusBadge status={post.status} /></td>
                  <td>{formatDateTime(post.createdAt)}</td>
                  <td>
                    <Link to={`/posts/${post.id}`} className="btn btn-ghost btn-sm">
                      Chi tiết
                    </Link>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </DashboardSection>
  )
}
