import { Link } from 'react-router-dom'
import LoadingState from '@/shared/components/LoadingState'
import ErrorState from '@/shared/components/ErrorState'
import EmptyState from '@/shared/components/EmptyState'
import { formatDateTime, getErrorMessage } from '@/shared/utils/apiHelpers'
import { shortId, truncateText } from '../constants/jobConstants'
import JobStatusBadge from './JobStatusBadge'
import JobTypeBadge from './JobTypeBadge'
import JobActionButtons from './JobActionButtons'
import './JobsTables.css'

export default function GenerationJobTable({
  items,
  isLoading,
  isError,
  error,
  onRetry,
  onViewError,
  onActionError,
}) {
  if (isLoading) return <LoadingState />
  if (isError) return <ErrorState message={getErrorMessage(error)} onRetry={onRetry} />
  if (items.length === 0) return <EmptyState message="Không có generation job nào" />

  return (
    <div className="jobs-table-wrap">
      <table>
        <thead>
          <tr>
            <th>Job ID</th>
            <th>Post</th>
            <th>Type</th>
            <th>Status</th>
            <th>Retry</th>
            <th>Started</th>
            <th>Finished</th>
            <th>Error</th>
            <th>Actions</th>
          </tr>
        </thead>
        <tbody>
          {items.map((job) => (
            <tr key={job.id}>
              <td title={job.id}>{shortId(job.id)}</td>
              <td>
                <Link to={`/posts/${job.postId}`} title={job.postId}>
                  {shortId(job.postId)}
                </Link>
              </td>
              <td><JobTypeBadge type={job.jobType} /></td>
              <td><JobStatusBadge status={job.status} /></td>
              <td>{job.retryCount}/{job.maxRetries}</td>
              <td>{formatDateTime(job.startedAt)}</td>
              <td>{formatDateTime(job.completedAt)}</td>
              <td title={job.errorMessage || job.errorCode || ''}>
                {truncateText(job.errorCode || job.errorMessage, 40) || '—'}
              </td>
              <td>
                <JobActionButtons
                  job={job}
                  onViewError={onViewError}
                  onActionError={onActionError}
                />
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  )
}
