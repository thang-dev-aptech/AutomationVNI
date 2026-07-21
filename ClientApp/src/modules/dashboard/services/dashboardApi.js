import axiosInstance from '@/api/axiosInstance'
import { postApi } from '@/modules/posts/services/postApi'
import { socialChannelApi } from '@/modules/social-channels/services/socialChannelApi'
import { mediaAssetApi } from '@/modules/media/services/mediaAssetApi'
import { generationJobApi } from '@/modules/jobs/services/generationJobApi'
import { publishLogApi } from '@/modules/jobs/services/publishLogApi'
import { promptTemplateApi } from '@/modules/prompt-templates/services/promptTemplateApi'
import { pageContextApi } from '@/modules/page-contexts/services/pageContextApi'
import { unwrapApiData } from '@/shared/utils/apiHelpers'
import { hasRole, ROLES } from '@/shared/auth/permissions'

const COUNT_PAGE = { index: 1, size: 1 }

function devLog(message, error) {
  if (import.meta.env.DEV) {
    console.debug(`[dashboard] ${message}`, error?.message || error)
  }
}

async function safePagedTotal(fetcher, params, label) {
  try {
    const data = unwrapApiData(await fetcher(params))
    return {
      total: data?.total ?? 0,
      items: data?.items ?? [],
      available: true,
    }
  } catch (error) {
    devLog(`${label} failed`, error)
    return { total: null, items: [], available: false, error }
  }
}

async function trySummaryEndpoint() {
  try {
    const response = await axiosInstance.get('/api/Dashboard/summary')
    return unwrapApiData(response)
  } catch (error) {
    if (error?.response?.status === 404) return null
    devLog('summary endpoint unavailable', error)
    return null
  }
}

function isChannelExpired(channel) {
  if (!channel?.tokenExpiresAt) return false
  return new Date(channel.tokenExpiresAt) < new Date()
}

function prioritizeRecentPosts(posts, userId, roles) {
  if (!hasRole(roles, [ROLES.CONTENT_MANAGER]) || !userId || !posts?.length) {
    return posts.slice(0, 5)
  }

  const own = posts.filter((post) => String(post.userId) === String(userId))
  const others = posts.filter((post) => String(post.userId) !== String(userId))
  return [...own, ...others].slice(0, 5)
}

function countOwnPosts(posts, userId) {
  if (!userId || !posts?.length) return 0
  return posts.filter((post) => String(post.userId) === String(userId)).length
}

