import React, { useState, useEffect, useCallback, useRef } from 'react'
import { useNavigate, useSearchParams } from 'react-router-dom'
import { inboxApi } from './api/inboxApi'
import { useAuth } from '../../auth/useAuth'
import InboxFilterBar from './components/InboxFilterBar'
import InboxList from './components/InboxList'
import InboxDetail from './components/InboxDetail'
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
  const [searchParams] = useSearchParams()
  const { canCare, isReadOnly } = useAuth()

  // State
  const [items, setItems] = useState([DEFAULT_INITIAL_ITEM])
  const [selectedItem, setSelectedItem] = useState(DEFAULT_INITIAL_ITEM)
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

  useEffect(() => {
    totalRef.current = total
  }, [total])

  useEffect(() => {
    pageIndexRef.current = pageIndex
  }, [pageIndex])

  useEffect(() => {
    itemsRef.current = items
  }, [items])

  // Auxiliary data
  const [channels, setChannels] = useState([])
  const [users, setUsers] = useState([])
  const [tags, setTags] = useState([])

  // Filters — kind khởi tạo từ ?kind= (ClientApp redirect /messages|/comments)
  const [filters, setFilters] = useState({
    socialChannelId: null,
    kind: kindFromSearchParams(searchParams),
    status: null,
    assignedFilter: 'all',
    tagId: null,
    unreadOnly: false,
    keyword: '',
  })

  // Responsive mobile state: 'list' | 'detail'
  const [mobileView, setMobileView] = useState('list')

  const isMountedRef = useRef(true)

  // 1. Fetch metadata (channels, users, tags)
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

    return () => {
      isMountedRef.current = false
    }
  }, [])

  // 2. Fetch list of conversations & comments (foreground: index=1, size=30; background: index=1, merge)
  const loadList = useCallback(
    async (isBackground = false) => {
      if (!isBackground) {
        setLoadingList(true)
        setError(null)
        setPageError(null)
        setPageIndex(1)
        if (listRef.current) {
          listRef.current.scrollTop = 0
        }
      }
      try {
        const req = {
          keyword: filters.keyword?.trim() || null,
          socialChannelId: filters.socialChannelId || null,
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
            setItems((prev) => {
              const page1Map = new Map(fetchedItems.map((it) => [`${it.kind}:${it.id}`, it]))
              const existingKeys = new Set(prev.map((it) => `${it.kind}:${it.id}`))
              const brandNew = fetchedItems.filter((it) => !existingKeys.has(`${it.kind}:${it.id}`))
              const updatedExisting = prev.map((it) => {
                const key = `${it.kind}:${it.id}`
                return page1Map.has(key) ? { ...it, ...page1Map.get(key) } : it
              })
              return [...brandNew, ...updatedExisting]
            })
          } else {
            setItems(fetchedItems)
            if (Array.isArray(fetchedItems) && fetchedItems.length > 0) {
              setSelectedItem((prev) => {
                const found = fetchedItems.find((it) => it.id === prev?.id)
                return found || fetchedItems[0]
              })
            } else {
              setItems([])
              setSelectedItem(null)
              setDetail(null)
            }
          }
        }
      } catch (err) {
        if (isMountedRef.current && !isBackground) {
          setError(err?.response?.data?.message || err?.message || 'Không thể tải danh sách hộp thư')
        }
      } finally {
        if (isMountedRef.current && !isBackground) {
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
    try {
      const req = {
        keyword: filters.keyword?.trim() || null,
        socialChannelId: filters.socialChannelId || null,
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
      const rawItems = data?.items || data?.Items || (Array.isArray(data) ? data : [])
      const rawTotal = data?.total ?? data?.Total ?? data?.totalCount ?? data?.TotalCount ?? totalRef.current

      if (isMountedRef.current) {
        setTotal(rawTotal)
        setItems((prev) => {
          const seen = new Set(prev.map((it) => `${it.kind}:${it.id}`))
          const unique = rawItems.filter((it) => !seen.has(`${it.kind}:${it.id}`))
          return [...prev, ...unique]
        })
        setPageIndex(nextIndex)
      }
    } catch (err) {
      if (isMountedRef.current) {
        setPageError(err?.response?.data?.message || err?.message || 'Không thể tải thêm hội thoại')
      }
    } finally {
      if (isMountedRef.current) {
        setLoadingMore(false)
      }
      isLoadingMoreRef.current = false
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
    }, 20000)

    const handleFocus = () => {
      loadList(true)
    }

    const handleVisibilityChange = () => {
      if (document.visibilityState === 'visible') {
        loadList(true)
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
    setSelectedItem(item)
    setMobileView('detail')
    loadDetail(item)
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

  const handleNavigateCustomer = () => {
    navigate('/customers')
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
          filters={filters}
          onChangeFilters={setFilters}
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

      {/* Right Column: Customer Panel Placeholder (t3 will implement details) */}
      {showCustomerPanel && (
        <aside
          className="crm-customer-panel"
          data-testid="customer-panel"
          aria-label="Hồ sơ khách hàng"
        >
          <div className="crm-customer-panel-header">
            <div className="crm-customer-panel-title-wrap">
              <h3 className="crm-customer-panel-title">Hồ sơ khách hàng</h3>
              <span className="crm-customer-panel-badge">SO9</span>
            </div>
            <button
              type="button"
              className="crm-customer-toggle-btn crm-customer-toggle-btn--close"
              data-testid="toggle-customer-panel"
              onClick={toggleCustomerPanel}
              aria-label="Thu gọn hồ sơ"
              title="Thu gọn hồ sơ"
            >
              ✕ Thu gọn
            </button>
          </div>
          <div className="crm-customer-panel-body">
            <div className="crm-customer-placeholder-notice">
              {selectedItem ? (
                <div>
                  <div style={{ fontWeight: 700, fontSize: '15px', color: 'var(--crm-text)' }}>
                    {selectedItem.displayName || 'Khách hàng'}
                  </div>
                  <div style={{ fontSize: '13px', color: 'var(--crm-text-muted)', marginTop: '4px' }}>
                    {selectedItem.channelName || ''}
                  </div>
                  <div style={{ marginTop: '16px', padding: '12px', background: 'var(--crm-surface-subtle)', borderRadius: 'var(--crm-radius-md)', fontSize: '12px', color: 'var(--crm-text-muted)' }}>
                    ℹ️ Vùng hiển thị hồ sơ khách hàng theo chuẩn SO9 (Task t3).
                  </div>
                </div>
              ) : (
                <p style={{ color: 'var(--crm-text-muted)' }}>Chọn một hội thoại để xem hồ sơ khách hàng</p>
              )}
            </div>
          </div>
        </aside>
      )}
    </div>
  )
}

export default InboxFeature
