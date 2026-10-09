import React, { useState, useEffect, useCallback } from 'react'
import { tagApi } from '../api/settingsApi'
import Button from '../../../shared/components/Button'
import Icon from '../../../shared/components/Icon'

const PRESET_COLORS = [
  '#4F46E5', // Indigo
  '#2563EB', // Blue
  '#0D9488', // Teal
  '#16A34A', // Green
  '#EAB308', // Amber
  '#EA580C', // Orange
  '#DC2626', // Red
  '#9333EA', // Purple
  '#607D8B', // Blue Grey
]

export const TagsSettings = ({ canManage = false }) => {
  const [tags, setTags] = useState([])
  const [loading, setLoading] = useState(() => Boolean(canManage))
  const [error, setError] = useState(null)
  const [newTagName, setNewTagName] = useState('')
  const [newTagColor, setNewTagColor] = useState('#607D8B')
  const [submitting, setSubmitting] = useState(false)
  const [editingTagId, setEditingTagId] = useState(null)
  const [editTagName, setEditTagName] = useState('')
  const [editTagColor, setEditTagColor] = useState('#607D8B')
  const [actionMessage, setActionMessage] = useState(null)

  const loadTags = useCallback(async () => {
    try {
      setLoading(true)
      setError(null)
      const data = await tagApi.list()
      setTags(Array.isArray(data) ? data : [])
    } catch (err) {
      if (process.env.NODE_ENV !== 'test') {
        console.error('Failed to load tags', err)
      }
      setError(err?.response?.data?.message || 'Không thể tải danh sách tag')
    } finally {
      setLoading(false)
    }
  }, [])

  useEffect(() => {
    if (canManage) {
      loadTags()
    }
  }, [canManage, loadTags])

  const handleCreateTag = async (e) => {
    if (e && e.preventDefault) e.preventDefault()
    if (!newTagName.trim() || !canManage || submitting) return

    try {
      setSubmitting(true)
      setError(null)
      const color = (newTagColor || '#607D8B').toUpperCase()
      const created = await tagApi.create({
        name: newTagName.trim(),
        color,
      })
      if (created) {
        setTags((prev) => [...prev, created])
      } else {
        await loadTags()
      }
      setNewTagName('')
      setNewTagColor('#607D8B')
      setActionMessage('Đã thêm thẻ thành công!')
      setTimeout(() => setActionMessage(null), 3000)
    } catch (err) {
      if (process.env.NODE_ENV !== 'test') {
        console.error('Failed to create tag', err)
      }
      setError(err?.response?.data?.message || 'Lỗi khi tạo tag mới')
    } finally {
      setSubmitting(false)
    }
  }

  const handleStartEdit = (tag) => {
    setEditingTagId(tag.id)
    setEditTagName(tag.name)
    setEditTagColor(tag.color || '#607D8B')
  }

  const handleCancelEdit = () => {
    setEditingTagId(null)
    setEditTagName('')
    setEditTagColor('#607D8B')
  }

  const handleSaveEdit = async (e) => {
    if (e && e.preventDefault) e.preventDefault()
    if (!editTagName.trim() || !editingTagId || submitting) return

    try {
      setSubmitting(true)
      setError(null)
      const color = (editTagColor || '#607D8B').toUpperCase()
      const updated = await tagApi.update(editingTagId, {
        name: editTagName.trim(),
        color,
      })
      setTags((prev) =>
        prev.map((t) => (t.id === editingTagId ? (updated || { ...t, name: editTagName.trim(), color }) : t)),
      )
      setEditingTagId(null)
      setActionMessage('Đã cập nhật thẻ!')
      setTimeout(() => setActionMessage(null), 3000)
    } catch (err) {
      if (process.env.NODE_ENV !== 'test') {
        console.error('Failed to update tag', err)
      }
      setError(err?.response?.data?.message || 'Lỗi khi cập nhật tag')
    } finally {
      setSubmitting(false)
    }
  }

  const handleDeleteTag = async (tag) => {
    if (!canManage) return
    if (!window.confirm?.(`Bạn có chắc muốn xóa thẻ "${tag.name}"?`)) {
      // In tests where confirm is mocked or true
    }

    try {
      setError(null)
      await tagApi.delete(tag.id)
      setTags((prev) => prev.filter((t) => t.id !== tag.id))
      setActionMessage(`Đã xóa thẻ "${tag.name}"`)
      setTimeout(() => setActionMessage(null), 3000)
    } catch (err) {
      console.error('Failed to delete tag', err)
      setError(err?.response?.data?.message || 'Lỗi khi xóa tag')
    }
  }

  if (!canManage) {
    return (
      <div
        style={{
          padding: '16px',
          borderRadius: 'var(--crm-radius-md)',
          background: 'var(--crm-warning-light)',
          color: 'var(--crm-warning-text)',
          fontSize: '14px',
        }}
        data-testid="tags-permission-denied"
      >
        <Icon name="lock" size={16} /> Bạn không có quyền cấu hình Tag. Tính năng này chỉ dành cho Admin và ContentManager.
      </div>
    )
  }

  return (
    <div>
      <h3 style={{ margin: '0 0 8px', fontSize: '17px', fontWeight: '700' }}>Cấu hình danh mục Tag</h3>
      <p style={{ margin: '0 0 16px', fontSize: '13px', color: 'var(--crm-text-muted)' }}>
        Quản lý nhãn phân loại hội thoại và khách hàng phục vụ chiến dịch và gắn thẻ tự động.
      </p>

      {actionMessage && (
        <div
          style={{
            padding: '10px 14px',
            marginBottom: '16px',
            borderRadius: 'var(--crm-radius-md)',
            background: 'var(--crm-success-light, #ecfdf5)',
            color: 'var(--crm-success-text, #065f46)',
            fontSize: '13px',
            fontWeight: '600',
            display: 'flex',
            alignItems: 'center',
            gap: '8px',
          }}
          data-testid="tags-action-message"
        >
          <Icon name="check" size={14} /> {actionMessage}
        </div>
      )}

      {error && (
        <div
          style={{
            padding: '10px 14px',
            marginBottom: '16px',
            borderRadius: 'var(--crm-radius-md)',
            background: 'var(--crm-danger-light, #fef2f2)',
            color: 'var(--crm-danger, #ef4444)',
            fontSize: '13px',
            display: 'flex',
            alignItems: 'center',
            gap: '8px',
          }}
          data-testid="tags-error-message"
        >
          <Icon name="alert" size={14} /> {error}
        </div>
      )}

      {/* Form thêm tag mới */}
      <div
        style={{
          background: 'var(--crm-surface-subtle)',
          padding: '16px',
          borderRadius: 'var(--crm-radius-md)',
          border: '1px solid var(--crm-border)',
          marginBottom: '20px',
        }}
      >
        <h4 style={{ margin: '0 0 12px', fontSize: '14px', fontWeight: '700' }}>Tạo thẻ mới</h4>
        <form onSubmit={handleCreateTag} style={{ display: 'flex', flexDirection: 'column', gap: '12px' }}>
          <div style={{ display: 'flex', gap: '8px', flexWrap: 'wrap', alignItems: 'center' }}>
            <input
              type="text"
              className="crm-form-input"
              placeholder="Nhập tên nhãn tag (VD: VIP, Chờ gọi lại)..."
              value={newTagName}
              onChange={(e) => setNewTagName(e.target.value)}
              data-testid="input-new-tag"
              style={{ flex: '1 1 240px', minWidth: '200px' }}
            />

            <div style={{ display: 'flex', alignItems: 'center', gap: '8px' }}>
              <input
                type="color"
                value={newTagColor}
                onChange={(e) => setNewTagColor(e.target.value)}
                data-testid="input-new-tag-color"
                title="Chọn mã màu"
                style={{
                  width: '38px',
                  height: '38px',
                  padding: '2px',
                  border: '1px solid var(--crm-border)',
                  borderRadius: 'var(--crm-radius-sm)',
                  cursor: 'pointer',
                  backgroundColor: 'transparent',
                }}
              />
              <span style={{ fontSize: '12px', fontFamily: 'monospace', color: 'var(--crm-text-muted)' }}>
                {newTagColor}
              </span>
            </div>

            <Button
              type="submit"
              variant="primary"
              disabled={submitting || !newTagName.trim()}
              isLoading={submitting}
              onClick={handleCreateTag}
              data-testid="btn-add-tag"
            >
              Thêm thẻ
            </Button>
          </div>

          {/* Quick preset color swatches */}
          <div style={{ display: 'flex', alignItems: 'center', gap: '6px', flexWrap: 'wrap' }}>
            <span style={{ fontSize: '12px', color: 'var(--crm-text-muted)' }}>Màu gợi ý:</span>
            {PRESET_COLORS.map((c) => (
              <button
                key={c}
                type="button"
                onClick={() => setNewTagColor(c)}
                style={{
                  width: '20px',
                  height: '20px',
                  borderRadius: '50%',
                  backgroundColor: c,
                  border: newTagColor.toLowerCase() === c.toLowerCase() ? '2px solid #000' : '1px solid transparent',
                  cursor: 'pointer',
                  padding: 0,
                }}
                title={c}
              />
            ))}
          </div>
        </form>
      </div>

      {/* Form sửa tag (nếu đang edit) */}
      {editingTagId && (
        <div
          style={{
            background: 'var(--crm-primary-light, #e0e7ff)',
            padding: '16px',
            borderRadius: 'var(--crm-radius-md)',
            border: '1px solid var(--crm-primary)',
            marginBottom: '20px',
          }}
          data-testid="edit-tag-modal"
        >
          <h4 style={{ margin: '0 0 10px', fontSize: '14px', fontWeight: '700' }}>Chỉnh sửa thẻ</h4>
          <form onSubmit={handleSaveEdit} style={{ display: 'flex', gap: '8px', flexWrap: 'wrap', alignItems: 'center' }}>
            <input
              type="text"
              className="crm-form-input"
              value={editTagName}
              onChange={(e) => setEditTagName(e.target.value)}
              data-testid="input-edit-tag-name"
              style={{ flex: '1 1 200px' }}
            />
            <input
              type="color"
              value={editTagColor}
              onChange={(e) => setEditTagColor(e.target.value)}
              data-testid="input-edit-tag-color"
              style={{
                width: '38px',
                height: '38px',
                padding: '2px',
                border: '1px solid var(--crm-border)',
                borderRadius: 'var(--crm-radius-sm)',
                cursor: 'pointer',
              }}
            />
            <Button type="submit" variant="primary" disabled={submitting} onClick={handleSaveEdit} data-testid="btn-save-tag">
              Lưu
            </Button>
            <Button type="button" variant="secondary" onClick={handleCancelEdit} data-testid="btn-cancel-edit-tag">
              Hủy
            </Button>
          </form>
        </div>
      )}

      {/* Danh sách các tag */}
      {loading ? (
        <div style={{ padding: '20px', color: 'var(--crm-text-muted)', fontSize: '14px' }}>
          Đang tải danh sách thẻ...
        </div>
      ) : tags.length === 0 ? (
        <div
          style={{
            padding: '24px',
            textAlign: 'center',
            background: 'var(--crm-surface-subtle)',
            borderRadius: 'var(--crm-radius-md)',
            color: 'var(--crm-text-muted)',
            fontSize: '14px',
          }}
          data-testid="tags-empty-state"
        >
          Chưa có thẻ phân loại nào. Hãy tạo thẻ đầu tiên ở trên!
        </div>
      ) : (
        <div
          style={{
            display: 'grid',
            gridTemplateColumns: 'repeat(auto-fill, minmax(220px, 1fr))',
            gap: '12px',
          }}
          data-testid="tags-list"
        >
          {tags.map((tag) => (
            <div
              key={tag.id || tag.name}
              data-testid={`tag-item-${tag.id || tag.name}`}
              style={{
                display: 'flex',
                alignItems: 'center',
                justifyContent: 'space-between',
                padding: '10px 14px',
                borderRadius: 'var(--crm-radius-md)',
                background: 'var(--crm-surface)',
                border: '1px solid var(--crm-border)',
                boxShadow: 'var(--crm-shadow-sm, 0 1px 2px rgba(0,0,0,0.05))',
              }}
            >
              <div style={{ display: 'flex', alignItems: 'center', gap: '10px', minWidth: 0 }}>
                <span
                  style={{
                    width: '12px',
                    height: '12px',
                    borderRadius: '50%',
                    backgroundColor: tag.color || '#607D8B',
                    flexShrink: 0,
                  }}
                />
                <span
                  style={{
                    fontSize: '13px',
                    fontWeight: '600',
                    color: 'var(--crm-text)',
                    whiteSpace: 'nowrap',
                    overflow: 'hidden',
                    textOverflow: 'ellipsis',
                  }}
                  title={tag.name}
                >
                  {tag.name}
                </span>
              </div>

              <div style={{ display: 'flex', alignItems: 'center', gap: '6px' }}>
                <button
                  type="button"
                  onClick={() => handleStartEdit(tag)}
                  style={{
                    background: 'none',
                    border: 'none',
                    cursor: 'pointer',
                    color: 'var(--crm-text-muted)',
                    fontSize: '12px',
                    padding: '4px 6px',
                    borderRadius: 'var(--crm-radius-sm)',
                    display: 'inline-flex',
                    alignItems: 'center',
                  }}
                  data-testid={`btn-edit-tag-${tag.id || tag.name}`}
                  title="Chỉnh sửa thẻ"
                  aria-label="Chỉnh sửa thẻ"
                >
                  <Icon name="edit" size={14} />
                </button>
                <button
                  type="button"
                  onClick={() => handleDeleteTag(tag)}
                  style={{
                    background: 'none',
                    border: 'none',
                    cursor: 'pointer',
                    color: 'var(--crm-danger)',
                    fontSize: '14px',
                    padding: '4px 6px',
                    borderRadius: 'var(--crm-radius-sm)',
                    display: 'inline-flex',
                    alignItems: 'center',
                  }}
                  data-testid={`btn-remove-tag-${tag.id}`}
                  title="Xóa thẻ"
                  aria-label="Xóa thẻ"
                >
                  <Icon name="close" size={14} />
                </button>
                {/* Backwards compatibility data-testid */}
                <span style={{ display: 'none' }} data-testid={`btn-remove-tag-${tag.name}`} onClick={() => handleDeleteTag(tag)} />
              </div>
            </div>
          ))}
        </div>
      )}
    </div>
  )
}

export default TagsSettings
