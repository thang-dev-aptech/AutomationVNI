import { useCallback, useMemo } from 'react'
import { useSearchParams } from 'react-router-dom'

export function useConversationParams() {
  const [searchParams, setSearchParams] = useSearchParams()

  const selectedId = searchParams.get('id') || null
  const paramKind = searchParams.get('kind') || null
  const selectedKind = paramKind || (selectedId ? 'message' : null)

  const filters = useMemo(() => {
    const search = searchParams.get('search') || ''

    // Kinds: if kinds param exists use it; otherwise if kind param exists and no id, use [kind]
    const kindsRaw = searchParams.get('kinds')
    let kinds = []
    if (kindsRaw) {
      kinds = kindsRaw.split(',').filter(Boolean)
    } else if (paramKind && !selectedId) {
      kinds = [paramKind]
    }

    const statusesRaw = searchParams.get('statuses')
    const statuses = statusesRaw
      ? statusesRaw.split(',').map((s) => Number.parseInt(s, 10)).filter((n) => !Number.isNaN(n))
      : []

    const channelsRaw = searchParams.get('channels') || searchParams.get('socialChannelIds')
    const socialChannelIds = channelsRaw ? channelsRaw.split(',').filter(Boolean) : []

    const groupsRaw = searchParams.get('groups') || searchParams.get('channelGroupIds')
    const channelGroupIds = groupsRaw ? groupsRaw.split(',').filter(Boolean) : []

    const unreadOnly = searchParams.get('unread') === 'true' || searchParams.get('unread') === '1'

    const fromUtc = searchParams.get('from') || null
    const toUtc = searchParams.get('to') || null

    const assignedRaw = searchParams.get('assigned') || searchParams.get('assignedUserIds')
    const assignedUserIds = assignedRaw ? assignedRaw.split(',').filter(Boolean) : []

    const customerUnansweredOnly =
      searchParams.get('unanswered') === 'true' || searchParams.get('unanswered') === '1'

    const openWindowOnly =
      searchParams.get('openWindow') === 'true' || searchParams.get('openWindow') === '1'

    const index = Number.parseInt(searchParams.get('page') || searchParams.get('index') || '1', 10) || 1
    const size = Number.parseInt(searchParams.get('size') || '20', 10) || 20

    return {
      search,
      kinds,
      statuses,
      socialChannelIds,
      channelGroupIds,
      unreadOnly,
      fromUtc,
      toUtc,
      assignedUserIds,
      customerUnansweredOnly,
      openWindowOnly,
      index,
      size,
    }
  }, [searchParams, paramKind, selectedId])

  const updateParams = useCallback(
    (updater) => {
      setSearchParams(
        (prev) => {
          const next = new URLSearchParams(prev)
          updater(next)
          return next
        },
        { replace: true },
      )
    },
    [setSearchParams],
  )

  const setSelectedConversation = useCallback(
    (id, kind) => {
      updateParams((params) => {
        if (!id) {
          params.delete('id')
          // If we had a kind specifically for this conversation, remove it unless kinds filter relies on it
          if (filters.kinds.length === 0) {
            params.delete('kind')
          }
        } else {
          params.set('id', id)
          if (kind) {
            params.set('kind', kind)
          }
        }
      })
    },
    [updateParams, filters.kinds],
  )

  const updateFilter = useCallback(
    (key, value) => {
      updateParams((params) => {
        // Reset page on filter change
        if (key !== 'page' && key !== 'index') {
          params.delete('page')
          params.delete('index')
        }

        switch (key) {
          case 'search':
            if (value) params.set('search', value)
            else params.delete('search')
            break
          case 'kinds':
            if (Array.isArray(value) && value.length > 0) {
              params.set('kinds', value.join(','))
              if (!params.get('id') && value.length === 1) {
                params.set('kind', value[0])
              }
            } else {
              params.delete('kinds')
              if (!params.get('id')) params.delete('kind')
            }
            break
          case 'statuses':
            if (Array.isArray(value) && value.length > 0) {
              params.set('statuses', value.join(','))
            } else {
              params.delete('statuses')
            }
            break
          case 'socialChannelIds':
          case 'channels':
            if (Array.isArray(value) && value.length > 0) {
              params.set('channels', value.join(','))
            } else {
              params.delete('channels')
              params.delete('socialChannelIds')
            }
            break
          case 'channelGroupIds':
          case 'groups':
            if (Array.isArray(value) && value.length > 0) {
              params.set('groups', value.join(','))
            } else {
              params.delete('groups')
              params.delete('channelGroupIds')
            }
            break
          case 'unreadOnly':
          case 'unread':
            if (value) params.set('unread', 'true')
            else params.delete('unread')
            break
          case 'fromUtc':
          case 'from':
            if (value) params.set('from', value)
            else params.delete('from')
            break
          case 'toUtc':
          case 'to':
            if (value) params.set('to', value)
            else params.delete('to')
            break
          case 'assignedUserIds':
          case 'assigned':
            if (Array.isArray(value) && value.length > 0) {
              params.set('assigned', value.join(','))
            } else {
              params.delete('assigned')
              params.delete('assignedUserIds')
            }
            break
          case 'customerUnansweredOnly':
          case 'unanswered':
            if (value) params.set('unanswered', 'true')
            else params.delete('unanswered')
            break
          case 'openWindowOnly':
          case 'openWindow':
            if (value) params.set('openWindow', 'true')
            else params.delete('openWindow')
            break
          case 'index':
          case 'page':
            if (value && value > 1) params.set('page', String(value))
            else params.delete('page')
            break
          default:
            if (value != null && value !== '') params.set(key, String(value))
            else params.delete(key)
        }
      })
    },
    [updateParams],
  )

  const clearFilters = useCallback(() => {
    updateParams((params) => {
      params.delete('search')
      params.delete('kinds')
      params.delete('statuses')
      params.delete('channels')
      params.delete('socialChannelIds')
      params.delete('groups')
      params.delete('channelGroupIds')
      params.delete('unread')
      params.delete('from')
      params.delete('to')
      params.delete('assigned')
      params.delete('assignedUserIds')
      params.delete('unanswered')
      params.delete('openWindow')
      params.delete('page')
      params.delete('index')
      if (!params.get('id')) {
        params.delete('kind')
      }
    })
  }, [updateParams])

  return {
    filters,
    selectedId,
    selectedKind,
    updateFilter,
    clearFilters,
    setSelectedConversation,
  }
}
