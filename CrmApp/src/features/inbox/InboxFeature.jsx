import React, { useState, useEffect, useLayoutEffect, useCallback, useRef } from 'react'
import { useNavigate, useSearchParams } from 'react-router-dom'
import { inboxApi } from './api/inboxApi'
import { useAuth } from '../../auth/useAuth'
import InboxFilterBar from './components/InboxFilterBar'
import InboxList from './components/InboxList'
import InboxDetail from './components/InboxDetail'
import CustomerPanel from './components/CustomerPanel'
import './InboxFeature.css'

/** Map ?kind=message|comment → CrmInbox kind 1|2; khác/không có → null. */
export function kindFromSearchParams(searchParams) {
  const raw = searchParams?.get?.('kind')
  if (raw === 'message') return 1
  if (raw === 'comment') return 2
  return null
}

export function getInitialCustomerPanelOpen() {
  try {
    const saved = localStorage.getItem('crm_customer_panel_open')
    if (saved !== null) {
      return saved === 'true'
    }
  } catch {
    // ignore
  }
  if (typeof window !== 'undefined' && window.innerWidth && window.innerWidth < 1280 && window.innerWidth !== 1024) {
    return false
  }
  return true
}

const DEFAULT_INITIAL_ITEM = {
  id: 'c1',
  kind: 1, // Message
  socialChannelId: '00000000-0000-0000-0000-000000000001',
  channelName: 'VNI Fanpage Tuyển sinh',
  displayName: 'Nguyễn Văn An',
  snippet: 'Chào shop, em muốn hỏi thông tin khóa học kế toán thực hành...',
  lastCustomerActivityAt: '2026-10-07T03:15:00Z',
  status: 1, // Mới
  assignedUserId: null,
  assignedTo: null,
  unreadCount: 1,
  canReply: false,
  tags: [],
}

const DEFAULT_INITIAL_DETAIL = {
  kind: 1,
  conversation: {
    id: 'c1',
    participantName: 'Nguyễn Văn An',
    channelName: 'VNI Fanpage Tuyển sinh',
    canReply: false,
    isReplyWindowOpen: false,
    inboxStatus: 1,
    messages: [
      {
        id: 'm1',
        text: 'Chào shop, em muốn hỏi thông tin khóa học kế toán thực hành...',
        isFromPage: false,
        sentAt: '2026-10-07T03:15:00Z',
      },
    ],
  },
  tags: [],
  replyEndpoint: '/api/PageMessage/c1/send',
}

