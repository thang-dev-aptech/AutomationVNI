import { Link, useParams } from 'react-router-dom'
import { useQuery } from '@tanstack/react-query'
import PageHeader from '@/shared/components/PageHeader'
import LoadingState from '@/shared/components/LoadingState'
import ErrorState from '@/shared/components/ErrorState'
import { formatDateTime, getErrorMessage, unwrapApiData } from '@/shared/utils/apiHelpers'
import { campaignApi, campaignQueryKeys } from '../services/campaignApi'

/** Stub list bài theo page — t5 bổ sung UX. */
export default function CampaignPagePostsPage() {
  const { id, channelId } = useParams()
  const params = { index: 1, size: 50 }
  const { data, isLoading, isError, error, refetch } = useQuery({
    queryKey: campaignQueryKeys.pagePosts(id, channelId, params),
    queryFn: async () =>
      unwrapApiData(await campaignApi.listPagePosts(id, channelId, params)),
    enabled: Boolean(id && channelId),
  })

  const items = data?.items ?? []

  return (
    <section data-testid="campaign-page-posts">
      <PageHeader
        title="Bài trong chiến dịch"
        description={`Page ${channelId}`}
        actions={
          <Link to={`/campaigns/${id}`} className="btn btn-ghost">
            ← Chi tiết chiến dịch
          </Link>
        }
      />

      <div className="card">
        {isLoading && <LoadingState />}
        {isError && <ErrorState message={getErrorMessage(error)} onRetry={refetch} />}
        {!isLoading && !isError && (
          <div className="table-container">
            <table>
              <thead>
                <tr>
                  <th>Giờ đăng</th>
                  <th>Trạng thái</th>
                  <th>Nội dung</th>
                  <th />
                </tr>
              </thead>
              <tbody>
                {items.length === 0 ? (
                  <tr>
                    <td colSpan={4} className="text-muted">
                      Chưa có bài
                    </td>
                  </tr>
                ) : (
                  items.map((p) => (
                    <tr key={p.id}>
                      <td>{formatDateTime(p.scheduledPublishAt || p.publishedAt)}</td>
                      <td>{p.status}</td>
                      <td>{p.contentSnippet || p.title || '—'}</td>
                      <td>
                        <Link to={`/posts/${p.id}`} className="btn btn-ghost">
                          Bài
                        </Link>
                      </td>
                    </tr>
                  ))
                )}
              </tbody>
            </table>
          </div>
        )}
      </div>
    </section>
  )
}