async function aggregateFromExistingApis({ userId, roles } = {}) {
  const [
    postsTotal,
    postsDraft,
    postsWaitingReview,
    postsApproved,
    postsScheduled,
    postsPublished,
    postsFailed,
    postsRecent,
    channelsActive,
    channelsInactive,
    channelsAll,
    mediaTotal,
    jobsPending,
    jobsRunning,
    jobsFailed,
    jobsDeadLetter,
    publishFailed,
    publishFailedRecent,
    templatesTotal,
    pageContextsAll,
  ] = await Promise.all([
    safePagedTotal((p) => postApi.filter(p), COUNT_PAGE, 'posts total'),
    safePagedTotal((p) => postApi.filter(p), { ...COUNT_PAGE, status: 1 }, 'posts draft'),
    safePagedTotal((p) => postApi.filter(p), { ...COUNT_PAGE, status: 10 }, 'posts waiting review'),
    safePagedTotal((p) => postApi.filter(p), { ...COUNT_PAGE, status: 11 }, 'posts approved'),
    safePagedTotal((p) => postApi.filter(p), { ...COUNT_PAGE, status: 5 }, 'posts scheduled'),
    safePagedTotal((p) => postApi.filter(p), { ...COUNT_PAGE, status: 7 }, 'posts published'),
    safePagedTotal((p) => postApi.filter(p), { ...COUNT_PAGE, status: 8 }, 'posts failed'),
    safePagedTotal((p) => postApi.filter(p), { index: 1, size: 20 }, 'posts recent'),
    safePagedTotal(
      (p) => socialChannelApi.filter(p),
      { ...COUNT_PAGE, isActive: true },
      'channels active',
    ),
    safePagedTotal(
      (p) => socialChannelApi.filter(p),
      { ...COUNT_PAGE, isActive: false },
      'channels inactive',
    ),
    safePagedTotal(
      (p) => socialChannelApi.filter(p),
      { index: 1, size: 100 },
      'channels all',
    ),
    safePagedTotal((p) => mediaAssetApi.filter(p), COUNT_PAGE, 'media total'),
    safePagedTotal(
      (p) => generationJobApi.filter(p),
      { ...COUNT_PAGE, status: 1 },
      'jobs pending',
    ),
    safePagedTotal(
      (p) => generationJobApi.filter(p),
      { ...COUNT_PAGE, status: 2 },
      'jobs running',
    ),
    safePagedTotal(
      (p) => generationJobApi.filter(p),
      { ...COUNT_PAGE, status: 4 },
      'jobs failed',
    ),
    safePagedTotal(
      (p) => generationJobApi.filter(p),
      { ...COUNT_PAGE, status: 7 },
      'jobs dead letter',
    ),
    safePagedTotal(
      (p) => publishLogApi.filter(p),
      { ...COUNT_PAGE, status: 2 },
      'publish logs failed',
    ),
    safePagedTotal(
      (p) => publishLogApi.filter(p),
      { index: 1, size: 5, status: 2 },
      'publish logs failed recent',
    ),
    safePagedTotal(
      (p) => promptTemplateApi.filter(p),
      { ...COUNT_PAGE, isActive: true },
      'templates total',
    ),
    safePagedTotal(
      (p) => pageContextApi.filter(p),
      { index: 1, size: 100 },
      'page contexts all',
    ),
  ])

  const channelItems = channelsAll.items ?? []
  const expiredChannels = channelItems.filter(isChannelExpired)
  const recentPosts = prioritizeRecentPosts(postsRecent.items ?? [], userId, roles)

  const contextItems = pageContextsAll.items ?? []
  const contextReadyCount = contextItems.filter(
    (c) => c.defaultTextTemplateId || c.defaultImageTemplateId || c.promptTemplateText,
  ).length
  const channelIdsWithContext = new Set(contextItems.map((c) => c.socialChannelId))
  const missingContextChannels = channelItems.filter(
    (c) => c.isActive !== false && !channelIdsWithContext.has(c.id),
  ).length

  const sections = {
    posts: postsTotal.available,
    channels: channelsActive.available || channelsAll.available,
    media: mediaTotal.available,
    jobs: jobsPending.available,
    publishLogs: publishFailed.available,
  }

  const partialErrors = []
  if (!postsTotal.available) partialErrors.push('posts')
  if (!channelsActive.available) partialErrors.push('channels')
  if (!mediaTotal.available) partialErrors.push('media')
  if (!jobsPending.available) partialErrors.push('jobs')
  if (!publishFailed.available) partialErrors.push('publishLogs')

  const failedJobsTotal =
    jobsFailed.total === null && jobsDeadLetter.total === null
      ? null
      : (jobsFailed.total ?? 0) + (jobsDeadLetter.total ?? 0)

  return {
    source: 'aggregate',
    posts: {
      total: postsTotal.total,
      draft: postsDraft.total,
      waitingReview: postsWaitingReview.total,
      approved: postsApproved.total,
      scheduled: postsScheduled.total,
      published: postsPublished.total,
      failed: postsFailed.total,
      recent: recentPosts,
      myRecentCount: countOwnPosts(postsRecent.items ?? [], userId),
      available: postsTotal.available,
    },
    channels: {
      active: channelsActive.total,
      inactive: channelsInactive.total,
      total: channelsAll.total,
      expired: expiredChannels,
      expiredCount: expiredChannels.length,
      available: channelsActive.available || channelsAll.available,
    },
    media: {
      total: mediaTotal.total,
      available: mediaTotal.available,
    },
    jobs: {
      pending: jobsPending.total,
      running: jobsRunning.total,
      failed: jobsFailed.total,
      deadLetter: jobsDeadLetter.total,
      failedTotal: failedJobsTotal,
      available: jobsPending.available,
    },
    publishLogs: {
      failed: publishFailed.total,
      recentFailed: publishFailedRecent.items ?? [],
      available: publishFailed.available,
    },
    templates: {
      total: templatesTotal.total,
      available: templatesTotal.available,
    },
    pageContexts: {
      total: pageContextsAll.total,
      ready: pageContextsAll.available ? contextReadyCount : null,
      missingChannels: pageContextsAll.available && channelsAll.available
        ? missingContextChannels
        : null,
      available: pageContextsAll.available,
    },
    bulk: { activeBatches: null, available: false },
    sections,
    partialErrors,
  }
}

export const dashboardQueryKeys = {
  stats: (userId, roles) => ['dashboard', 'stats', userId, roles],
}

export const dashboardApi = {
  async fetchStats({ userId, roles } = {}) {
    const summary = await trySummaryEndpoint()
    if (summary) {
      // BE trả 20 bài gần nhất; FE tự ưu tiên bài của user và cắt còn 5.
      const recentRaw = summary.posts?.recent ?? []
      return {
        ...summary,
        posts: {
          ...summary.posts,
          recent: prioritizeRecentPosts(recentRaw, userId, roles),
          myRecentCount: summary.posts?.myRecentCount ?? countOwnPosts(recentRaw, userId),
        },
        source: 'summary',
      }
    }
    return aggregateFromExistingApis({ userId, roles })
  },
}
