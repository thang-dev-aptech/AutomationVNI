import { useCallback, useMemo } from 'react'
import { useSearchParams } from 'react-router-dom'
import { useQuery } from '@tanstack/react-query'
import { unwrapApiData } from '@/shared/utils/apiHelpers'
import { postApi, postQueryKeys } from '../services/postApi'
import { monthRangeUtc } from '../utils/calendarGrid'
import {
  CALENDAR_VIEWS,
  CHANNEL_FILTER_MODES,
  DEFAULT_STATUS_KEYS,
  expandStatusKeys,
} from '../constants/calendarStatus'

function csv(list) {
  return (list ?? []).filter(Boolean).join(',')
}

function parseCsv(value) {
  if (!value) return []
  return value.split(',').map((s) => s.trim()).filter(Boolean)
}

function currentVnMonth() {
  const parts = new Intl.DateTimeFormat('en-CA', {
    timeZone: 'Asia/Ho_Chi_Minh',
    year: 'numeric',
    month: '2-digit',
  }).formatToParts(new Date())
  const get = (type) => Number(parts.find((p) => p.type === type)?.value)
  return { year: get('year'), month: get('month') }
}

function parseView(raw) {
  if (raw === CALENDAR_VIEWS.list || raw === CALENDAR_VIEWS.byChannel) return raw
  return CALENDAR_VIEWS.calendar
}

function parseChannelMode(raw) {
  return raw === CHANNEL_FILTER_MODES.group
    ? CHANNEL_FILTER_MODES.group
    : CHANNEL_FILTER_MODES.channel
}

function parseStatuses(raw) {
  const keys = parseCsv(raw)
  if (keys.length === 0) return [...DEFAULT_STATUS_KEYS]
  return keys
}

/**
 * Đọc/ghi trạng thái khung lịch trên URL (view, khoảng tháng, bộ lọc).
 * Cùng một filterRequest cho calendar / list / facets.
 */
