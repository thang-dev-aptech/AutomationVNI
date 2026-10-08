import React, { useState, useEffect, useCallback, useRef } from 'react'
import { useNavigate } from 'react-router-dom'
import { inboxApi } from './api/inboxApi'
import { useAuth } from '../../auth/useAuth'
import InboxFilterBar from './components/InboxFilterBar'
import InboxList from './components/InboxList'
import InboxDetail from './components/InboxDetail'
import './InboxFeature.css'

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
  const { canCare, isReadOnly } = useAuth()

  // State
  const [items, setItems] = useState([DEFAULT_INITIAL_ITEM])
  const [selectedItem, setSelectedItem] = useState(DEFAULT_INITIAL_ITEM)
  const [detail, setDetail] = useState(DEFAULT_INITIAL_DETAIL)
  const [loadingList, setLoadingList] = useState(false)
  const [loadingDetail, setLoadingDetail] = useState(false)
  const [error, setError] = useState(null)

  // Auxiliary data
  const [channels, setChannels] = useState([])
  const [users, setUsers] = useState([])
  const [tags, setTags] = useState([])

  // Filters
  const [filters, setFilters] = useState({
    socialChannelId: null,
    kind: null,
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

  // 2. Fetch list of conversations & comments
  const loadList = useCallback(
    async (isBackground = false) => {
      if (!isBackground) setLoadingList(true)
      setError(null)
      try {
        const req = {
          keyword: filters.keyword?.trim() || null,
          socialChannelId: filters.socialChannelId || null,
          kind: filters.kind || null,
          status: filters.status || null,
          tagId: filters.tagId || null,
          unreadOnly: filters.unreadOnly || null,
          pageSize: 50,
        }

        if (filters.assignedFilter === 'mine') {
          req.assignedMine = true
        } else if (filters.assignedFilter === 'unassigned') {
          req.unassignedOnly = true
        } else if (filters.assignedFilter && filters.assignedFilter !== 'all') {
          req.assignedUserId = filters.assignedFilter
        }

        const data = await inboxApi.filter(req)
        const fetchedItems = data?.items || data || []

        if (isMountedRef.current) {
          if (Array.isArray(fetchedItems) && fetchedItems.length > 0) {
            setItems(fetchedItems)
            // If current selected item not in new items, select first
            setSelectedItem((prev) => {
              const found = fetchedItems.find((it) => it.id === prev?.id)
              return found || fetchedItems[0]
            })
          } else if (fetchedItems.length === 0 && !isBackground) {
            setItems([])
            setSelectedItem(null)
            setDetail(null)
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

  useEffect(() => {
    loadList()
  }, [loadList])

  // 3. Auto-refresh periodically (20s) & when returning to tab
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

  // 4. Fetch detail when selectedItem changes
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
    <div className={`crm-inbox-grid crm-inbox-mobile-${mobileView}`} data-testid="inbox-feature">
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
        />
      </div>

      {/* Right Column: Chat Detail */}
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
    </div>
  )
}

export default InboxFeature
