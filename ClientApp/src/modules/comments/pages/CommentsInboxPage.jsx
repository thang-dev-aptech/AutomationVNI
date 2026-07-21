import { useMemo, useState } from 'react'
import { Link } from 'react-router-dom'
import PageHeader from '@/shared/components/PageHeader'
import LoadingState from '@/shared/components/LoadingState'
import ErrorState from '@/shared/components/ErrorState'
import EmptyState from '@/shared/components/EmptyState'
import StatusBadge from '@/shared/components/StatusBadge'
import { formatDateTime, getErrorMessage } from '@/shared/utils/apiHelpers'
import { toast } from '@/shared/stores/toastStore'
import { usePermissions } from '@/shared/hooks/usePermissions'
import { useSocialChannelAll } from '@/modules/social-channels/hooks/useSocialChannels'
import {
  getInboxStatusMeta,
  getPlatformLabel,
  getActionTypeLabel,
  truncate,
} from '../constants/commentConstants'
import {
  useAssignComment,
  useCommentActions,
  useCommentInbox,
  useCommentModeration,
  useCommentNote,
  useCommentSummary,
  useCommentThread,
  useReplyComment,
  useSetCommentStatus,
  useSyncComments,
} from '../hooks/useComments'
import './CommentsInboxPage.css'

function CommentNode({ comment, depth = 0 }) {
  return (
    <div className={`comment-node depth-${Math.min(depth, 4)}`}>
      <div className="comment-node-meta">
        <strong>{comment.authorName || comment.authorUsername || 'Ẩn danh'}</strong>
        <span>{formatDateTime(comment.commentedAt || comment.createdAt)}</span>
        {comment.isFromPage && <StatusBadge label="Page" tone="info" />}
        {comment.isHidden && <StatusBadge label="Đã ẩn" tone="warning" />}
        {comment.isPending && <StatusBadge label="Pending" tone="warning" />}
      </div>
      <p className="comment-node-body">{comment.message || '(không có nội dung)'}</p>
      {comment.replies?.length > 0 && (
        <div className="comment-node-children">
          {comment.replies.map((child) => (
            <CommentNode key={child.id} comment={child} depth={depth + 1} />
          ))}
        </div>
      )}
    </div>
  )
}

