import { useMemo, useState } from 'react'
import Modal from '@/shared/components/Modal'
import LoadingState from '@/shared/components/LoadingState'
import ErrorState from '@/shared/components/ErrorState'
import EmptyState from '@/shared/components/EmptyState'
import ChannelMultiSelect from '@/shared/components/ChannelMultiSelect'
import { usePermissions } from '@/shared/hooks/usePermissions'
import { getErrorMessage } from '@/shared/utils/apiHelpers'
import { confirmAction } from '@/shared/utils/confirmAction'
import { toast } from '@/shared/stores/toastStore'
import { useSocialChannelAll } from '../hooks/useSocialChannels'
import {
  useChannelGroupAll,
  useCreateChannelGroup,
  useDeleteChannelGroup,
  useUpdateChannelGroup,
} from '../hooks/useChannelGroups'
import './ChannelGroupTab.css'

/**
 * Tab quản lý nhóm kênh trên trang Platforms.
 * Chỉ Admin/ContentManager thấy nút tạo/sửa/xoá (khớp api/ChannelGroup).
 */
export default function ChannelGroupTab() {
  const { hasRole } = usePermissions()
  const canManageGroups = hasRole(['Admin', 'ContentManager'])

  const {
    data: groups = [],
    isLoading,
    isError,
    error,
    refetch,
  } = useChannelGroupAll()

  const {
    data: channels = [],
    isLoading: channelsLoading,
  } = useSocialChannelAll()

  const createMutation = useCreateChannelGroup()
  const updateMutation = useUpdateChannelGroup()
  const deleteMutation = useDeleteChannelGroup()

  const [modalOpen, setModalOpen] = useState(false)
  const [editing, setEditing] = useState(null)
  const [name, setName] = useState('')
  const [description, setDescription] = useState('')
  const [channelIds, setChannelIds] = useState([])
  const [formError, setFormError] = useState('')

  const sortedGroups = useMemo(
    () => [...groups].sort((a, b) => (a.name || '').localeCompare(b.name || '', 'vi')),
    [groups],
  )

  const openCreate = () => {
    setEditing(null)
    setName('')
    setDescription('')
    setChannelIds([])
    setFormError('')
    setModalOpen(true)
  }

  const openEdit = (group) => {
    setEditing(group)
    setName(group.name || '')
    setDescription(group.description || '')
    setChannelIds((group.channels ?? []).map((c) => c.id))
    setFormError('')
    setModalOpen(true)
  }

  const closeModal = () => {
    setModalOpen(false)
    setFormError('')
  }

  const handleSubmit = async (event) => {
    event.preventDefault()
    const trimmed = name.trim()
    if (!trimmed) {
      setFormError('Tên nhóm không được để trống')
      return
    }

    const payload = {
      name: trimmed,
      description: description.trim() || null,
      channelIds,
    }

    try {
      setFormError('')
      if (editing) {
        await updateMutation.mutateAsync({ id: editing.id, payload })
        toast.success('Đã cập nhật nhóm kênh')
      } else {
        await createMutation.mutateAsync(payload)
        toast.success('Đã tạo nhóm kênh')
      }
      closeModal()
    } catch (err) {
      setFormError(getErrorMessage(err))
    }
  }

  const handleDelete = async (group) => {
    if (!confirmAction(`Xóa nhóm kênh "${group.name}"? Kênh và bài viết không bị ảnh hưởng.`)) {
      return
    }
    try {
      await deleteMutation.mutateAsync(group.id)
      toast.success('Đã xóa nhóm kênh')
    } catch (err) {
      toast.error(getErrorMessage(err))
    }
  }

  const isSaving = createMutation.isPending || updateMutation.isPending

  return (
    <div className="channel-group-tab">
      <div className="channel-group-tab-toolbar">
        <h2 className="platforms-section-title">Nhóm kênh</h2>
        {canManageGroups && (
          <button type="button" className="btn btn-primary" onClick={openCreate}>
            + Tạo nhóm
          </button>
        )}
      </div>

      {(isLoading || channelsLoading) && <LoadingState />}
      {isError && <ErrorState message={getErrorMessage(error)} onRetry={refetch} />}

      {!isLoading && !isError && sortedGroups.length === 0 && (
        <EmptyState message="Chưa có nhóm kênh nào." />
      )}

      {!isLoading && !isError && sortedGroups.length > 0 && (
        <div className="card channel-group-table-wrap">
          <table className="channel-group-table">
            <thead>
              <tr>
                <th>Tên nhóm</th>
                <th>Số kênh</th>
                {canManageGroups && <th />}
              </tr>
            </thead>
            <tbody>
              {sortedGroups.map((group) => (
                <tr key={group.id}>
                  <td>
                    <strong>{group.name}</strong>
                    {group.description ? (
                      <div className="channel-group-desc">{group.description}</div>
                    ) : null}
                  </td>
                  <td>{group.channelCount ?? group.channels?.length ?? 0}</td>
                  {canManageGroups && (
                    <td className="channel-group-actions">
                      <button
                        type="button"
                        className="btn btn-ghost btn-sm"
                        onClick={() => openEdit(group)}
                      >
                        Sửa
                      </button>
                      <button
                        type="button"
                        className="btn btn-ghost btn-sm channel-group-delete"
                        onClick={() => handleDelete(group)}
                        disabled={deleteMutation.isPending}
                      >
                        Xóa
                      </button>
                    </td>
                  )}
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      <Modal
        open={modalOpen}
        title={editing ? 'Sửa nhóm kênh' : 'Tạo nhóm kênh'}
        onClose={closeModal}
        footer={
          <>
            <button type="button" className="btn btn-secondary" onClick={closeModal} disabled={isSaving}>
              Hủy
            </button>
            <button
              type="submit"
              form="channel-group-form"
              className="btn btn-primary"
              disabled={isSaving}
            >
              {isSaving ? 'Đang lưu…' : 'Lưu'}
            </button>
          </>
        }
      >
        <form id="channel-group-form" onSubmit={handleSubmit} className="channel-group-form">
          <div className="form-group">
            <label htmlFor="channel-group-name">Tên nhóm</label>
            <input
              id="channel-group-name"
              value={name}
              onChange={(e) => setName(e.target.value)}
              placeholder="VD: Fanpage miền Bắc"
              autoFocus
            />
          </div>
          <div className="form-group">
            <label htmlFor="channel-group-desc">Mô tả (tuỳ chọn)</label>
            <input
              id="channel-group-desc"
              value={description}
              onChange={(e) => setDescription(e.target.value)}
              placeholder="Ghi chú ngắn"
            />
          </div>
          <div className="form-group">
            <ChannelMultiSelect
              channels={channels}
              value={channelIds}
              onChange={setChannelIds}
              label="Kênh trong nhóm"
              placeholder="Chọn kênh"
            />
          </div>
          {formError ? (
            <p className="form-error" role="alert">{formError}</p>
          ) : null}
        </form>
      </Modal>
    </div>
  )
}
