import { useState } from 'react'
import { Link } from 'react-router-dom'
import PageHeader from '@/shared/components/PageHeader'
import LoadingState from '@/shared/components/LoadingState'
import ErrorState from '@/shared/components/ErrorState'
import { usePermissions } from '@/shared/hooks/usePermissions'
import { getErrorMessage } from '@/shared/utils/apiHelpers'
import { hasRole, ROLES } from '@/shared/auth/permissions'
import { useDashboardStats } from '../hooks/useDashboardStats'
import { usePageMetrics, useSyncPageMetrics } from '../hooks/usePageMetrics'
import CustomerOverview from '../components/CustomerOverview'
import StatCard from '../components/StatCard'
import RecentPostsPanel from '../components/RecentPostsPanel'
import JobHealthPanel from '../components/JobHealthPanel'
import ChannelHealthPanel from '../components/ChannelHealthPanel'
import { buildStatCards, DASHBOARD_QUICK_LINKS } from '../utils/dashboardLayout'
import './DashboardPage.css'

/**
 * Trang này phục vụ HAI người khác nhau, nên chia làm hai tầng:
 *
 *   Tầng trên — kết quả:  page chạy thế nào, bài nào hiệu quả, còn việc gì phải làm.
 *   Tầng dưới — vận hành: hàng đợi job, dead-letter, log đăng lỗi. Gập lại mặc định.
 *
 * Trước đây chỉ có tầng dưới. Khách mở dashboard ra gặp "dead letter 0" và "độ phủ page-context
 * 12/18" thì không rút ra được điều gì về công việc của mình.
 *
 * Phần vận hành KHÔNG bị xoá — người trực hệ thống vẫn cần, và nó là chỗ đầu tiên nhìn khi có
 * bài không lên được.
 */
export default function DashboardPage() {
  const [days, setDays] = useState(30)
  const [showOps, setShowOps] = useState(false)
  const permissions = usePermissions()
  const { roles } = permissions

  const metrics = usePageMetrics(days)
  const syncMutation = useSyncPageMetrics()
  const ops = useDashboardStats()

  const isContentManager = hasRole(roles, [ROLES.CONTENT_MANAGER])
  const opsCards = ops.data ? buildStatCards(ops.data, roles) : []
  const visibleLinks = DASHBOARD_QUICK_LINKS.filter((item) => item.visible(permissions))

  return (
    <section className="dashboard-page">
      <PageHeader
        title="Tổng quan"
        description="Kết quả trên fanpage và website tin tức"
        actions={
          <button
            type="button"
            className="btn btn-secondary btn-sm"
            onClick={() => metrics.refetch()}
            disabled={metrics.isFetching}
          >
            {metrics.isFetching ? 'Đang tải...' : 'Làm mới'}
          </button>
        }
      />

      {metrics.isLoading && <LoadingState message="Đang tải số liệu fanpage..." />}

      {metrics.isError && (
        <ErrorState
          message={getErrorMessage(metrics.error, 'Không tải được số liệu fanpage')}
          onRetry={metrics.refetch}
        />
      )}

      {!metrics.isLoading && !metrics.isError && metrics.data && (
        <CustomerOverview
          data={metrics.data}
          days={days}
          onChangeDays={setDays}
          onSync={() => syncMutation.mutate()}
          syncing={syncMutation.isPending}
        />
      )}

      {syncMutation.isSuccess && (
        <p className="dashboard-partial-note">
          Đang lấy số mới từ Facebook — mất khoảng một phút rưỡi cho 15 page. Số trên trang sẽ tự
          cập nhật, không cần tải lại.
        </p>
      )}

      <div className="dashboard-ops">
        <button
          type="button"
          className="dashboard-ops-toggle"
          onClick={() => setShowOps((v) => !v)}
          aria-expanded={showOps}
        >
          <span aria-hidden="true">{showOps ? '▾' : '▸'}</span>
          Chi tiết vận hành hệ thống
          <span className="dashboard-ops-hint">hàng đợi, lỗi đăng bài, sức khoẻ kênh</span>
        </button>

        {showOps && (
          <div className="dashboard-ops-body">
            {ops.isLoading && <LoadingState message="Đang tải thống kê vận hành..." />}
            {ops.isError && (
              <ErrorState
                message={getErrorMessage(ops.error, 'Không tải được thống kê vận hành')}
                onRetry={ops.refetch}
              />
            )}
            {!ops.isLoading && !ops.isError && ops.data && (
              <>
                <div className="dashboard-stats-grid">
                  {opsCards.map((card) => (
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

                <div className="dashboard-layout-grid">
                  <RecentPostsPanel
                    posts={ops.data.posts?.recent ?? []}
                    title={isContentManager ? 'Bài viết gần đây (ưu tiên của bạn)' : 'Bài viết gần đây'}
                    showOwnerHint={isContentManager}
                    myRecentCount={ops.data.posts?.myRecentCount ?? 0}
                  />
                  <div className="dashboard-side-panels">
                    <JobHealthPanel
                      jobs={ops.data.jobs}
                      publishLogs={ops.data.publishLogs}
                      canViewJobs={permissions.canViewJobs}
                    />
                    <ChannelHealthPanel
                      channels={ops.data.channels}
                      canViewPlatforms={permissions.canViewPlatforms}
                    />
                  </div>
                </div>

                {visibleLinks.length > 0 && (
                  <div className="dashboard-quick-links">
                    {visibleLinks.map((item) => (
                      <Link key={item.to} to={item.to} className="card dashboard-quick-link">
                        <div className="dashboard-quick-link-title">{item.label}</div>
                        <div className="dashboard-quick-link-desc">{item.desc}</div>
                      </Link>
                    ))}
                  </div>
                )}
              </>
            )}
          </div>
        )}
      </div>
    </section>
  )
}
