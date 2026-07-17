import StatusBadge from '@/shared/components/StatusBadge'
import { getJobStatusMeta } from '../constants/jobConstants'

export default function JobStatusBadge({ status }) {
  const meta = getJobStatusMeta(status)
  return <StatusBadge label={meta.label} tone={meta.tone} />
}
