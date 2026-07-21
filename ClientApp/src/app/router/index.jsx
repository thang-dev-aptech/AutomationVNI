import { Navigate, Route, Routes } from 'react-router-dom'
import MainLayout from '@/app/layouts/MainLayout'
import AuthLayout from '@/app/layouts/AuthLayout'
import ProtectedRoute from '@/app/router/ProtectedRoute'
import GuestRoute from '@/app/router/GuestRoute'
import { ROUTE_ROLES } from '@/app/router/routeRoles'
import DashboardPage from '@/modules/dashboard/pages/DashboardPage'
import PlatformsPage from '@/modules/social-channels/pages/PlatformsPage'
import PostListPage from '@/modules/posts/pages/PostListPage'
import PostCreatePage from '@/modules/posts/pages/PostCreatePage'
import PostDetailPage from '@/modules/posts/pages/PostDetailPage'
import MediaPage from '@/modules/media/pages/MediaPage'
import JobsPage from '@/modules/jobs/pages/JobsPage'
import PromptTemplateListPage from '@/modules/prompt-templates/pages/PromptTemplateListPage'
import PageContextListPage from '@/modules/page-contexts/pages/PageContextListPage'
import BulkCreatePage from '@/modules/bulk/pages/BulkCreatePage'
import BatchProgressPage from '@/modules/bulk/pages/BatchProgressPage'
import CommentsInboxPage from '@/modules/comments/pages/CommentsInboxPage'
import PageMessagesInboxPage from '@/modules/messages/pages/PageMessagesInboxPage'
import LoginPage from '@/modules/auth/pages/LoginPage'
import ForbiddenPage from '@/shared/pages/ForbiddenPage'
import NotFoundPage from '@/shared/pages/NotFoundPage'
import DataDeletionPage from '@/shared/pages/DataDeletionPage'
import PrivacyPolicyPage from '@/shared/pages/PrivacyPolicyPage'
import TermsPage from '@/shared/pages/TermsPage'

export default function AppRouter() {
  return (
    <Routes>
      <Route path="/data-deletion" element={<DataDeletionPage />} />
      <Route path="/privacy" element={<PrivacyPolicyPage />} />
      <Route path="/terms" element={<TermsPage />} />

      <Route element={<GuestRoute />}>
        <Route element={<AuthLayout />}>
          <Route path="/login" element={<LoginPage />} />
        </Route>
      </Route>

      <Route element={<ProtectedRoute />}>
        <Route element={<MainLayout />}>
          <Route path="/forbidden" element={<ForbiddenPage />} />
          <Route index element={<Navigate to="/dashboard" replace />} />
          <Route path="/dashboard" element={<DashboardPage />} />
          <Route element={<ProtectedRoute allowedRoles={ROUTE_ROLES.platforms} />}>
            <Route path="/platforms" element={<PlatformsPage />} />
          </Route>
          <Route path="/posts" element={<PostListPage />} />
          <Route element={<ProtectedRoute allowedRoles={ROUTE_ROLES.postsCreate} />}>
            <Route path="/posts/create" element={<PostCreatePage />} />
            <Route path="/bulk" element={<BulkCreatePage />} />
            <Route path="/bulk/:batchId" element={<BatchProgressPage />} />
          </Route>
          <Route path="/posts/:id" element={<PostDetailPage />} />
          <Route path="/media" element={<MediaPage />} />
          <Route element={<ProtectedRoute allowedRoles={ROUTE_ROLES.templates} />}>
            <Route path="/prompt-templates" element={<PromptTemplateListPage />} />
            <Route path="/page-contexts" element={<PageContextListPage />} />
          </Route>
          <Route element={<ProtectedRoute allowedRoles={ROUTE_ROLES.jobs} />}>
            <Route path="/jobs" element={<JobsPage />} />
          </Route>
          <Route element={<ProtectedRoute allowedRoles={ROUTE_ROLES.comments} />}>
            <Route path="/comments" element={<CommentsInboxPage />} />
          </Route>
          <Route element={<ProtectedRoute allowedRoles={ROUTE_ROLES.messages} />}>
            <Route path="/messages" element={<PageMessagesInboxPage />} />
          </Route>
          <Route path="*" element={<NotFoundPage />} />
        </Route>
      </Route>

      <Route path="*" element={<NotFoundPage />} />
    </Routes>
  )
}
