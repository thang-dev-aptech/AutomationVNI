import { useMemo } from 'react'
import { useAuthStore } from '@/modules/auth/stores/authStore'
import * as permissions from '@/shared/auth/permissions'

export function usePermissions() {
  const roles = useAuthStore((state) => state.roles)
  const currentUser = useAuthStore((state) => state.currentUser)

  return useMemo(
    () => ({
      roles,
      currentUser,
      canManageChannels: permissions.canManageChannels(roles),
      canViewPlatforms: permissions.canViewPlatforms(roles),
      canCreatePost: permissions.canCreatePost(roles),
      canSubmitReview: permissions.canSubmitReview(roles),
      canApprovePost: permissions.canApprovePost(roles),
      canRejectPost: permissions.canRejectPost(roles),
      canSchedulePost: permissions.canSchedulePost(roles),
      canPublishPost: permissions.canPublishPost(roles),
      canManageMedia: permissions.canManageMedia(roles),
      canManageTemplates: permissions.canManageTemplates(roles),
      canManageJobs: permissions.canManageJobs(roles),
      canViewJobs: permissions.canViewJobs(roles),
      canViewPosts: permissions.canViewPosts(roles),
      canViewMedia: permissions.canViewMedia(roles),
      canViewDashboard: permissions.canViewDashboard(roles),
      canViewComments: permissions.canViewComments(roles),
      canManageComments: permissions.canManageComments(roles),
      canViewMessages: permissions.canViewMessages(roles),
      canManageMessages: permissions.canManageMessages(roles),
      canEditPost: (postUserId) =>
        permissions.canEditPost(roles, postUserId, currentUser?.id),
      canDeletePost: (postUserId) =>
        permissions.canDeletePost(roles, postUserId, currentUser?.id),
      canDeleteAllPosts: permissions.canDeleteAllPosts(roles),
      hasRole: (allowedRoles) => permissions.hasRole(roles, allowedRoles),
    }),
    [roles, currentUser],
  )
}
