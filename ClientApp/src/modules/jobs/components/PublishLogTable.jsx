import { useState } from 'react'
import { Link } from 'react-router-dom'
import LoadingState from '@/shared/components/LoadingState'
import ErrorState from '@/shared/components/ErrorState'
import EmptyState from '@/shared/components/EmptyState'
import { formatDateTime, getErrorMessage } from '@/shared/utils/apiHelpers'
import { shortId, truncateText } from '../constants/jobConstants'
import PublishStatusBadge from './PublishStatusBadge'
import JobErrorPanel from './JobErrorPanel'
import './JobsTables.css'

export default function PublishLogTable({
  items,
  channelMap = {},
  isLoading,
  isError,
  error,
  onRetry,
}) {
  const [errorPanel, setErrorPanel] = useState(null)

  if (isLoading) return <LoadingState />
  if (isError) return <ErrorState message={getErrorMessage(error)} onRetry={onRetry} />
  if (items.length === 0) return <EmptyState message="Không có publish log nào" />

  return (
    <>
      <div className="jobs-table-wrap">
        <table>
          <thead>
            <tr>
              <th>ID</th>
              <th>Post</th>
              <th>Channel</th>
              <th>Status</th>
              <th>Attempt</th>
              <th>Published at</th>
              <th>External ID</th>
              <th>Error</th>
              <th />
            </tr>
          </thead>
          <tbody>
            {items.map((log) => (
              <tr key={log.id}>
                <td title={log.id}>{shortId(log.id)}</td>
                <td>
                  <Link to={`/posts/${log.postId}`} title={log.postId}>
                    {shortId(log.postId)}
                  </Link>
                </td>
                <td title={log.socialChannelId}>
                  {channelMap[log.socialChannelId] || shortId(log.socialChannelId)}
                </td>
                <td><PublishStatusBadge status={log.status} /></td>
                <td>{log.attemptNumber}</td>
                <td>{formatDateTime(log.publishedAt)}</td>
                <td title={log.externalPostId || ''}>
                  {truncateText(log.externalPostId, 24) || '—'}
                </td>
                <td title={log.errorMessage || log.errorCode || ''}>
                  {truncateText(log.errorCode || log.errorMessage, 40) || '—'}
                </td>
                <td>
                  {(log.errorMessage || log.errorCode) && (
                    <button
                      type="button"
                      className="btn btn-ghost btn-sm"
                      onClick={() => setErrorPanel(log)}
                    >
                      Error
                    </button>
                  )}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>

      <JobErrorPanel
        open={Boolean(errorPanel)}
        title="Publish log error"
        errorCode={errorPanel?.errorCode}
        errorMessage={errorPanel?.errorMessage}
        onClose={() => setErrorPanel(null)}
      />
    </>
  )
}