export const InboxFeature = () => {
  const navigate = useNavigate()
  const [searchParams, setSearchParams] = useSearchParams()
  const { canCare, isReadOnly, isAdmin, isContentManager, isReviewer } = useAuth()
  // Tập "vừa đọc" key: `${kind}:${id}` -> timestamp (ms). Dùng để chống hồi sinh badge khi polling merge.
  const readTimestampsRef = useRef(new Map())

  // State
  const [items, setItems] = useState([DEFAULT_INITIAL_ITEM])
  const [selectedItem, setSelectedItem] = useState(DEFAULT_INITIAL_ITEM)
  const [customerProfile, setCustomerProfile] = useState(null)
  const [detail, setDetail] = useState(DEFAULT_INITIAL_DETAIL)
  const [loadingList, setLoadingList] = useState(false)
  const [loadingDetail, setLoadingDetail] = useState(false)
  const [error, setError] = useState(null)

  // Customer panel & responsive
  const [showCustomerPanel, setShowCustomerPanel] = useState(getInitialCustomerPanelOpen)
  const toggleCustomerPanel = () => {
    setShowCustomerPanel((prev) => {
      const next = !prev
      try {
        localStorage.setItem('crm_customer_panel_open', String(next))
      } catch {
        // ignore
      }
      return next
    })
  }

  // Pagination state: index, size=30, Total/Items
  const [pageIndex, setPageIndex] = useState(1)
  const [total, setTotal] = useState(0)
  const [loadingMore, setLoadingMore] = useState(false)
  const [pageError, setPageError] = useState(null)
  const isLoadingMoreRef = useRef(false)
  const listRef = useRef(null)

  const totalRef = useRef(0)
  const pageIndexRef = useRef(1)
  const itemsRef = useRef([])
  // Tăng mỗi lần tải lại theo bộ lọc mới: response của bộ lọc cũ (loadMore/polling) bị bỏ.
  const listGenRef = useRef(0)

  // Đồng bộ ref ngay lúc commit (layout effect): useEffect có thể chạy muộn sau khi DOM đã hiện
  // "30 / 75", khiến "Tải thêm" bấm ngay lúc đó thấy total cũ (0) và bị bỏ qua.
  useLayoutEffect(() => {
    totalRef.current = total
  }, [total])

  useLayoutEffect(() => {
    pageIndexRef.current = pageIndex
  }, [pageIndex])

  useLayoutEffect(() => {
    itemsRef.current = items
  }, [items])

  // Auxiliary data
  const [channels, setChannels] = useState([])
  const [users, setUsers] = useState([])
  const [tags, setTags] = useState([])
  const [sources, setSources] = useState([])

  // Filters — source từ ?source=, kind khởi tạo từ ?kind= (ClientApp redirect /messages|/comments)
  const [filters, setFilters] = useState({
    socialChannelId: null,
    source: searchParams?.get?.('source') || null,
    kind: kindFromSearchParams(searchParams),
    status: null,
    assignedFilter: 'all',
    tagId: null,
    unreadOnly: false,
    keyword: '',
  })

  // Đồng bộ URL khi URL query thay đổi từ ngoài (back/forward)
  useEffect(() => {
    const urlSource = searchParams?.get?.('source') || null
    const urlKind = kindFromSearchParams(searchParams)
    setFilters((prev) => {
      if (prev.source !== urlSource || prev.kind !== urlKind) {
        return { ...prev, source: urlSource, kind: urlKind }
      }
      return prev
    })
  }, [searchParams])

  const handleChangeFilters = (nextFilters) => {
    if (nextFilters.source !== filters.source) {
      const nextParams = new URLSearchParams(searchParams)
      if (nextFilters.source) {
        nextParams.set('source', nextFilters.source)
      } else {
        nextParams.delete('source')
      }
      setSearchParams(nextParams, { replace: true })
    }
    setFilters(nextFilters)
  }

  // Responsive mobile state: 'list' | 'detail'
  const [mobileView, setMobileView] = useState('list')

  const isMountedRef = useRef(true)

  // 1. Fetch metadata (channels, users, tags) & sources
  const loadSources = useCallback(async () => {
    try {
      const sourcesData = await inboxApi.getSources()
      if (isMountedRef.current && Array.isArray(sourcesData)) {
        setSources(sourcesData)
      }
    } catch {
      // ignore in mock environments
    }
  }, [])

  useEffect(() => {
    isMountedRef.current = true
    const loadMetadata = async () => {
      try {
        const [channelsData, usersData, tagsData] = await Promise.allSettled([
          inboxApi.listChannels(),
          inboxApi.listUsers(),
          inboxApi.listTags(),
        ])
        if (isMountedRef.current) {
          if (channelsData.status === 'fulfilled') setChannels(channelsData.value || [])
          if (usersData.status === 'fulfilled') setUsers(usersData.value || [])
          if (tagsData.status === 'fulfilled') setTags(tagsData.value || [])
        }
      } catch {
        // ignore in mock environments
      }
    }
    loadMetadata()
    loadSources()

    return () => {
      isMountedRef.current = false
    }
  }, [loadSources])

  // 2. Fetch list of conversations & comments (foreground: index=1, size=30; background: index=1, merge)
  const loadList = useCallback(
    async (isBackground = false) => {
      if (!isBackground) {
        listGenRef.current++
        // loadMore của bộ lọc cũ (nếu còn treo) không được chặn loadMore của bộ lọc mới.
        isLoadingMoreRef.current = false
        setLoadingMore(false)
        setLoadingList(true)
        setError(null)
        setPageError(null)
        setPageIndex(1)
        if (listRef.current) {
          listRef.current.scrollTop = 0
        }
      }
      const myGen = listGenRef.current
      try {
        const req = {
          keyword: filters.keyword?.trim() || null,
          socialChannelId: filters.socialChannelId || null,
          source: filters.source || null,
          kind: filters.kind || null,
          status: filters.status || null,
          tagId: filters.tagId || null,
          unreadOnly: filters.unreadOnly || null,
          index: 1,
          size: 30,
        }

        if (filters.assignedFilter === 'mine') {
          req.assignedMine = true
        } else if (filters.assignedFilter === 'unassigned') {
          req.unassignedOnly = true
        } else if (filters.assignedFilter && filters.assignedFilter !== 'all') {
          req.assignedUserId = filters.assignedFilter
        }

        const data = await inboxApi.filter(req)
        if (myGen !== listGenRef.current) return
        const fetchedItems = data?.items || data?.Items || (Array.isArray(data) ? data : [])
        const rawTotal =
          data?.total ??
          data?.Total ??
          data?.totalCount ??
          data?.TotalCount ??
          (isBackground ? totalRef.current : fetchedItems.length)

        if (isMountedRef.current) {
          setTotal(rawTotal)
          if (isBackground) {
            // Polling nền: chỉ index=1, merge (cập nhật item cũ, chèn item mới lên đầu),
            // giữ các trang đã tải và hội thoại đang chọn.
            // Item có mặt ở trang 1 mới xếp đúng thứ tự trang 1 lên đầu (kể cả item cũ vừa có
            // hoạt động mới); phần còn lại giữ thứ tự cũ phía sau.
            setItems((prev) => {
              const prevMap = new Map(prev.map((it) => [`${it.kind}:${it.id}`, it]))
              const page1Keys = new Set(fetchedItems.map((it) => `${it.kind}:${it.id}`))
              const head = fetchedItems.map((it) => {
                const key = `${it.kind}:${it.id}`
                const prevItem = prevMap.get(key)
                let unreadCount = it.unreadCount

                const readAt = readTimestampsRef.current.get(key)
                if (readAt !== undefined) {
                  const parsedTime = it.lastCustomerActivityAt
                    ? new Date(it.lastCustomerActivityAt).getTime()
                    : 0
                  const activityTime = isNaN(parsedTime) ? 0 : parsedTime
                  if (activityTime <= readAt) {
                    unreadCount = 0
                  }
                }

                return {
                  ...prevItem,
                  ...it,
                  unreadCount: unreadCount ?? prevItem?.unreadCount ?? 0,
                }
              })
              const rest = prev.filter((it) => !page1Keys.has(`${it.kind}:${it.id}`))
              return [...head, ...rest]
            })
          } else {
            let itemsToDisplay = fetchedItems.map((it) => {
              const key = `${it.kind}:${it.id}`
              const readAt = readTimestampsRef.current.get(key)
              if (readAt !== undefined) {
                const parsedTime = it.lastCustomerActivityAt
                  ? new Date(it.lastCustomerActivityAt).getTime()
                  : 0
                const activityTime = isNaN(parsedTime) ? 0 : parsedTime
                if (activityTime <= readAt) {
                  return { ...it, unreadCount: 0 }
                }
              }
              return it
            })
            const targetId = searchParams?.get?.('id')
            const targetKind = kindFromSearchParams(searchParams) || (searchParams?.get?.('kind') === 'comment' ? 2 : 1)

            let targetItem = targetId ? itemsToDisplay.find((it) => String(it.id) === String(targetId)) : null

            if (targetId && !targetItem) {
              try {
                let detailData = null
                if (targetKind === 2) {
                  detailData = await inboxApi.getComment(targetId)
                } else {
                  detailData = await inboxApi.getMessage(targetId)
                }
                if (detailData && myGen === listGenRef.current) {
                  const conv = detailData.conversation || detailData.thread || detailData
                  targetItem = {
                    id: targetId,
                    kind: targetKind,
                    socialChannelId: conv.socialChannelId || detailData.socialChannelId || null,
                    channelName: conv.channelName || detailData.channelName || null,
                    displayName: conv.participantName || conv.authorName || conv.displayName || 'Khách hàng',
                    snippet: conv.snippet || conv.messages?.[0]?.text || conv.message || '',
                    lastCustomerActivityAt: conv.lastCustomerActivityAt || conv.lastMessageAt || conv.commentedAt || new Date().toISOString(),
                    status: conv.inboxStatus || conv.status || 1,
                    assignedUserId: conv.assignedUserId || null,
                    assignedTo: conv.assignedTo || null,
                    unreadCount: conv.unreadCount || 0,
                    canReply: conv.canReply ?? false,
                    tags: detailData.tags || [],
                  }
                  itemsToDisplay = [targetItem, ...itemsToDisplay]
                }
              } catch {
                // If fetching target item fails, fall through gracefully
              }
            }

            if (myGen !== listGenRef.current) return
            setItems(itemsToDisplay)
            if (targetItem) {
              setSelectedItem(targetItem)
            } else if (itemsToDisplay.length > 0) {
              setSelectedItem((prev) => {
                const found = itemsToDisplay.find((it) => it.id === prev?.id)
                return found || itemsToDisplay[0]
              })
            } else {
              setItems([])
              setSelectedItem(null)
              setDetail(null)
            }
          }
        }
      } catch (err) {
        if (isMountedRef.current && !isBackground && myGen === listGenRef.current) {
          setError(err?.response?.data?.message || err?.message || 'Không thể tải danh sách hộp thư')
        }
      } finally {
        if (isMountedRef.current && !isBackground && myGen === listGenRef.current) {
          setLoadingList(false)
        }
      }
    },
    [filters],
  )

  // 3. Load next page (infinite scroll & fallback button)
  const loadMore = useCallback(async () => {
    if (isLoadingMoreRef.current || loadingList || itemsRef.current.length >= totalRef.current) return
    isLoadingMoreRef.current = true
    setLoadingMore(true)
    setPageError(null)

    const nextIndex = pageIndexRef.current + 1
    const myGen = listGenRef.current
    try {
      const req = {
        keyword: filters.keyword?.trim() || null,
        socialChannelId: filters.socialChannelId || null,
        source: filters.source || null,
        kind: filters.kind || null,
        status: filters.status || null,
        tagId: filters.tagId || null,
        unreadOnly: filters.unreadOnly || null,
        index: nextIndex,
        size: 30,
      }

      if (filters.assignedFilter === 'mine') {
        req.assignedMine = true
      } else if (filters.assignedFilter === 'unassigned') {
        req.unassignedOnly = true
      } else if (filters.assignedFilter && filters.assignedFilter !== 'all') {
        req.assignedUserId = filters.assignedFilter
      }

      const data = await inboxApi.filter(req)
      if (myGen !== listGenRef.current) return
      const rawItems = data?.items || data?.Items || (Array.isArray(data) ? data : [])
      const rawTotal = data?.total ?? data?.Total ?? data?.totalCount ?? data?.TotalCount ?? totalRef.current

      if (isMountedRef.current) {
        setTotal(rawTotal)
        setItems((prev) => {
          const seen = new Set(prev.map((it) => `${it.kind}:${it.id}`))
          const unique = rawItems
            .filter((it) => !seen.has(`${it.kind}:${it.id}`))
            .map((it) => {
              const key = `${it.kind}:${it.id}`
              const readAt = readTimestampsRef.current.get(key)
              if (readAt !== undefined) {
                const parsedTime = it.lastCustomerActivityAt
                  ? new Date(it.lastCustomerActivityAt).getTime()
                  : 0
                const activityTime = isNaN(parsedTime) ? 0 : parsedTime
                if (activityTime <= readAt) {
                  return { ...it, unreadCount: 0 }
                }
              }
              return it
            })
          return [...prev, ...unique]
        })
        setPageIndex(nextIndex)
      }
    } catch (err) {
      if (isMountedRef.current && myGen === listGenRef.current) {
        setPageError(err?.response?.data?.message || err?.message || 'Không thể tải thêm hội thoại')
      }
    } finally {
      // Bộ lọc đã đổi: cờ/loading thuộc về lượt tải mới, đừng đụng vào.
      if (myGen === listGenRef.current) {
        if (isMountedRef.current) setLoadingMore(false)
        isLoadingMoreRef.current = false
      }
    }
  }, [filters, loadingList])

  const handleRetryPage = () => {
    loadMore()
  }

  useEffect(() => {
    loadList()
  }, [loadList])

  // 4. Auto-refresh periodically (20s) & when returning to tab
  useEffect(() => {
    const timer = setInterval(() => {
      loadList(true)
      loadSources()
    }, 20000)

    const handleFocus = () => {
      loadList(true)
      loadSources()
    }

    const handleVisibilityChange = () => {
      if (document.visibilityState === 'visible') {
        loadList(true)
        loadSources()
      }
    }

    window.addEventListener('focus', handleFocus)
    document.addEventListener('visibilitychange', handleVisibilityChange)

    return () => {
      clearInterval(timer)
      window.removeEventListener('focus', handleFocus)
      document.removeEventListener('visibilitychange', handleVisibilityChange)
    }
  }, [loadList])

  // 5. Fetch detail when selectedItem changes
  const loadDetail = useCallback(async (item) => {
    if (!item?.id) return
    setLoadingDetail(true)
    try {
      let detailData = null
      if (item.kind === 1) {
        detailData = await inboxApi.getMessage(item.id)
      } else {
        detailData = await inboxApi.getComment(item.id)
      }
      if (isMountedRef.current) {
        setDetail(detailData)
      }
    } catch {
      if (isMountedRef.current) {
        setDetail(null)
      }
    } finally {
      if (isMountedRef.current) {
        setLoadingDetail(false)
      }
    }
  }, [])

  useEffect(() => {
    if (selectedItem?.id && selectedItem.id !== DEFAULT_INITIAL_ITEM.id) {
      loadDetail(selectedItem)
    }
  }, [selectedItem, loadDetail])

  // Handlers
  const handleSelectItem = (item) => {
    if (!item) return
    const key = `${item.kind}:${item.id}`
    const canMarkRead = !isReadOnly && (isAdmin || isContentManager || isReviewer || canCare)

    if (canMarkRead) {
      const now = Date.now()
      const parsedTime = item.lastCustomerActivityAt
        ? new Date(item.lastCustomerActivityAt).getTime()
        : 0
      const activityTime = isNaN(parsedTime) ? 0 : parsedTime
      readTimestampsRef.current.set(key, Math.max(now, activityTime))

      if (item.unreadCount > 0) {
        setItems((prev) =>
          prev.map((it) =>
            it.kind === item.kind && it.id === item.id ? { ...it, unreadCount: 0 } : it,
          ),
        )
      }

      inboxApi.markRead(item.kind, item.id).catch((err) => {
        console.warn('inboxApi.markRead error:', err)
      })
    }

    const nextSelected = canMarkRead && item.unreadCount > 0 ? { ...item, unreadCount: 0 } : item
    setSelectedItem(nextSelected)
    setCustomerProfile(null)
    setMobileView('detail')
    loadDetail(nextSelected)
  }

  const handleBackToList = () => {
    setMobileView('list')
  }

  const handleSendReply = async (text) => {
    if (!selectedItem) return
    if (selectedItem.kind === 1) {
      await inboxApi.sendMessage(selectedItem.id, text)
    } else {
      await inboxApi.replyComment(selectedItem.id, text)
    }
    if (selectedItem?.id) {
      readTimestampsRef.current.set(`${selectedItem.kind}:${selectedItem.id}`, Date.now())
    }
    await loadDetail(selectedItem)
    await loadList(true)
  }

  const handleAddNote = async (note) => {
    if (!selectedItem) return
    if (selectedItem.kind === 1) {
      await inboxApi.addMessageNote(selectedItem.id, note)
    } else {
      await inboxApi.addCommentNote(selectedItem.id, note)
    }
    await loadDetail(selectedItem)
  }

  const handleChangeStatus = async (status) => {
    if (!selectedItem) return
    if (selectedItem.kind === 1) {
      await inboxApi.setMessageStatus(selectedItem.id, status)
    } else {
      await inboxApi.setCommentStatus(selectedItem.id, status)
    }
    setSelectedItem((prev) => (prev ? { ...prev, status } : prev))
    await loadList(true)
    await loadDetail(selectedItem)
  }

  const handleAssign = async (userId, userName) => {
    if (!selectedItem) return
    const payload = { assignedUserId: userId, assignedTo: userName }
    if (selectedItem.kind === 1) {
      await inboxApi.assignMessage(selectedItem.id, payload)
    } else {
      await inboxApi.assignComment(selectedItem.id, payload)
    }
    setSelectedItem((prev) => (prev ? { ...prev, assignedUserId: userId, assignedTo: userName } : prev))
    await loadList(true)
    await loadDetail(selectedItem)
  }

  const handleAttachTag = async (tagId) => {
    if (!selectedItem) return
    const targetType = selectedItem.kind === 1 ? 1 : 2
    await inboxApi.attachTag(tagId, targetType, selectedItem.id)
    await loadDetail(selectedItem)
    await loadList(true)
  }

  const handleDetachTag = async (tagId) => {
    if (!selectedItem) return
    const targetType = selectedItem.kind === 1 ? 1 : 2
    await inboxApi.detachTag(tagId, targetType, selectedItem.id)
    await loadDetail(selectedItem)
    await loadList(true)
  }

  const handleNavigateCustomer = (target) => {
    const custId = typeof target === 'string' ? target : target?.id
    if (custId) {
      navigate(`/customers/${custId}`)
    } else {
      navigate('/customers')
    }
  }

  return (
    <div
      className={`crm-inbox-grid crm-inbox-mobile-${mobileView} ${!showCustomerPanel ? 'crm-inbox-grid--no-customer' : ''}`}
      data-testid="inbox-feature"
    >
      {/* Left Column: Sidebar with Filter & List */}
      <div className="crm-inbox-sidebar">
        <InboxFilterBar
          channels={channels}
          tags={tags}
          users={users}
          sources={sources}
          filters={filters}
          onChangeFilters={handleChangeFilters}
          onSearchSubmit={() => loadList()}
        />

        <InboxList
          items={items}
          selectedId={selectedItem?.id}
          onSelectItem={handleSelectItem}
          loading={loadingList}
          error={error}
          total={total}
          hasMore={items.length < total}
          loadingMore={loadingMore}
          pageError={pageError}
          onLoadMore={loadMore}
          onRetryPage={handleRetryPage}
          listRef={listRef}
        />
      </div>

      {/* Middle Column: Chat Detail */}
      <InboxDetail
        item={selectedItem}
        detail={detail}
        loading={loadingDetail}
        users={users}
        tags={tags}
        canCare={canCare}
        isReadOnly={isReadOnly}
        onBack={handleBackToList}
        onSendReply={handleSendReply}
        onAddNote={handleAddNote}
        onChangeStatus={handleChangeStatus}
        onAssign={handleAssign}
        onAttachTag={handleAttachTag}
        onDetachTag={handleDetachTag}
        onNavigateCustomer={handleNavigateCustomer}
        linkedCustomerId={customerProfile?.linked ? customerProfile?.customer?.id : null}
      />

      {/* Toggle button to open customer panel when closed */}
      {!showCustomerPanel && (
        <button
          type="button"
          className="crm-customer-toggle-btn crm-customer-toggle-btn--open"
          data-testid="toggle-customer-panel"
          onClick={toggleCustomerPanel}
          aria-label="Mở hồ sơ"
          title="Mở hồ sơ"
        >
          👤 Mở hồ sơ
        </button>
      )}

      {/* Right Column: Customer Panel SO9 */}
      {showCustomerPanel && (
        <CustomerPanel
          item={selectedItem}
          tags={tags}
          isReadOnly={isReadOnly}
          canCare={canCare}
          onClose={toggleCustomerPanel}
          onCustomerLoaded={setCustomerProfile}
        />
      )}
    </div>
  )
}

export default InboxFeature