export default function CommentsInboxPage() {
  const permissions = usePermissions()
  const canAct = permissions.hasRole(['Admin', 'ContentManager', 'Reviewer'])
  const canSync = permissions.hasRole(['Admin', 'ContentManager'])

  const [platform, setPlatform] = useState('')
  const [channelId, setChannelId] = useState('')
  const [status, setStatus] = useState('')
  const [unrepliedOnly, setUnrepliedOnly] = useState(true)
  const [keyword, setKeyword] = useState('')
  const [selectedId, setSelectedId] = useState(null)
  const [replyText, setReplyText] = useState('')
  const [noteText, setNoteText] = useState('')
  const [assignTo, setAssignTo] = useState('')

  const params = useMemo(() => ({
    index: 1,
    size: 50,
    platform: platform ? Number(platform) : undefined,
    socialChannelId: channelId || undefined,
    inboxStatus: status ? Number(status) : undefined,
    unrepliedOnly: unrepliedOnly || undefined,
    keyword: keyword || undefined,
  }), [platform, channelId, status, unrepliedOnly, keyword])

  const { data: channels = [] } = useSocialChannelAll()
  const { data: summary } = useCommentSummary()
  const { data, isLoading, isError, error, refetch } = useCommentInbox(params)
  const { data: thread, isLoading: threadLoading } = useCommentThread(selectedId)
  const { data: actions = [] } = useCommentActions(selectedId)

  const syncMutation = useSyncComments()
  const replyMutation = useReplyComment()
  const moderationMutation = useCommentModeration()
  const statusMutation = useSetCommentStatus()
  const assignMutation = useAssignComment()
  const noteMutation = useCommentNote()

  const items = data?.items ?? []
  const caps = thread?.capabilities ?? {}

  const handleSync = async (mode) => {
    try {
      const result = await syncMutation.mutateAsync({
        socialChannelId: channelId || null,
        mode: mode === 'full' ? 1 : 2,
      })
      toast.success(
        `Đồng bộ xong: ${result.postsUpserted} bài, ${result.commentsUpserted} comment`
        + (result.errors?.length ? ` (${result.errors.length} lỗi)` : ''),
      )
    } catch (err) {
      toast.error(getErrorMessage(err))
    }
  }

  const handleReply = async () => {
    if (!selectedId || !replyText.trim()) return
    try {
      await replyMutation.mutateAsync({ id: selectedId, message: replyText.trim() })
      setReplyText('')
      toast.success('Đã gửi trả lời')
    } catch (err) {
      toast.error(getErrorMessage(err))
    }
  }

  const runModeration = async (action, approve) => {
    if (!selectedId) return
    try {
      await moderationMutation.mutateAsync({ id: selectedId, action, approve })
      toast.success('Đã cập nhật')
    } catch (err) {
      toast.error(getErrorMessage(err))
    }
  }

  return (
    <section className="comments-inbox-page">
      <PageHeader
        title="Hộp thư comment"
        description="Facebook + Threads — đồng bộ, trả lời và kiểm duyệt comment trên các page đã kết nối"
        actions={(
          <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap' }}>
            <button type="button" className="btn btn-secondary btn-sm" onClick={() => refetch()}>
              Làm mới
            </button>
            {canSync && (
              <>
                <button
                  type="button"
                  className="btn btn-secondary btn-sm"
                  disabled={syncMutation.isPending}
                  onClick={() => handleSync('recent')}
                >
                  {syncMutation.isPending ? 'Đang đồng bộ...' : 'Đồng bộ gần đây'}
                </button>
                <button
                  type="button"
                  className="btn btn-primary btn-sm"
                  disabled={syncMutation.isPending}
                  onClick={() => handleSync('full')}
                >
                  Đồng bộ đầy đủ
                </button>
              </>
            )}
          </div>
        )}
      />

      <div className="comments-summary-row">
        <div className="comments-summary-chip">Tổng: {summary?.total ?? '—'}</div>
        <div className="comments-summary-chip warn">Chưa trả lời: {summary?.unreplied ?? '—'}</div>
        <div className="comments-summary-chip">Mới: {summary?.newCount ?? '—'}</div>
        <div className="comments-summary-chip">Đang xử lý: {summary?.inProgress ?? '—'}</div>
        <div className="comments-summary-chip">Ẩn: {summary?.hidden ?? '—'}</div>
        <div className="comments-summary-chip">Pending: {summary?.pending ?? '—'}</div>
      </div>

      <div className="card card-body comments-filters">
        <div className="comments-filters-grid">
          <div className="form-group">
            <label htmlFor="c-platform">Nền tảng</label>
            <select id="c-platform" value={platform} onChange={(e) => setPlatform(e.target.value)}>
              <option value="">Tất cả</option>
              <option value="1">Facebook</option>
              <option value="5">Threads</option>
            </select>
          </div>
          <div className="form-group">
            <label htmlFor="c-channel">Kênh</label>
            <select id="c-channel" value={channelId} onChange={(e) => setChannelId(e.target.value)}>
              <option value="">Tất cả</option>
              {channels.map((c) => (
                <option key={c.id} value={c.id}>{c.pageName}</option>
              ))}
            </select>
          </div>
          <div className="form-group">
            <label htmlFor="c-status">Trạng thái</label>
            <select id="c-status" value={status} onChange={(e) => setStatus(e.target.value)}>
              <option value="">Tất cả</option>
              <option value="1">Mới</option>
              <option value="2">Đang xử lý</option>
              <option value="3">Đã trả lời</option>
              <option value="4">Bỏ qua</option>
            </select>
          </div>
          <div className="form-group">
            <label htmlFor="c-keyword">Tìm kiếm</label>
            <input
              id="c-keyword"
              value={keyword}
              onChange={(e) => setKeyword(e.target.value)}
              placeholder="Nội dung / tác giả..."
            />
          </div>
          <label className="comments-check">
            <input
              type="checkbox"
              checked={unrepliedOnly}
              onChange={(e) => setUnrepliedOnly(e.target.checked)}
            />
            Chỉ chưa trả lời
          </label>
        </div>
      </div>

      <div className="comments-layout">
        <div className="card comments-list-panel">
          {isLoading && <LoadingState message="Đang tải hộp thư..." />}
          {isError && <ErrorState message={getErrorMessage(error)} onRetry={refetch} />}
          {!isLoading && !isError && items.length === 0 && (
            <EmptyState
              message="Chưa có comment. Bấm Đồng bộ để kéo dữ liệu từ Facebook/Threads."
            />
          )}
          {!isLoading && !isError && items.length > 0 && (
            <ul className="comments-list">
              {items.map((item) => {
                const meta = getInboxStatusMeta(item.inboxStatus)
                return (
                  <li key={item.id}>
                    <button
                      type="button"
                      className={`comments-list-item${selectedId === item.id ? ' is-active' : ''}`}
                      onClick={() => {
                        setSelectedId(item.id)
                        setReplyText('')
                        setNoteText(item.internalNote || '')
                        setAssignTo(item.assignedTo || '')
                      }}
                    >
                      <div className="comments-list-top">
                        <span className="comments-list-author">
                          {item.authorName || item.authorUsername || 'Ẩn danh'}
                        </span>
                        <StatusBadge label={meta.label} tone={meta.tone} />
                      </div>
                      <div className="comments-list-body">{truncate(item.message, 140)}</div>
                      <div className="comments-list-meta">
                        {getPlatformLabel(item.platform)}
                        {item.channelName ? ` · ${item.channelName}` : ''}
                        {' · '}
                        {formatDateTime(item.commentedAt || item.createdAt)}
                      </div>
                    </button>
                  </li>
                )
              })}
            </ul>
          )}
        </div>

        <div className="card comments-detail-panel">
          {!selectedId && (
            <EmptyState message="Chọn một comment bên trái để xem hội thoại và trả lời" />
          )}
          {selectedId && threadLoading && <LoadingState message="Đang tải hội thoại..." />}
          {selectedId && !threadLoading && thread && (
            <div className="comments-detail">
              <div className="comments-detail-header">
                <div>
                  <h2 style={{ margin: '0 0 6px', fontSize: '1.05rem' }}>
                    {thread.authorName || thread.authorUsername || 'Ẩn danh'}
                  </h2>
                  <div className="comments-list-meta">
                    {getPlatformLabel(thread.platform)}
                    {thread.channelName ? ` · ${thread.channelName}` : ''}
                    {thread.postPermalinkUrl && (
                      <>
                        {' · '}
                        <a href={thread.postPermalinkUrl} target="_blank" rel="noreferrer">
                          Xem bài gốc
                        </a>
                      </>
                    )}
                    {thread.localPostId && (
                      <>
                        {' · '}
                        <Link to={`/posts/${thread.localPostId}`}>Bài nội bộ</Link>
                      </>
                    )}
                  </div>
                </div>
                <StatusBadge
                  label={getInboxStatusMeta(thread.inboxStatus).label}
                  tone={getInboxStatusMeta(thread.inboxStatus).tone}
                />
              </div>

              {thread.postMessage && (
                <div className="comments-post-preview">
                  <div className="comments-post-label">Bài viết</div>
                  <p>{truncate(thread.postMessage, 280)}</p>
                </div>
              )}

              <div className="comments-thread">
                <CommentNode comment={thread} />
              </div>

              {canAct && (
                <div className="comments-actions">
                  {caps.canReply && (
                    <div className="form-group">
                      <label htmlFor="reply-box">Trả lời</label>
                      <textarea
                        id="reply-box"
                        rows={3}
                        value={replyText}
                        onChange={(e) => setReplyText(e.target.value)}
                        placeholder="Nhập nội dung trả lời..."
                      />
                      <button
                        type="button"
                        className="btn btn-primary"
                        disabled={replyMutation.isPending || !replyText.trim()}
                        onClick={handleReply}
                      >
                        {replyMutation.isPending ? 'Đang gửi...' : 'Gửi trả lời'}
                      </button>
                    </div>
                  )}

                  <div className="comments-action-row">
                    {caps.canHide && !thread.isHidden && (
                      <button type="button" className="btn btn-secondary btn-sm" onClick={() => runModeration('hide')}>
                        Ẩn
                      </button>
                    )}
                    {caps.canUnhide && thread.isHidden && (
                      <button type="button" className="btn btn-secondary btn-sm" onClick={() => runModeration('unhide')}>
                        Hiện
                      </button>
                    )}
                    {caps.canDelete && (
                      <button
                        type="button"
                        className="btn btn-danger btn-sm"
                        onClick={() => {
                          if (window.confirm('Xóa comment này trên Facebook?')) runModeration('delete')
                        }}
                      >
                        Xóa
                      </button>
                    )}
                    {caps.canManagePending && thread.isPending && (
                      <>
                        <button type="button" className="btn btn-secondary btn-sm" onClick={() => runModeration('pending', true)}>
                          Duyệt pending
                        </button>
                        <button type="button" className="btn btn-ghost btn-sm" onClick={() => runModeration('pending', false)}>
                          Bỏ qua pending
                        </button>
                      </>
                    )}
                    <button
                      type="button"
                      className="btn btn-ghost btn-sm"
                      onClick={() => statusMutation.mutateAsync({ id: selectedId, status: 2 })}
                    >
                      Đánh dấu đang xử lý
                    </button>
                    <button
                      type="button"
                      className="btn btn-ghost btn-sm"
                      onClick={() => statusMutation.mutateAsync({ id: selectedId, status: 4 })}
                    >
                      Bỏ qua
                    </button>
                  </div>

                  <div className="comments-meta-forms">
                    <div className="form-group">
                      <label htmlFor="assign-to">Gán người xử lý</label>
                      <div style={{ display: 'flex', gap: 8 }}>
                        <input
                          id="assign-to"
                          value={assignTo}
                          onChange={(e) => setAssignTo(e.target.value)}
                          placeholder="email / tên..."
                        />
                        <button
                          type="button"
                          className="btn btn-secondary btn-sm"
                          onClick={async () => {
                            try {
                              await assignMutation.mutateAsync({ id: selectedId, assignedTo: assignTo })
                              toast.success('Đã gán')
                            } catch (err) {
                              toast.error(getErrorMessage(err))
                            }
                          }}
                        >
                          Gán
                        </button>
                      </div>
                    </div>
                    <div className="form-group">
                      <label htmlFor="note-box">Ghi chú nội bộ</label>
                      <textarea
                        id="note-box"
                        rows={2}
                        value={noteText}
                        onChange={(e) => setNoteText(e.target.value)}
                      />
                      <button
                        type="button"
                        className="btn btn-secondary btn-sm"
                        onClick={async () => {
                          try {
                            await noteMutation.mutateAsync({ id: selectedId, note: noteText })
                            toast.success('Đã lưu ghi chú')
                          } catch (err) {
                            toast.error(getErrorMessage(err))
                          }
                        }}
                      >
                        Lưu ghi chú
                      </button>
                    </div>
                  </div>
                </div>
              )}

              {actions.length > 0 && (
                <div className="comments-action-log">
                  <h3>Lịch sử thao tác</h3>
                  <ul>
                    {actions.map((a) => (
                      <li key={a.id}>
                        <span>{getActionTypeLabel(a.actionType)}</span>
                        {' · '}
                        {a.actorUserName || '—'}
                        {' · '}
                        {formatDateTime(a.createdAt)}
                        {!a.success && a.errorMessage ? ` · lỗi: ${a.errorMessage}` : ''}
                      </li>
                    ))}
                  </ul>
                </div>
              )}
            </div>
          )}
        </div>
      </div>
    </section>
  )
}