export function useCalendarQuery() {
  const [searchParams, setSearchParams] = useSearchParams()
  const fallbackMonth = useMemo(() => currentVnMonth(), [])

  const view = parseView(searchParams.get('view'))
  const channelMode = parseChannelMode(searchParams.get('channelMode'))
  const year = Number(searchParams.get('year')) || fallbackMonth.year
  const month = Number(searchParams.get('month')) || fallbackMonth.month
  const statusKeys = parseStatuses(searchParams.get('status'))
  const channelIds = parseCsv(searchParams.get('channels'))
  const groupIds = parseCsv(searchParams.get('groups'))
  const authorIds = parseCsv(searchParams.get('authors'))
  const categoryIds = parseCsv(searchParams.get('categories'))
  const postTypes = parseCsv(searchParams.get('types'))
  const keyword = searchParams.get('q') ?? ''

  const patchParams = useCallback((patch, { replace = false } = {}) => {
    setSearchParams((prev) => {
      const next = new URLSearchParams(prev)
      Object.entries(patch).forEach(([key, value]) => {
        if (value === undefined || value === null || value === ''
          || (Array.isArray(value) && value.length === 0)) {
          next.delete(key)
          return
        }
        next.set(key, Array.isArray(value) ? csv(value) : String(value))
      })
      return next
    }, { replace })
  }, [setSearchParams])

  const setView = useCallback((nextView) => {
    patchParams({ view: nextView === CALENDAR_VIEWS.calendar ? undefined : nextView })
  }, [patchParams])

  const setChannelMode = useCallback((mode) => {
    patchParams({
      channelMode: mode === CHANNEL_FILTER_MODES.group ? mode : undefined,
      // Đổi radio: không mang theo tập lọc của chế độ kia
      channels: mode === CHANNEL_FILTER_MODES.channel ? channelIds : undefined,
      groups: mode === CHANNEL_FILTER_MODES.group ? groupIds : undefined,
    })
  }, [patchParams, channelIds, groupIds])

  const setMonthCursor = useCallback((nextYear, nextMonth) => {
    const fb = currentVnMonth()
    const isDefault = nextYear === fb.year && nextMonth === fb.month
    patchParams({
      year: isDefault ? undefined : nextYear,
      month: isDefault ? undefined : nextMonth,
    }, { replace: true })
  }, [patchParams])

  const setStatusKeys = useCallback((keys) => {
    const next = [...keys]
    const isDefault = next.length === DEFAULT_STATUS_KEYS.length
      && DEFAULT_STATUS_KEYS.every((k) => next.includes(k))
    patchParams({ status: isDefault ? undefined : next })
  }, [patchParams])

  const toggleStatusKey = useCallback((key) => {
    const next = statusKeys.includes(key)
      ? statusKeys.filter((k) => k !== key)
      : [...statusKeys, key]
    setStatusKeys(next)
  }, [statusKeys, setStatusKeys])

  const setChannelIds = useCallback((ids) => {
    patchParams({ channels: ids })
  }, [patchParams])

  const toggleChannelId = useCallback((id) => {
    const next = channelIds.includes(id)
      ? channelIds.filter((x) => x !== id)
      : [...channelIds, id]
    setChannelIds(next)
  }, [channelIds, setChannelIds])

  const setGroupIds = useCallback((ids) => {
    patchParams({ groups: ids })
  }, [patchParams])

  const toggleGroupId = useCallback((id) => {
    const next = groupIds.includes(id)
      ? groupIds.filter((x) => x !== id)
      : [...groupIds, id]
    setGroupIds(next)
  }, [groupIds, setGroupIds])

  const toggleAuthorId = useCallback((id) => {
    const next = authorIds.includes(id)
      ? authorIds.filter((x) => x !== id)
      : [...authorIds, id]
    patchParams({ authors: next })
  }, [authorIds, patchParams])

  const toggleCategoryId = useCallback((id) => {
    const next = categoryIds.includes(id)
      ? categoryIds.filter((x) => x !== id)
      : [...categoryIds, id]
    patchParams({ categories: next })
  }, [categoryIds, patchParams])

  const togglePostType = useCallback((type) => {
    const next = postTypes.includes(type)
      ? postTypes.filter((x) => x !== type)
      : [...postTypes, type]
    patchParams({ types: next })
  }, [postTypes, patchParams])

  const setKeyword = useCallback((q) => {
    patchParams({ q: q?.trim() ? q : undefined }, { replace: true })
  }, [patchParams])

  const range = useMemo(() => monthRangeUtc(year, month), [year, month])

  const filterRequest = useMemo(() => {
    const statuses = expandStatusKeys(statusKeys)
    return {
      fromUtc: range.fromUtc,
      toUtc: range.toUtc,
      statuses: statuses.length > 0 ? statuses : undefined,
      socialChannelIds: channelMode === CHANNEL_FILTER_MODES.channel && channelIds.length > 0
        ? channelIds
        : undefined,
      channelGroupIds: channelMode === CHANNEL_FILTER_MODES.group && groupIds.length > 0
        ? groupIds
        : undefined,
      authors: authorIds.length > 0 ? authorIds : undefined,
      categoryIds: categoryIds.length > 0 ? categoryIds : undefined,
      postTypes: postTypes.length > 0 ? postTypes : undefined,
      keyword: keyword.trim() || undefined,
    }
  }, [
    range, statusKeys, channelMode, channelIds, groupIds,
    authorIds, categoryIds, postTypes, keyword,
  ])

  return {
    view,
    setView,
    channelMode,
    setChannelMode,
    year,
    month,
    setMonthCursor,
    statusKeys,
    setStatusKeys,
    toggleStatusKey,
    channelIds,
    setChannelIds,
    toggleChannelId,
    groupIds,
    setGroupIds,
    toggleGroupId,
    authorIds,
    toggleAuthorId,
    categoryIds,
    toggleCategoryId,
    postTypes,
    togglePostType,
    keyword,
    setKeyword,
    filterRequest,
    searchParams,
  }
}

export function useCalendarPosts(request, { enabled = true } = {}) {
  return useQuery({
    queryKey: postQueryKeys.calendar(request),
    queryFn: async () => unwrapApiData(await postApi.calendar(request)),
    enabled: Boolean(enabled && request?.fromUtc && request?.toUtc),
  })
}

export function useCalendarList(request, { enabled = true } = {}) {
  return useQuery({
    queryKey: postQueryKeys.calendarList(request),
    queryFn: async () => unwrapApiData(await postApi.calendarList(request)),
    enabled: Boolean(enabled && request?.fromUtc && request?.toUtc),
  })
}

export function useCalendarFacets(request, { enabled = true } = {}) {
  return useQuery({
    queryKey: postQueryKeys.calendarFacets(request),
    queryFn: async () => unwrapApiData(await postApi.calendarFacets(request)),
    enabled: Boolean(enabled && request?.fromUtc && request?.toUtc),
  })
}
