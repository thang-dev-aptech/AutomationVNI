import StatusBadge from '@/shared/components/StatusBadge'
import { getPostStatusMeta } from '../constants/postStatus'

export default function PostStatusBadge({ status }) {
  const meta = getPostStatusMeta(status)
  return <StatusBadge label={meta.label} tone={meta.tone} />
}
