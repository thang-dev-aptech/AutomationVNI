import { ROLES } from '@/shared/auth/permissions'

export const ROUTE_ROLES = {
  platforms: [ROLES.ADMIN, ROLES.CONTENT_MANAGER, ROLES.VIEWER],
  postsCreate: [ROLES.ADMIN, ROLES.CONTENT_MANAGER],
  jobs: [ROLES.ADMIN, ROLES.CONTENT_MANAGER, ROLES.REVIEWER, ROLES.VIEWER],
}
