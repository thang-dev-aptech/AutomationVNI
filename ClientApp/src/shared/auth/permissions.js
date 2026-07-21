export const ROLES = {
  ADMIN: 'Admin',
  CONTENT_MANAGER: 'ContentManager',
  REVIEWER: 'Reviewer',
  VIEWER: 'Viewer',
}

function normalizeRoles(userRoles) {
  if (!userRoles?.length) return []
  return Array.isArray(userRoles) ? userRoles : [userRoles]
}

/** User có ít nhất một role trong allowedRoles */
export function hasRole(userRoles, allowedRoles) {
  if (!allowedRoles?.length) return true
  const roles = normalizeRoles(userRoles)
  return allowedRoles.some((role) => roles.includes(role))
}

export function canManageChannels(userRoles) {
  return hasRole(userRoles, [ROLES.ADMIN])
}

/** Reviewer không truy cập Platforms */
export function canViewPlatforms(userRoles) {
  return hasRole(userRoles, [ROLES.ADMIN, ROLES.CONTENT_MANAGER, ROLES.VIEWER])
}

export function canCreatePost(userRoles) {
  return hasRole(userRoles, [ROLES.ADMIN, ROLES.CONTENT_MANAGER])
}

export function canEditPost(userRoles, postUserId, currentUserId) {
  if (hasRole(userRoles, [ROLES.ADMIN])) return true
  if (hasRole(userRoles, [ROLES.CONTENT_MANAGER])) {
    if (!postUserId || !currentUserId) return true
    return String(postUserId) === String(currentUserId)
  }
  return false
}

export function canDeletePost(userRoles, postUserId, currentUserId) {
  return canEditPost(userRoles, postUserId, currentUserId)
}

/** Xóa tất cả bài: Admin (toàn bộ) hoặc ContentManager (bài của mình). */
export function canDeleteAllPosts(userRoles) {
  return hasRole(userRoles, [ROLES.ADMIN, ROLES.CONTENT_MANAGER])
}

export function canSubmitReview(userRoles) {
  return hasRole(userRoles, [ROLES.ADMIN, ROLES.CONTENT_MANAGER])
}

export function canApprovePost(userRoles) {
  return hasRole(userRoles, [ROLES.ADMIN, ROLES.REVIEWER])
}

export function canRejectPost(userRoles) {
  return hasRole(userRoles, [ROLES.ADMIN, ROLES.REVIEWER])
}

export function canSchedulePost(userRoles) {
  return hasRole(userRoles, [ROLES.ADMIN, ROLES.REVIEWER])
}

export function canPublishPost(userRoles) {
  return hasRole(userRoles, [ROLES.ADMIN, ROLES.REVIEWER])
}

export function canManageMedia(userRoles) {
  return hasRole(userRoles, [ROLES.ADMIN, ROLES.CONTENT_MANAGER])
}

export function canManageTemplates(userRoles) {
  return hasRole(userRoles, [ROLES.ADMIN, ROLES.CONTENT_MANAGER])
}

export function canManageJobs(userRoles) {
  return hasRole(userRoles, [ROLES.ADMIN])
}

export function canViewJobs(userRoles) {
  return hasRole(userRoles, [
    ROLES.ADMIN,
    ROLES.CONTENT_MANAGER,
    ROLES.REVIEWER,
    ROLES.VIEWER,
  ])
}

/** Menu visibility helpers */
export function canViewPosts(userRoles) {
  return hasRole(userRoles, [
    ROLES.ADMIN,
    ROLES.CONTENT_MANAGER,
    ROLES.REVIEWER,
    ROLES.VIEWER,
  ])
}

export function canViewMedia(userRoles) {
  return hasRole(userRoles, [
    ROLES.ADMIN,
    ROLES.CONTENT_MANAGER,
    ROLES.REVIEWER,
    ROLES.VIEWER,
  ])
}

export function canViewDashboard(userRoles) {
  return hasRole(userRoles, [
    ROLES.ADMIN,
    ROLES.CONTENT_MANAGER,
    ROLES.REVIEWER,
    ROLES.VIEWER,
  ])
}

export function canViewComments(userRoles) {
  return hasRole(userRoles, [
    ROLES.ADMIN,
    ROLES.CONTENT_MANAGER,
    ROLES.REVIEWER,
    ROLES.VIEWER,
  ])
}

export function canManageComments(userRoles) {
  return hasRole(userRoles, [ROLES.ADMIN, ROLES.CONTENT_MANAGER, ROLES.REVIEWER])
}

export function canViewMessages(userRoles) {
  return canViewComments(userRoles)
}

export function canManageMessages(userRoles) {
  return canManageComments(userRoles)
}
