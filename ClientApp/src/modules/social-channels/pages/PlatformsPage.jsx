import { useEffect, useMemo, useState } from 'react'
import { useNavigate, useSearchParams } from 'react-router-dom'
import PageHeader from '@/shared/components/PageHeader'
import LoadingState from '@/shared/components/LoadingState'
import ErrorState from '@/shared/components/ErrorState'
import EmptyState from '@/shared/components/EmptyState'
import { usePermissions } from '@/shared/hooks/usePermissions'
import { getErrorMessage } from '@/shared/utils/apiHelpers'
import { confirmAction, CONFIRM_MESSAGES } from '@/shared/utils/confirmAction'
import { toast } from '@/shared/stores/toastStore'
import ConnectionCard from '../components/ConnectionCard'
import SocialChannelTable from '../components/SocialChannelTable'
import SocialChannelFormModal from '../components/SocialChannelFormModal'
import { PROVIDER_CATALOG } from '../constants/socialPlatform'
import {
  useCreateSocialChannel,
  useDeleteSocialChannel,
  useDisconnectSocialConnection,
  useMetaConnectUrl,
  useSocialChannelAll,
  useSocialConnections,
  useUpdateSocialChannel,
} from '../hooks/useSocialChannels'
import './PlatformsPage.css'

export default function PlatformsPage() {
  const navigate = useNavigate()
  const [searchParams] = useSearchParams()
  const { canManageChannels } = usePermissions()
  const [keyword, setKeyword] = useState('')
  const [expandedIds, setExpandedIds] = useState(() => new Set())
  const [connectMenuOpen, setConnectMenuOpen] = useState(false)
  const [modalOpen, setModalOpen] = useState(false)
  const [editingItem, setEditingItem] = useState(null)
  const [formError, setFormError] = useState('')

  const {
    data: connections = [],
    isLoading: connectionsLoading,
    isError: connectionsError,
    error: connectionsErr,
    refetch: refetchConnections,
  } = useSocialConnections()

  const {
    data: allChannels = [],
    isLoading: channelsLoading,
    isError: channelsError,
    error: channelsErr,
    refetch: refetchChannels,
  } = useSocialChannelAll()

  const createMutation = useCreateSocialChannel()
  const updateMutation = useUpdateSocialChannel()
  const deleteMutation = useDeleteSocialChannel()
  const disconnectMutation = useDisconnectSocialConnection()
  const metaConnectMutation = useMetaConnectUrl()

  const orphanChannels = useMemo(() => {
    const kw = keyword.trim().toLowerCase()
    return allChannels.filter((ch) => {
      if (ch.socialConnectionId) return false
      if (!kw) return true
      return (
        ch.pageName?.toLowerCase().includes(kw) ||
        ch.externalPageId?.toLowerCase().includes(kw)
      )
    })
  }, [allChannels, keyword])

  const filteredConnections = useMemo(() => {
    const kw = keyword.trim().toLowerCase()
    if (!kw) return connections
    return connections.filter((c) => {
      if (c.displayName?.toLowerCase().includes(kw)) return true
      return (c.channels ?? []).some(
        (ch) =>
          ch.pageName?.toLowerCase().includes(kw) ||
          ch.externalPageId?.toLowerCase().includes(kw),
      )
    })
  }, [connections, keyword])

  useEffect(() => {
    const status = searchParams.get('metaConnected')
    if (!status) return

    if (status === 'success') {
      const fb = searchParams.get('fb') ?? '0'
      const ig = searchParams.get('ig') ?? '0'
      const gr = searchParams.get('gr') ?? '0'
      toast.success(`Đã sync Meta: ${fb} Page, ${ig} Instagram, ${gr} Group`)
      refetchConnections()
      refetchChannels()
    } else if (status === 'error') {
      const message = searchParams.get('message')
      toast.error(message ? decodeURIComponent(message) : 'Kết nối Meta thất bại')
    }

    navigate('/platforms', { replace: true })
  }, [searchParams, navigate, refetchConnections, refetchChannels])

  useEffect(() => {
    if (connections.length === 0) return
    setExpandedIds((prev) => {
      if (prev.size > 0) return prev
      return new Set([connections[0].id])
    })
  }, [connections])

  const openCreate = () => {
    setEditingItem(null)
    setFormError('')
    setModalOpen(true)
    setConnectMenuOpen(false)
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

  const handleConnectMeta = async () => {
    setConnectMenuOpen(false)
    try {
      const { url } = await metaConnectMutation.mutateAsync()
      if (url) window.location.href = url
    } catch (connectError) {
      toast.error(getErrorMessage(connectError))
    }
  }

  const handleProviderAction = (provider) => {
    if (provider.connectAction === 'meta') {
      handleConnectMeta()
      return
    }
    if (provider.connectAction === 'manual') {
      openCreate()
      return
    }
    toast.error(`${provider.label} sắp hỗ trợ`)
  }

  const handleDisconnect = async (connection) => {
    if (
      !confirmAction(
        `Ngắt kết nối tài khoản "${connection.displayName}"? Các kênh thuộc tài khoản sẽ bị tắt.`,
      )
    ) {
      return
    }
    try {
      await disconnectMutation.mutateAsync(connection.id)
      toast.success('Đã ngắt kết nối tài khoản')
    } catch (err) {
      toast.error(getErrorMessage(err))
    }
  }

  const toggleExpanded = (id) => {
    setExpandedIds((prev) => {
      const next = new Set(prev)
      if (next.has(id)) next.delete(id)
      else next.add(id)
      return next
    })
  }

  const isLoading = connectionsLoading || channelsLoading
  const isError = connectionsError || channelsError
  const error = connectionsErr || channelsErr

  const refetchAll = () => {
    refetchConnections()
    refetchChannels()
  }

  return (
    <section className="platforms-page">
      <PageHeader
        title="Platforms / Kênh"
        description="Kết nối theo tài khoản (Meta…) — sync Pages, Instagram, Groups; thêm provider sau không đổi layout"
        actions={
          canManageChannels ? (
            <div className="platforms-header-actions">
              <div className="connect-menu">
                <button
                  type="button"
                  className="btn btn-primary"
                  onClick={() => setConnectMenuOpen((v) => !v)}
                  aria-expanded={connectMenuOpen}
                >
                  + Connect
                </button>
                {connectMenuOpen && (
                  <ul className="connect-menu-list">
                    {PROVIDER_CATALOG.map((p) => (
                      <li key={p.id}>
                        <button
                          type="button"
                          className="connect-menu-item"
                          disabled={!p.supported}
                          onClick={() => handleProviderAction(p)}
                        >
                          <strong>{p.label}</strong>
                          <span>{p.description}</span>
                        </button>
                      </li>
                    ))}
                  </ul>
                )}
              </div>
              <button type="button" className="btn btn-secondary" onClick={openCreate}>
                Kết nối thủ công
              </button>
            </div>
          ) : null
        }
      />

      <div className="card card-body platforms-filter">
        <div className="form-group" style={{ marginBottom: 0 }}>
          <label htmlFor="channel-keyword">Tìm tài khoản / kênh</label>
          <input
            id="channel-keyword"
            value={keyword}
            onChange={(event) => setKeyword(event.target.value)}
            placeholder="Tên tài khoản, page, group…"
          />
        </div>
      </div>

      {isLoading && <LoadingState />}
      {isError && <ErrorState message={getErrorMessage(error)} onRetry={refetchAll} />}

      {!isLoading && !isError && (
        <>
          <div className="connections-list">
            <h2 className="platforms-section-title">Tài khoản đã kết nối</h2>
            {filteredConnections.length === 0 ? (
              <EmptyState message="Chưa có tài khoản Meta nào. Bấm + Connect → Meta." />
            ) : (
              filteredConnections.map((connection) => (
                <ConnectionCard
                  key={connection.id}
                  connection={connection}
                  expanded={expandedIds.has(connection.id)}
                  onToggle={() => toggleExpanded(connection.id)}
                  canManage={canManageChannels}
                  onResync={handleConnectMeta}
                  onDisconnect={() => handleDisconnect(connection)}
                  onEditChannel={openEdit}
                  onDeleteChannel={handleDelete}
                  resyncPending={metaConnectMutation.isPending}
                />
              ))
            )}
          </div>

          {orphanChannels.length > 0 && (
            <div className="orphan-channels">
              <h2 className="platforms-section-title">Kênh thủ công / chưa gắn tài khoản</h2>
              <div className="card platforms-table">
                <SocialChannelTable
                  items={orphanChannels}
                  isLoading={false}
                  isError={false}
                  onEdit={openEdit}
                  onDelete={handleDelete}
                  canManage={canManageChannels}
                  emptyMessage="Không có kênh thủ công"
                />
              </div>
            </div>
          )}
        </>
      )}

      <SocialChannelFormModal
        open={modalOpen}
        onClose={() => setModalOpen(false)}
        initialData={editingItem}
        defaultPlatform={1}
        onSubmit={handleSubmit}
        isSubmitting={createMutation.isPending || updateMutation.isPending}
        errorMessage={formError}
      />
    </section>
  )
}
