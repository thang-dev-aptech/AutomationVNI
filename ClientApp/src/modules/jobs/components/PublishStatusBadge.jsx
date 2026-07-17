import StatusBadge from '@/shared/components/StatusBadge'
import { getPublishStatusMeta } from '../constants/jobConstants'

export default function PublishStatusBadge({ status }) {
  const meta = getPublishStatusMeta(status)
  return <StatusBadge label={meta.label} tone={meta.tone} />
}
