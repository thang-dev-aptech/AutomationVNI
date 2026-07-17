import { useEffect, useState } from 'react'
import { Link, useNavigate, useParams } from 'react-router-dom'
import PageHeader from '@/shared/components/PageHeader'
import { usePermissions } from '@/shared/hooks/usePermissions'
import LoadingState from '@/shared/components/LoadingState'
import ErrorState from '@/shared/components/ErrorState'
import { formatDateTime, getErrorMessage } from '@/shared/utils/apiHelpers'
import { toast } from '@/shared/stores/toastStore'
import { useSocialChannelAll } from '@/modules/social-channels/hooks/useSocialChannels'
import PostGenerationActions from '../components/PostGenerationActions'
import PostGenerationStatus from '../components/PostGenerationStatus'
import PostMediaPanel from '@/modules/media/components/PostMediaPanel'
import PostStatusBadge from '../components/PostStatusBadge'
import PostWorkflowActions from '../components/PostWorkflowActions'
import { getAvailableWorkflowActions, getGenerationFlowLabel } from '../constants/postStatus'
import { usePostDetail, usePostTimeline, useUpdatePost } from '../hooks/usePosts'

export default function PostDetailPage() {
  const { id } = useParams()
  const navigate = useNavigate()
  const { canEditPost } = usePermissions()
  const { data: post, isLoading, isError, error, refetch } = usePostDetail(id)
  const { data: channels = [] } = useSocialChannelAll()
  const { data: timeline } = usePostTimeline(id)
  const updateMutation = useUpdatePost()

  const [content, setContent] = useState('')
  const [saveError, setSaveError] = useState('')

  useEffect(() => {
    if (post) {
      setContent(post.content || '')
    }
  }, [post])

  const channelName = channels.find((c) => c.id === post?.socialChannelId)?.pageName
  const workflowActions = post ? getAvailableWorkflowActions(post.status) : null
  const canEditContent = workflowActions?.canEditContent && canEditPost(post?.userId)

  const handleSaveContent = async () => {
    setSaveError('')
    try {
      await updateMutation.mutateAsync({ id, payload: { content } })
      toast.success('Đã lưu nội dung')
    } catch (saveErr) {
      setSaveError(getErrorMessage(saveErr))
      toast.error(getErrorMessage(saveErr))
    }
  }

  if (isLoading) return <LoadingState />
  if (isError) return <ErrorState message={getErrorMessage(error)} onRetry={refetch} />
  if (!post) return <ErrorState message="Không tìm thấy bài viết" />

  return (
    <section>
      <PageHeader
        title={post.title}
        description={`Kênh: ${channelName || '—'} · ${getGenerationFlowLabel(post.generationFlow)}`}
        actions={(
          <Link to="/posts" className="btn btn-secondary">
            Danh sách
          </Link>
        )}
      />

      <div className="card card-body" style={{ marginBottom: 16 }}>
        <div style={{ display: 'flex', flexWrap: 'wrap', gap: 24 }}>
          <div>
            <div style={{ color: 'var(--color-text-muted)', fontSize: '0.8rem' }}>Trạng thái</div>
            <PostStatusBadge status={post.status} />
          </div>
          <div>
            <div style={{ color: 'var(--color-text-muted)', fontSize: '0.8rem' }}>Lịch đăng</div>
            <div>{formatDateTime(post.scheduledPublishAt)}</div>
          </div>
          <div>
            <div style={{ color: 'var(--color-text-muted)', fontSize: '0.8rem' }}>Đã đăng lúc</div>
            <div>{formatDateTime(post.publishedAt)}</div>
          </div>
          <div>
            <div style={{ color: 'var(--color-text-muted)', fontSize: '0.8rem' }}>Tạo lúc</div>
            <div>{formatDateTime(post.createdAt)}</div>
          </div>
          <div>
            <div style={{ color: 'var(--color-text-muted)', fontSize: '0.8rem' }}>Cập nhật</div>
            <div>{formatDateTime(post.updatedAt)}</div>
          </div>
        </div>
        {post.rejectionReason && (
          <div className="alert alert-error" style={{ marginTop: 16, marginBottom: 0 }}>
            Lý do từ chối: {post.rejectionReason}
          </div>
        )}
      </div>

      <PostWorkflowActions post={post} onDeleted={() => navigate('/posts')} />

      <PostGenerationActions post={post} />

      <PostGenerationStatus postId={post.id} postStatus={post.status} />

      <PostMediaPanel postId={post.id} />

      {timeline?.events?.length > 0 && (
        <div className="card card-body" style={{ marginBottom: 16 }}>
          <h2 style={{ margin: '0 0 12px', fontSize: '1.05rem' }}>Timeline</h2>
          <ul style={{ margin: 0, padding: 0, listStyle: 'none' }}>
            {timeline.events.map((event, index) => (
              <li
                key={`${event.type}-${event.timestamp}-${index}`}
                style={{
                  padding: '10px 0',
                  borderBottom: '1px solid var(--color-border)',
                }}
              >
                <div style={{ fontWeight: 600 }}>{event.label}</div>
                <div style={{ fontSize: '0.85rem', color: 'var(--color-text-muted)' }}>
                  {formatDateTime(event.timestamp)}
                  {event.detail ? ` · ${event.detail}` : ''}
                </div>
              </li>
            ))}
          </ul>
        </div>
      )}

      <div className="card card-body">
        <h2 style={{ marginTop: 0, fontSize: '1.125rem' }}>Nội dung bài đăng</h2>
        {saveError && <div className="alert alert-error">{saveError}</div>}
        <div className="form-group">
          <textarea
            value={content}
            onChange={(event) => setContent(event.target.value)}
            rows={12}
            disabled={!canEditContent}
            placeholder="Nội dung sẽ được AI sinh sau khi worker xử lý..."
          />
        </div>
        {canEditContent && (
          <button
            type="button"
            className="btn btn-primary"
            onClick={handleSaveContent}
            disabled={updateMutation.isPending}
          >
            {updateMutation.isPending ? 'Đang lưu...' : 'Lưu nội dung'}
          </button>
        )}
      </div>
    </section>
  )
}
