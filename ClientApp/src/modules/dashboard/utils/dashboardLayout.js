import { hasRole, ROLES } from '@/shared/auth/permissions'

function formatCount(value) {
  return value === null || value === undefined ? null : value
}

export function buildStatCards(stats, roles) {
  const posts = stats?.posts ?? {}
  const channels = stats?.channels ?? {}
  const media = stats?.media ?? {}
  const jobs = stats?.jobs ?? {}
  const templates = stats?.templates ?? {}
  const pageContexts = stats?.pageContexts ?? {}
  const bulk = stats?.bulk ?? {}

  const cards = [
    {
      id: 'total-posts',
      label: 'Tổng bài viết',
      value: formatCount(posts.total),
      tone: 'neutral',
      to: '/posts',
    },
    {
      id: 'in-pipeline',
      label: 'Đang xử lý AI',
      value: formatCount(posts.inPipeline),
      hint: bulk.activeBatches > 0 ? `${bulk.activeBatches} batch đang chạy` : undefined,
      tone: 'info',
      to: '/posts',
    },
    {
      id: 'waiting-review',
      label: 'Chờ duyệt',
      value: formatCount(posts.waitingReview),
      tone: 'warning',
      to: '/posts',
      emphasized: hasRole(roles, [ROLES.REVIEWER, ROLES.ADMIN]),
    },
    {
      id: 'approved',
      label: 'Đã duyệt',
      value: formatCount(posts.approved),
      tone: 'info',
      to: '/posts',
    },
    {
      id: 'scheduled',
      label: 'Đã lên lịch',
      value: formatCount(posts.scheduled),
      tone: 'info',
      to: '/posts',
    },
    {
      id: 'published',
      label: 'Đã đăng',
      value: formatCount(posts.published),
      tone: 'success',
      to: '/posts',
    },
    {
      id: 'failed-posts',
      label: 'Bài thất bại',
      value: formatCount(posts.failed),
      hint: posts.needAction > 0 ? `${posts.needAction} bài cần sửa/thiếu media` : undefined,
      tone: 'danger',
      to: '/posts',
    },
    {
      id: 'failed-jobs',
      label: 'Jobs thất bại',
      value: formatCount(jobs.failedTotal),
      tone: 'danger',
      to: '/jobs',
      hidden: !hasRole(roles, [ROLES.ADMIN, ROLES.CONTENT_MANAGER]),
    },
    {
      id: 'active-channels',
      label: 'Kênh hoạt động',
      value: formatCount(channels.active),
      hint: channels.expiredCount > 0 ? `${channels.expiredCount} kênh token hết hạn` : undefined,
      tone: 'success',
      to: '/platforms',
      hidden: !hasRole(roles, [ROLES.ADMIN, ROLES.CONTENT_MANAGER, ROLES.VIEWER]),
    },
    {
      id: 'page-contexts',
      label: 'Page sẵn sàng',
      value: formatCount(pageContexts.ready),
      hint:
        pageContexts.missingChannels > 0
          ? `${pageContexts.missingChannels} page chưa có context`
          : pageContexts.total !== undefined
            ? `${pageContexts.total} context đã tạo`
            : undefined,
      tone: pageContexts.missingChannels > 0 ? 'warning' : 'success',
      to: '/page-contexts',
      hidden: !hasRole(roles, [ROLES.ADMIN, ROLES.CONTENT_MANAGER]),
    },
    {
      id: 'prompt-templates',
      label: 'Danh mục template',
      value: formatCount(templates.total),
      tone: 'neutral',
      to: '/prompt-templates',
      hidden: !hasRole(roles, [ROLES.ADMIN, ROLES.CONTENT_MANAGER]),
    },
    {
      id: 'media-assets',
      label: 'Media assets',
      value: formatCount(media.total),
      tone: 'neutral',
      to: '/media',
    },
  ]

  if (hasRole(roles, [ROLES.CONTENT_MANAGER]) && posts.myRecentCount > 0) {
    cards.splice(1, 0, {
      id: 'my-recent-posts',
      label: 'Bài của tôi (gần đây)',
      value: posts.myRecentCount,
      tone: 'info',
      to: '/posts',
      hint: 'Trong 20 bài mới nhất',
      emphasized: true,
    })
  }

  const visible = cards.filter((card) => !card.hidden)

  if (hasRole(roles, [ROLES.REVIEWER])) {
    const waitingIndex = visible.findIndex((card) => card.id === 'waiting-review')
    if (waitingIndex > 0) {
      const [waitingCard] = visible.splice(waitingIndex, 1)
      visible.unshift(waitingCard)
    }
  }

  return visible
}

export const DASHBOARD_QUICK_LINKS = [
  {
    to: '/posts/create',
    label: 'Tạo bài viết',
    desc: 'Nhập chủ đề, chọn page — AI sinh nội dung',
    visible: (p) => p.canViewPosts,
  },
  {
    to: '/bulk',
    label: 'Tạo hàng loạt',
    desc: 'Nhiều ý tưởng × nhiều page, import CSV',
    visible: (p) => p.canViewPosts,
  },
  {
    to: '/comments',
    label: 'Hộp thư comment',
    desc: 'Trả lời và kiểm duyệt Facebook / Threads',
    visible: (p) => p.canViewComments,
  },
  {
    to: '/messages',
    label: 'Tin nhắn Page',
    desc: 'Đọc, trả lời và phân công hội thoại Messenger',
    visible: (p) => p.canViewMessages,
  },
  {
    to: '/platforms',
    label: 'Platforms / Kênh',
    desc: 'Kết nối và quản lý page',
    visible: (p) => p.canViewPlatforms,
  },
  {
    to: '/page-contexts',
    label: 'Page Context',
    desc: 'Branding, danh mục mặc định, CTA từng page',
    visible: (p) => p.canManageTemplates,
  },
  {
    to: '/prompt-templates',
    label: 'Danh mục template',
    desc: 'Template prompt text + ảnh theo ngành',
    visible: (p) => p.canManageTemplates,
  },
  {
    to: '/media',
    label: 'Media',
    desc: 'Kho ảnh nội bộ',
    visible: (p) => p.canViewMedia,
  },
  {
    to: '/jobs',
    label: 'Jobs',
    desc: 'Queue xử lý background',
    visible: (p) => p.canViewJobs,
  },
]
