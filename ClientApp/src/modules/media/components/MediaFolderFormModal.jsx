import { useEffect, useState } from 'react'
import Modal from '@/shared/components/Modal'
import { useWritableMediaFolderPages } from '../hooks/useMediaFolders'
import MediaFolderPickerTree from './MediaFolderPickerTree'

/**
 * Tạo mới / đổi tên thư mục media. Khi `editing` có giá trị → chế độ sửa (1 Page, không đổi).
 *
 * Khi tạo mới, "Gắn với Page" là multi-select:
 * - Chọn 0 hoặc 1 Page: hành vi cũ — 1 ô Tên thư mục (chọn đúng 1 Page tự điền tên = tên
 *   Page, vẫn sửa được), có thể chọn thư mục cha qua MediaFolderPickerTree.
 * - Chọn từ 2 Page trở lên: ẩn ô Tên thư mục và thư mục cha (một ParentFolderId chỉ thuộc
 *   đúng 1 Page) — MỖI folder tự lấy tên theo đúng tên Page tương ứng. Payload gửi
 *   `items: [{socialChannelId, name}]` để component cha (MediaPage) biết gọi mutation nào.
 */
export default function MediaFolderFormModal({
  open,
  editing = null,
  defaultParentId = null,
  defaultSocialChannelId = null,
  onClose,
  onSubmit,
  isSubmitting = false,
  errorMessage = '',
}) {
  const [name, setName] = useState('')
  const [parentFolderId, setParentFolderId] = useState('')
  const [socialChannelId, setSocialChannelId] = useState('')
  const [selectedPageIds, setSelectedPageIds] = useState(() => new Set())

  // Chỉ Page mà actor có quyền tạo MediaFolder — khác GET /api/SocialChannel (không lọc quyền),
  // để "Chọn tất cả" không chọn nhầm Page mà actor không sở hữu (silent-fail khi submit).
  const { data: channels = [] } = useWritableMediaFolderPages()

  useEffect(() => {
    if (!open) return
    setName(editing?.name ?? '')
    setParentFolderId(editing?.parentFolderId ?? defaultParentId ?? '')
    const initialPageId = editing?.socialChannelId ?? defaultSocialChannelId ?? ''
    setSocialChannelId(initialPageId)
    setSelectedPageIds(new Set(initialPageId ? [initialPageId] : []))
  }, [open, editing, defaultParentId, defaultSocialChannelId])

  // Chọn từ 2 Page trở lên thì thư mục cha không còn ý nghĩa (thuộc riêng 1 Page) — bỏ chọn.
  useEffect(() => {
    if (selectedPageIds.size > 1) setParentFolderId('')
  }, [selectedPageIds])

  const togglePage = (id) =>
    setSelectedPageIds((prev) => {
      const next = new Set(prev)
      if (next.has(id)) next.delete(id)
      else next.add(id)
      // Vừa chọn đúng 1 Page: tự điền tên = tên Page (như <select> đơn trước đây), vẫn sửa được.
      if (next.size === 1) {
        const channel = channels.find((c) => c.id === [...next][0])
        if (channel) setName(channel.pageName)
      }
      return next
    })

  const toggleAllPages = () =>
    setSelectedPageIds((prev) => (prev.size === channels.length ? new Set() : new Set(channels.map((c) => c.id))))

  const handleSubmit = (event) => {
    event.preventDefault()

    if (editing) {
      if (!name.trim()) return
      onSubmit({
        name: name.trim(),
        parentFolderId: parentFolderId || null,
        socialChannelId: socialChannelId || null,
      })
      return
    }

    const pageIds = [...selectedPageIds]
    if (pageIds.length <= 1) {
      if (!name.trim()) return
      onSubmit({
        name: name.trim(),
        parentFolderId: parentFolderId || null,
        socialChannelId: pageIds[0] || null,
      })
      return
    }

    // 2+ Page: mỗi folder tự lấy tên theo đúng Page của nó, không dùng ô Tên thư mục chung.
    const items = pageIds.map((id) => ({
      socialChannelId: id,
      name: channels.find((c) => c.id === id)?.pageName ?? id,
    }))
    onSubmit({ items })
  }

  const isMultiPage = !editing && selectedPageIds.size > 1
  const pickerSocialChannelId = editing
    ? (socialChannelId || defaultSocialChannelId)
    : ([...selectedPageIds][0] || defaultSocialChannelId)

  return (
    <Modal
      open={open}
      title={editing ? 'Đổi tên / di chuyển thư mục' : 'Tạo thư mục'}
      onClose={onClose}
      footer={(
        <>
          <button type="button" className="btn btn-secondary" onClick={onClose}>Hủy</button>
          <button
            type="submit"
            form="media-folder-form"
            className="btn btn-primary"
            disabled={isSubmitting}
          >
            {isSubmitting ? 'Đang lưu...' : 'Lưu'}
          </button>
        </>
      )}
    >
      <form id="media-folder-form" onSubmit={handleSubmit}>
        {errorMessage && <div className="alert alert-error">{errorMessage}</div>}

        {!isMultiPage && (
          <div className="form-group">
            <label htmlFor="folder-name">Tên thư mục</label>
            <input
              id="folder-name"
              value={name}
              onChange={(event) => setName(event.target.value)}
              placeholder="VD: Logo, Banner, Sản phẩm..."
              autoFocus
            />
          </div>
        )}

        {(editing || selectedPageIds.size <= 1) && (
          <div className="form-group">
            <label htmlFor="folder-parent">Thư mục cha</label>
            <MediaFolderPickerTree
              socialChannelId={pickerSocialChannelId}
              value={parentFolderId || null}
              onChange={(id) => setParentFolderId(id || '')}
              excludeFolderId={editing?.id ?? null}
            />
          </div>
        )}
        {isMultiPage && (
          <p className="form-hint">
            Mỗi Page đã chọn sẽ có 1 thư mục gốc, tên trùng tên Page đó (không chọn được thư mục cha khi chọn nhiều Page).
          </p>
        )}

        {editing ? (
          <div className="form-group">
            <label htmlFor="folder-page">Gắn với Page (Tùy chọn)</label>
            <select
              id="folder-page"
              value={socialChannelId}
              onChange={(event) => setSocialChannelId(event.target.value)}
            >
              <option value="">— Không gắn page —</option>
              {channels.map((c) => (
                <option key={c.id} value={c.id}>{c.pageName}</option>
              ))}
            </select>
          </div>
        ) : (
          <div className="form-group">
            <div className="media-folder-page-multiselect-header">
              <label>Gắn với Page (Tùy chọn — chọn nhiều để tạo 1 thư mục cùng tên Page ở mỗi Page)</label>
              {channels.length > 0 && (
                <button type="button" className="btn btn-ghost btn-sm" onClick={toggleAllPages}>
                  {selectedPageIds.size === channels.length ? 'Bỏ chọn tất cả' : 'Chọn tất cả'}
                </button>
              )}
            </div>
            <div className="media-folder-page-multiselect-list">
              {channels.map((c) => (
                <label key={c.id} className="media-folder-page-multiselect-item">
                  <input
                    type="checkbox"
                    checked={selectedPageIds.has(c.id)}
                    onChange={() => togglePage(c.id)}
                  />
                  {c.pageName}
                </label>
              ))}
            </div>
          </div>
        )}
      </form>
    </Modal>
  )
}
