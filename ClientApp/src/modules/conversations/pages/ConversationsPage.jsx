import { useEffect, useMemo, useState } from 'react'
import { useConversationParams } from '../hooks/useConversationParams'
import { useInboxList } from '../hooks/useInbox'
import ConversationFilterBar from '../components/ConversationFilterBar'
import ConversationList from '../components/ConversationList'
import ConversationChatPanel from '../components/ConversationChatPanel'
import CustomerInfoPanel from '../components/CustomerInfoPanel'
import './ConversationsPage.css'

function useIsNarrow(breakpoint = 768) {
  const [isNarrow, setIsNarrow] = useState(
    () => typeof window !== 'undefined' && window.innerWidth <= breakpoint,
  )

  useEffect(() => {
    if (typeof window === 'undefined') return undefined
    const handleResize = () => setIsNarrow(window.innerWidth <= breakpoint)
    window.addEventListener('resize', handleResize)
    return () => window.removeEventListener('resize', handleResize)
  }, [breakpoint])

  return isNarrow
}

export default function ConversationsPage() {
  const {
    filters,
    selectedId,
    selectedKind,
    updateFilter,
    clearFilters,
    setSelectedConversation,
  } = useConversationParams()

  const isNarrow = useIsNarrow(768)
  const [isCustomerInfoOpen, setIsCustomerInfoOpen] = useState(() => {
    try {
      const saved = localStorage.getItem('crm_customer_info_panel_open')
      return saved !== null ? JSON.parse(saved) : true
    } catch {
      return true
    }
  })

  const handleToggleCustomerInfo = () => {
    setIsCustomerInfoOpen((prev) => {
      const next = !prev
      try {
        localStorage.setItem('crm_customer_info_panel_open', JSON.stringify(next))
      } catch {}
      return next
    })
  }

  const handleCloseCustomerInfo = () => {
    setIsCustomerInfoOpen(false)
    try {
      localStorage.setItem('crm_customer_info_panel_open', JSON.stringify(false))
    } catch {}
  }

  // Payload for api/Inbox/filter
  const filterRequest = useMemo(
    () => ({
      kinds: filters.kinds && filters.kinds.length > 0 ? filters.kinds : null,
      statuses: filters.statuses && filters.statuses.length > 0 ? filters.statuses : null,
      socialChannelIds:
        filters.socialChannelIds && filters.socialChannelIds.length > 0
          ? filters.socialChannelIds
          : null,
      channelGroupIds:
        filters.channelGroupIds && filters.channelGroupIds.length > 0
          ? filters.channelGroupIds
          : null,
      unreadOnly: filters.unreadOnly ? true : null,
      fromUtc: filters.fromUtc || null,
      toUtc: filters.toUtc || null,
      assignedUserIds:
        filters.assignedUserIds && filters.assignedUserIds.length > 0
          ? filters.assignedUserIds
          : null,
      customerUnansweredOnly: filters.customerUnansweredOnly ? true : null,
      openWindowOnly: filters.openWindowOnly ? true : null,
      search: filters.search ? filters.search.trim() : null,
      index: filters.index || 1,
      size: filters.size || 20,
    }),
    [filters],
  )

  const { data: listData, isLoading } = useInboxList(filterRequest)

  const items = listData?.items || []
  const total = listData?.total || 0

  const selectedItem = useMemo(() => {
    if (!selectedId) return null
    return items.find((x) => String(x.id) === String(selectedId)) || null
  }, [items, selectedId])

  const handleSelectConversation = (id, kind) => {
    setSelectedConversation(id, kind)
  }

  const handleBackToList = () => {
    setSelectedConversation(null)
  }

  return (
    <div
      className={`conversations-page${isNarrow ? ' is-narrow' : ''}${selectedId ? ' has-selection' : ''}${isCustomerInfoOpen ? ' is-info-open' : ''}`}
      data-testid="conversations-page"
    >
      {/* Cột 1: Tìm kiếm + Thanh lọc + Danh sách */}
      {(!isNarrow || !selectedId) && (
        <section
          className="conversations-col conversations-col--list"
          data-testid="conversations-col-list"
        >
          <ConversationFilterBar
            filters={filters}
            onUpdateFilter={updateFilter}
            onClearFilters={clearFilters}
          />
          <ConversationList
            items={items}
            total={total}
            page={filters.index}
            size={filters.size}
            isLoading={isLoading}
            selectedId={selectedId}
            onSelectConversation={handleSelectConversation}
            onPageChange={(page) => updateFilter('page', page)}
          />
        </section>
      )}

      {/* Cột 2: Chat panel slot (t4) */}
      {(!isNarrow || selectedId) && (
        <section
          className="conversations-col conversations-col--chat"
          data-testid="conversations-col-chat"
        >
          <ConversationChatPanel
            kind={selectedKind}
            id={selectedId}
            conversation={selectedItem}
            onBack={handleBackToList}
            onToggleCustomerInfo={handleToggleCustomerInfo}
            isCustomerInfoOpen={isCustomerInfoOpen}
          />
        </section>
      )}

      {/* Cột 3: Customer info panel slot (t5) */}
      {!isNarrow && isCustomerInfoOpen && selectedId && (
        <section
          className="conversations-col conversations-col--info"
          data-testid="conversations-col-info"
        >
          <CustomerInfoPanel
            kind={selectedKind}
            id={selectedId}
            conversation={selectedItem}
            onClose={handleCloseCustomerInfo}
            onSelectConversation={handleSelectConversation}
          />
        </section>
      )}
    </div>
  )
}
