import { Link } from 'react-router-dom'
import PageHeader from '@/shared/components/PageHeader'
import LoadingState from '@/shared/components/LoadingState'
import ErrorState from '@/shared/components/ErrorState'
import { usePermissions } from '@/shared/hooks/usePermissions'
import { useAuthStore } from '@/modules/auth/stores/authStore'
import { getErrorMessage } from '@/shared/utils/apiHelpers'
import { hasRole, ROLES } from '@/shared/auth/permissions'
import { useDashboardStats } from '../hooks/useDashboardStats'
import StatCard from '../components/StatCard'
import RecentPostsPanel from '../components/RecentPostsPanel'
import JobHealthPanel from '../components/JobHealthPanel'
import ChannelHealthPanel from '../components/ChannelHealthPanel'
import { buildStatCards, DASHBOARD_QUICK_LINKS } from '../utils/dashboardLayout'
import './DashboardPage.css'

export default function DashboardPage() {
  const currentUser = useAuthStore((state) => state.currentUser)
  const roles = useAuthStore((state) => state.roles)
  const permissions = usePermissions()
  const { data: stats, isLoading, isError, error, refetch, isFetching } = useDashboardStats()

  const statCards = stats ? buildStatCards(stats, roles) : []
  const visibleLinks = DASHBOARD_QUICK_LINKS.filter((item) => item.visible(permissions))
  const isContentManager = hasRole(roles, [ROLES.CONTENT_MANAGER])

  return (
    <section className="dashboard-page">
      <PageHeader
        title="Tổng quan"
        description="VNI Automation — dashboard vận hành nội dung AI"
        actions={
          !isLoading ? (
            <button
              type="button"
              className="btn btn-secondary btn-sm"
              onClick={() => refetch()}
              disabled={isFetching}
            >
              {isFetching ? 'Đang làm mới...' : 'Làm mới'}
            </button>
          ) : null
        }
      />

      <div className="card card-body dashboard-welcome">
        <p style={{ margin: '0 0 8px' }}>
          Xin chào, <strong>{currentUser?.email || 'user'}</strong>
        </p>
        {roles.length > 0 && (
          <p style={{ margin: 0, color: 'var(--color-text-muted)', fontSize: '0.9rem' }}>
            Vai trò: {roles.join(', ')}
          </p>
        )}
      </div>

      {isLoading && <LoadingState message="Đang tải thống kê dashboard..." />}

      {isError && (
        <ErrorState
          message={getErrorMessage(error, 'Không thể tải dashboard')}
          onRetry={refetch}
        />
      )}

      {!isLoading && !isError && stats && (
        <>
          {stats.partialErrors?.length > 0 && (
            <p className="dashboard-partial-note">
              Một số nguồn dữ liệu chưa sẵn sàng ({stats.partialErrors.join(', ')}).
              Các mục khác vẫn hiển thị bình thường.
            </p>
          )}

          <div className="dashboard-stats-grid">
            {statCards.map((card) => (
              <StatCard
                key={card.id}
                label={card.label}
                value={card.value}
                hint={card.hint}
                tone={card.tone}
                to={card.to}
                emphasized={card.emphasized}
              />
            ))}
          </div>

          <div className="dashboard-panels">
            <RecentPostsPanel
              posts={stats.posts?.recent ?? []}
              title={isContentManager ? 'Bài viết gần đây (ưu tiên của bạn)' : 'Bài viết gần đây'}
              showOwnerHint={isContentManager}
              myRecentCount={stats.posts?.myRecentCount ?? 0}
            />

            <div className="dashboard-panels">
              <JobHealthPanel
                jobs={stats.jobs}
                publishLogs={stats.publishLogs}
                canViewJobs={permissions.canViewJobs}
              />
              <ChannelHealthPanel
                channels={stats.channels}
                canViewPlatforms={permissions.canViewPlatforms}
              />
            </div>
          </div>

          {visibleLinks.length > 0 && (
            <div>
              <h2 style={{ margin: '0 0 12px', fontSize: '1rem' }}>Truy cập nhanh</h2>
              <div className="dashboard-quick-links">
                {visibleLinks.map((item) => (
                  <Link
                    key={item.to}
                    to={item.to}
                    className="card dashboard-quick-link"
                  >
                    <div className="dashboard-quick-link-title">{item.label}</div>
                    <div className="dashboard-quick-link-desc">{item.desc}</div>
                  </Link>
                ))}
              </div>
            </div>
          )}
        </>
      )}
    </section>
  )
}
