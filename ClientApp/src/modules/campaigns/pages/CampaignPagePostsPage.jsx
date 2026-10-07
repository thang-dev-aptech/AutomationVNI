import { useMemo, useState } from 'react'
import { Link, useParams } from 'react-router-dom'
import { useQuery } from '@tanstack/react-query'
import PageHeader from '@/shared/components/PageHeader'
import LoadingState from '@/shared/components/LoadingState'
import ErrorState from '@/shared/components/ErrorState'
import EmptyState from '@/shared/components/EmptyState'
import { formatDateTime, getErrorMessage, unwrapApiData } from '@/shared/utils/apiHelpers'
import PostStatusBadge from '@/modules/posts/components/PostStatusBadge'
import { campaignApi, campaignQueryKeys } from '../services/campaignApi'
import { useCampaignDetail } from '../hooks/useCampaigns'

export default function CampaignPagePostsPage() {
  const { id, channelId } = useParams()
  const [page, setPage] = useState(1)
  const size = 20
  const params = useMemo(() => ({ index: page, size }), [page])

  const { data: detail } = useCampaignDetail(id)
  const channelName = useMemo(() => {
    const pages = detail?.pages ?? []
    return pages.find((p) => p.socialChannelId === channelId)?.channelName
      || channelId
  }, [detail, channelId])

  const { data, isLoading, isError, error, refetch } = useQuery({
    queryKey: campaignQueryKeys.pagePosts(id, channelId, params),
    queryFn: async () =>
      unwrapApiData(await campaignApi.listPagePosts(id, channelId, params)),
    enabled: Boolean(id && channelId),
  })

  const items = data?.items ?? []
  const total = data?.total ?? 0
  const totalPages = Math.max(1, Math.ceil(total / (data?.size || size)))

  return (
    <section data-testid="campaign-page-posts">
      <PageHeader
        title="Bài trong chiến dịch"
        description={channelName}
        actions={
          <Link to={`/campaigns/${id}`} className="btn btn-ghost">
            ← Chi tiết chiến dịch
          </Link>
        }
      />

      <div className="card">
        {isLoading && <LoadingState />}
        {isError && <ErrorState message={getErrorMessage(error)} onRetry={refetch} />}
        {!isLoading && !isError && items.length === 0 && (
          <EmptyState message="Chưa có bài cho page này trong chiến dịch" />
        )}
        {!isLoading && !isError && items.length > 0 && (
          <div className="table-container">
            <table data-testid="campaign-posts-table">
              <thead>
                <tr>
                  <th>Media</th>
                  <th>Giờ đăng</th>
                  <th>Trạng thái</th>
                  <th>Nội dung</th>
                  <th />
                </tr>
              </thead>
              <tbody>
                {items.map((p) => (
                  <tr key={p.id} data-testid={`post-row-${p.id}`}>
                    <td>
                      {p.thumbnailUrl ? (
                        <img
                          src={p.thumbnailUrl}
                          alt=""
                          width={48}
                          height={48}
                          style={{ objectFit: 'cover', borderRadius: 4 }}
                          data-testid={`post-thumb-${p.id}`}
                        />
                      ) : (
                        <span className="text-muted">—</span>
                      )}
                      {p.mediaCount > 0 ? (
                        <div className="text-muted" style={{ fontSize: '0.75rem' }}>
                          {p.mediaCount} file
                        </div>
                      ) : null}
                    </td>
                    <td>{formatDateTime(p.scheduledPublishAt || p.publishedAt)}</td>
                    <td><PostStatusBadge status={p.status} /></td>
                    <td style={{ maxWidth: 360 }}>
                      {p.contentSnippet || p.title || '—'}
                    </td>
                    <td>
                      <Link
                        to={`/posts/${p.id}`}
                        className="btn btn-ghost"
                        data-testid={`post-link-${p.id}`}
                      >
                        Chi tiết bài
                      </Link>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>

      {!isLoading && totalPages > 1 && (
        <div style={{ display: 'flex', justifyContent: 'center', gap: 12, marginTop: 16 }}>
          <button
            type="button"
            className="btn btn-secondary"
            disabled={page <= 1}
            onClick={() => setPage((p) => p - 1)}
          >
            Trước
          </button>
          <span style={{ alignSelf: 'center' }}>
            Trang {page} / {totalPages}
          </span>
          <button
            type="button"
            className="btn btn-secondary"
            disabled={page >= totalPages}
            onClick={() => setPage((p) => p + 1)}
          >
            Sau
          </button>
        </div>
      )}
    </section>
  )
}
