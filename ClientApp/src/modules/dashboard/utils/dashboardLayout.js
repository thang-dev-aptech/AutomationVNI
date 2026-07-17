import { hasRole, ROLES } from '@/shared/auth/permissions'

function formatCount(value) {
  return value === null || value === undefined ? null : value
}

export function buildStatCards(stats, roles) {
  const posts = stats?.posts ?? {}
  const channels = stats?.channels ?? {}
  const media = stats?.media ?? {}
  const jobs = stats?.jobs ?? {}

  const cards = [
    {
      id: 'total-posts',
      label: 'Tổng bài viết',
      value: formatCount(posts.total),
      tone: 'neutral',
      to: '/posts',
    },
    {
      id: 'draft',
      label: 'Nháp',
      value: formatCount(posts.draft),
      tone: 'neutral',
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
      tone: 'danger',
      to: '/posts',
    },
    {
      id: 'failed-jobs',
      label: 'Jobs thất bại',
      value: formatCount(jobs.failedTotal),
      tone: 'danger',
      to: '/jobs',
    },
    {
      id: 'active-channels',
      label: 'Kênh hoạt động',
      value: formatCount(channels.active),
      tone: 'success',
      to: '/platforms',
      hidden: !hasRole(roles, [ROLES.ADMIN, ROLES.CONTENT_MANAGER, ROLES.VIEWER]),
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
    to: '/platforms',
    label: 'Platforms / Kênh',
    desc: 'Kết nối và quản lý page',
    visible: (p) => p.canViewPlatforms,
  },
  {
    to: '/posts',
    label: 'Bài viết',
    desc: 'Pipeline sinh & đăng bài AI',
    visible: (p) => p.canViewPosts,
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
