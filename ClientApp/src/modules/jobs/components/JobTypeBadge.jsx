import StatusBadge from '@/shared/components/StatusBadge'
import { getJobTypeMeta } from '../constants/jobConstants'

export default function JobTypeBadge({ type }) {
  const meta = getJobTypeMeta(type)
  return <StatusBadge label={meta.label} tone={meta.tone} />
}
