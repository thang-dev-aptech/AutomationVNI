import { Link, useParams } from 'react-router-dom'
import PageHeader from '@/shared/components/PageHeader'
import LoadingState from '@/shared/components/LoadingState'
import ErrorState from '@/shared/components/ErrorState'
import { formatDateTime, getErrorMessage } from '@/shared/utils/apiHelpers'
import { useCampaignDetail } from '../hooks/useCampaigns'
import { statusLabel } from '../constants/campaignEnums'

/** Stub + table page cơ bản — t5 bổ sung thao tác lifecycle. */
export default function CampaignDetailPage() {
  const { id } = useParams()
  const { data, isLoading, isError, error, refetch } = useCampaignDetail(id)
  const campaign = data?.campaign
  const pages = data?.pages ?? []

  return (
    <section data-testid="campaign-detail">
      <PageHeader
        title={campaign?.name || 'Chi tiết chiến dịch'}
        description={campaign ? statusLabel(campaign.status) : 'Đang tải...'}
        actions={
          <Link to="/campaigns" className="btn btn-ghost">
            ← Danh sách
          </Link>
        }
      />

      {isLoading && <LoadingState />}
      {isError && <ErrorState message={getErrorMessage(error)} onRetry={refetch} />}

      {!isLoading && !isError && (
        <div className="card">
          <div className="table-container">
            <table data-testid="campaign-pages-table">
              <thead>
                <tr>
                  <th>Page</th>
                  <th>Lên lịch</th>
                  <th>Đã đăng</th>
                  <th>Thất bại</th>
                  <th>Bài kế tiếp</th>
                  <th>Cảnh báo</th>
                </tr>
              </thead>
              <tbody>
                {pages.length === 0 ? (
                  <tr>
                    <td colSpan={6} className="text-muted">
                      Chưa có page đang chạy
                    </td>
                  </tr>
                ) : (
                  pages.map((p) => (
                    <tr key={p.socialChannelId}>
                      <td>
                        <Link to={`/campaigns/${id}/pages/${p.socialChannelId}`}>
                          {p.channelName || p.socialChannelId}
                        </Link>
                      </td>
                      <td>{p.scheduledCount}</td>
                      <td>{p.publishedCount}</td>
                      <td>{p.failedCount}</td>
                      <td>{formatDateTime(p.nextScheduledAt)}</td>
                      <td>{p.warning || '—'}</td>
                    </tr>
                  ))
                )}
              </tbody>
            </table>
          </div>
        </div>
      )}
    </section>
  )
}
