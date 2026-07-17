import { useMemo, useState } from 'react'
import { Link } from 'react-router-dom'
import PageHeader from '@/shared/components/PageHeader'
import { usePermissions } from '@/shared/hooks/usePermissions'
import LoadingState from '@/shared/components/LoadingState'
import ErrorState from '@/shared/components/ErrorState'
import EmptyState from '@/shared/components/EmptyState'
import { formatDateTime, getErrorMessage } from '@/shared/utils/apiHelpers'
import { confirmAction, CONFIRM_MESSAGES } from '@/shared/utils/confirmAction'
import { toast } from '@/shared/stores/toastStore'
import { useCategoryList } from '@/modules/categories/hooks/useCategories'
import { useSocialChannelAll } from '@/modules/social-channels/hooks/useSocialChannels'
import PostFilterBar from '../components/PostFilterBar'
import PostStatusBadge from '../components/PostStatusBadge'
import { useDeletePost, usePosts } from '../hooks/usePosts'

export default function PostListPage() {
  const { canCreatePost, canDeletePost } = usePermissions()
  const [keyword, setKeyword] = useState('')
  const [status, setStatus] = useState('')
  const [generationFlow, setGenerationFlow] = useState('')
  const [page, setPage] = useState(1)

  const params = useMemo(
    () => ({
      keyword,
      index: page,
      size: 20,
      status: status ? Number(status) : undefined,
      generationFlow: generationFlow ? Number(generationFlow) : undefined,
    }),
    [keyword, page, status, generationFlow],
  )

  const { data, isLoading, isError, error, refetch } = usePosts(params)
  const { data: channels = [] } = useSocialChannelAll()
  const { data: categoryData } = useCategoryList({ index: 1, size: 200 })
  const deleteMutation = useDeletePost()

  const channelMap = useMemo(
    () => Object.fromEntries(channels.map((c) => [c.id, c.pageName])),
    [channels],
  )

  const categoryMap = useMemo(
    () => Object.fromEntries((categoryData?.items ?? []).map((c) => [c.id, c.name])),
    [categoryData],
  )

  const items = data?.items ?? []
  const total = data?.total ?? 0
  const totalPages = Math.max(1, Math.ceil(total / (data?.size || 20)))

  const handleDelete = async (item) => {
    if (!confirmAction(CONFIRM_MESSAGES.deletePost(item.title))) return
    try {
      await deleteMutation.mutateAsync(item.id)
      toast.success('Đã xóa bài viết')
    } catch (deleteError) {
      toast.error(getErrorMessage(deleteError))
    }
  }

  return (
    <section>
      <PageHeader
        title="Bài viết"
        description="Quản lý pipeline sinh nội dung và đăng bài tự động"
        actions={
          canCreatePost ? (
            <Link to="/posts/create" className="btn btn-primary">
              Tạo bài viết
            </Link>
          ) : null
        }
      />

      <div style={{ marginBottom: 16 }}>
        <PostFilterBar
          keyword={keyword}
          onKeywordChange={(value) => { setKeyword(value); setPage(1) }}
          status={status}
          onStatusChange={(value) => { setStatus(value); setPage(1) }}
          generationFlow={generationFlow}
          onGenerationFlowChange={(value) => { setGenerationFlow(value); setPage(1) }}
        />
      </div>

      <div className="card">
        {isLoading && <LoadingState />}
        {isError && <ErrorState message={getErrorMessage(error)} onRetry={refetch} />}
        {!isLoading && !isError && items.length === 0 && (
          <EmptyState
            message="Chưa có bài viết nào"
            action={
              canCreatePost ? (
                <Link to="/posts/create" className="btn btn-primary">
                  Tạo bài viết đầu tiên
                </Link>
              ) : null
            }
          />
        )}
        {!isLoading && !isError && items.length > 0 && (
          <table>
            <thead>
              <tr>
                <th>Tiêu đề</th>
                <th>Kênh</th>
                <th>Danh mục</th>
                <th>Trạng thái</th>
                <th>Lịch đăng</th>
                <th>Ngày tạo</th>
                <th />
              </tr>
            </thead>
            <tbody>
              {items.map((item) => (
                <tr key={item.id}>
                  <td>
                    <Link to={`/posts/${item.id}`}>{item.title}</Link>
                  </td>
                  <td>{channelMap[item.socialChannelId] || '—'}</td>
                  <td>{item.categoryId ? (categoryMap[item.categoryId] || '—') : '—'}</td>
                  <td><PostStatusBadge status={item.status} /></td>
                  <td>{formatDateTime(item.scheduledPublishAt)}</td>
                  <td>{formatDateTime(item.createdAt)}</td>
                  <td>
                    <div style={{ display: 'flex', gap: 8, justifyContent: 'flex-end' }}>
                      <Link to={`/posts/${item.id}`} className="btn btn-ghost">
                        Chi tiết
                      </Link>
                      {canDeletePost(item.userId) && (
                        <button
                          type="button"
                          className="btn btn-danger"
                          onClick={() => handleDelete(item)}
                        >
                          Xóa
                        </button>
                      )}
                    </div>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
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
