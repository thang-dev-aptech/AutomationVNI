import { useMemo, useState } from 'react'
import PageHeader from '@/shared/components/PageHeader'
import { usePermissions } from '@/shared/hooks/usePermissions'
import { getErrorMessage } from '@/shared/utils/apiHelpers'
import { confirmAction, CONFIRM_MESSAGES } from '@/shared/utils/confirmAction'
import { toast } from '@/shared/stores/toastStore'
import PlatformCard from '../components/PlatformCard'
import SocialChannelTable from '../components/SocialChannelTable'
import SocialChannelFormModal from '../components/SocialChannelFormModal'
import { PLATFORM_CARDS } from '../constants/socialPlatform'
import {
  useCreateSocialChannel,
  useDeleteSocialChannel,
  useSocialChannelAll,
  useSocialChannels,
  useUpdateSocialChannel,
} from '../hooks/useSocialChannels'
import './PlatformsPage.css'

export default function PlatformsPage() {
  const { canManageChannels } = usePermissions()
  const [keyword, setKeyword] = useState('')
  const [selectedCardId, setSelectedCardId] = useState(null)
  const [modalOpen, setModalOpen] = useState(false)
  const [editingItem, setEditingItem] = useState(null)
  const [formError, setFormError] = useState('')

  const selectedCard = PLATFORM_CARDS.find((card) => card.id === selectedCardId)

  const params = useMemo(
    () => ({
      keyword,
      index: 1,
      size: 50,
      platform: selectedCard?.backendPlatform ?? undefined,
    }),
    [keyword, selectedCard],
  )

  const { data, isLoading, isError, error, refetch } = useSocialChannels(params)
  const { data: allChannels = [] } = useSocialChannelAll()
  const createMutation = useCreateSocialChannel()
  const updateMutation = useUpdateSocialChannel()
  const deleteMutation = useDeleteSocialChannel()

  const items = data?.items ?? []

  const channelCountByPlatform = useMemo(() => {
    const counts = {}
    for (const card of PLATFORM_CARDS) {
      if (card.backendPlatform) {
        counts[card.id] = allChannels.filter(
          (item) => item.platform === card.backendPlatform,
        ).length
      } else {
        counts[card.id] = 0
      }
    }
    return counts
  }, [allChannels])

  const openCreate = (platformValue) => {
    setEditingItem(null)
    setFormError('')
    if (platformValue) {
      const card = PLATFORM_CARDS.find((c) => c.backendPlatform === platformValue)
      setSelectedCardId(card?.id ?? null)
    }
    setModalOpen(true)
  }

  const openEdit = (item) => {
    setEditingItem(item)
    setFormError('')
    setModalOpen(true)
  }

  const handleSubmit = async (payload) => {
    try {
      setFormError('')
      if (editingItem) {
        await updateMutation.mutateAsync({ id: editingItem.id, payload })
      } else {
        const { isActive, ...createPayload } = payload
        const created = await createMutation.mutateAsync(createPayload)
        if (isActive === false) {
          await updateMutation.mutateAsync({
            id: created.id,
            payload: { isActive: false },
          })
        }
      }
      setModalOpen(false)
      toast.success(editingItem ? 'Đã cập nhật kênh' : 'Đã kết nối kênh')
    } catch (submitError) {
      setFormError(getErrorMessage(submitError))
    }
  }

  const handleDelete = async (item) => {
    if (!confirmAction(CONFIRM_MESSAGES.deleteChannel(item.pageName))) return
    try {
      await deleteMutation.mutateAsync(item.id)
      toast.success('Đã ngắt kết nối kênh')
    } catch (deleteError) {
      toast.error(getErrorMessage(deleteError))
    }
  }

  const handleCardClick = (card) => {
    if (!card.supported) return
    setSelectedCardId((prev) => (prev === card.id ? null : card.id))
  }

  const defaultPlatform = selectedCard?.backendPlatform ?? undefined

  return (
    <section className="platforms-page">
      <PageHeader
        title="Platforms / Kênh"
        description="Kết nối và quản lý page mạng xã hội — cần thiết trước khi tạo bài viết"
        actions={
          canManageChannels ? (
            <button
              type="button"
              className="btn btn-primary"
              onClick={() => openCreate(defaultPlatform)}
            >
              Kết nối kênh
            </button>
          ) : null
        }
      />

      <div className="platform-cards">
        {PLATFORM_CARDS.map((card) => (
          <PlatformCard
            key={card.id}
            label={card.label}
            description={card.description}
            channelCount={channelCountByPlatform[card.id] ?? 0}
            supported={card.supported}
            selected={selectedCardId === card.id}
            onClick={() => handleCardClick(card)}
          />
        ))}
      </div>

      <div className="card card-body platforms-filter">
        <div className="form-group" style={{ marginBottom: 0 }}>
          <label htmlFor="channel-keyword">Tìm kiếm kênh</label>
          <input
            id="channel-keyword"
            value={keyword}
            onChange={(event) => setKeyword(event.target.value)}
            placeholder="Tên page..."
          />
        </div>
        {selectedCard && (
          <p className="platforms-filter-hint">
            Đang lọc: <strong>{selectedCard.label}</strong>
            {' · '}
            <button
              type="button"
              className="btn btn-ghost btn-sm"
              onClick={() => setSelectedCardId(null)}
            >
              Bỏ lọc
            </button>
          </p>
        )}
      </div>

      <div className="card platforms-table">
        <SocialChannelTable
          items={items}
          isLoading={isLoading}
          isError={isError}
          error={error}
          onRetry={refetch}
          onEdit={openEdit}
          onDelete={handleDelete}
          canManage={canManageChannels}
          emptyMessage={
            selectedCard
              ? `Chưa có kênh ${selectedCard.label} nào được kết nối`
              : 'Chưa có kênh nào được kết nối'
          }
        />
      </div>

      <SocialChannelFormModal
        open={modalOpen}
        onClose={() => setModalOpen(false)}
        initialData={editingItem}
        defaultPlatform={defaultPlatform}
        onSubmit={handleSubmit}
        isSubmitting={createMutation.isPending || updateMutation.isPending}
        errorMessage={formError}
      />
    </section>
  )
}
