import { useEffect, useState } from 'react'
import Modal from '@/shared/components/Modal'
import { getErrorMessage } from '@/shared/utils/apiHelpers'
import { useSocialChannelAll } from '@/modules/social-channels/hooks/useSocialChannels'
import { useCreateMediaFolderAcrossPages } from '../hooks/useMediaFolders'

/**
 * MEDIA-06: tạo 1 folder gốc cùng tên ở nhiều Page cùng lúc (vd 100 Page → 100 folder).
 * Best-effort: bấm nút một lần, mỗi Page tạo/lưu độc lập — một Page lỗi (không có quyền,
 * tên trống, v.v.) không chặn các Page khác. Sau khi chạy xong hiển thị kết quả từng Page
 * (thành công/lỗi) thay vì tự đóng modal, vì ngay cả kết quả "thành công" cũng chỉ đúng
 * một phần khi có Page lỗi.
 */
export default function MediaFolderBulkCreateModal({ open, onClose, onSuccess }) {
  const { data: channels = [] } = useSocialChannelAll()
  const [name, setName] = useState('')
  const [description, setDescription] = useState('')
  const [selectedIds, setSelectedIds] = useState(() => new Set())
  const [submitError, setSubmitError] = useState('')
  const [result, setResult] = useState(null)

  const mutation = useCreateMediaFolderAcrossPages()

  useEffect(() => {
    if (!open) return
    setName('')
    setDescription('')
    setSelectedIds(new Set())
    setSubmitError('')
    setResult(null)
  }, [open])

  const toggleChannel = (id) =>
    setSelectedIds((prev) => {
      const next = new Set(prev)
      if (next.has(id)) next.delete(id)
      else next.add(id)
      return next
    })

  const toggleAll = () =>
    setSelectedIds((prev) => (prev.size === channels.length ? new Set() : new Set(channels.map((c) => c.id))))

  const handleSubmit = async () => {
    setSubmitError('')
    try {
      const response = await mutation.mutateAsync({
        name: name.trim(),
        description: description.trim() || null,
        socialChannelIds: [...selectedIds],
      })
      setResult(response)
      onSuccess?.(response)
    } catch (err) {
      setSubmitError(getErrorMessage(err))
    }
  }

  const canSubmit = Boolean(name.trim()) && selectedIds.size > 0 && !mutation.isPending
  const channelName = (id) => channels.find((c) => c.id === id)?.pageName ?? id

  return (
    <Modal
      open={open}
      title="Tạo thư mục hàng loạt theo Page"
      onClose={onClose}
      footer={result ? (
        <button type="button" className="btn btn-primary" onClick={onClose}>Đóng</button>
      ) : (
        <>
          <button type="button" className="btn btn-secondary" onClick={onClose}>Hủy</button>
          <button type="button" className="btn btn-primary" disabled={!canSubmit} onClick={handleSubmit}>
            {mutation.isPending
              ? 'Đang tạo...'
              : `Tạo trong ${selectedIds.size || 0} Page`}
          </button>
        </>
      )}
    >
      {submitError && <div className="alert alert-error">{submitError}</div>}

      {result ? (
        <div className="bulk-across-pages-result">
          <p className="form-hint">
            Đã tạo {result.totalSucceeded}/{result.totalRequested} thư mục
            {result.totalFailed > 0 ? `, ${result.totalFailed} Page lỗi` : ''}.
          </p>
          <ul>
            {result.results.map((r) => (
              <li key={r.socialChannelId} className={r.success ? 'is-success' : 'is-error'}>
                {r.success ? '✅' : '❌'} {channelName(r.socialChannelId)}
                {!r.success && r.errorMessage ? ` — ${r.errorMessage}` : ''}
              </li>
            ))}
          </ul>
        </div>
      ) : (
        <>
          <p className="form-hint">
            Tạo cùng một thư mục gốc, cùng tên, ở tất cả Page được chọn bên dưới. Mỗi Page
            được xử lý độc lập — một Page lỗi không ảnh hưởng các Page khác.
          </p>

          <div className="form-group">
            <label htmlFor="bulk-across-name">Tên thư mục</label>
            <input
              id="bulk-across-name"
              value={name}
              onChange={(event) => setName(event.target.value)}
              placeholder="VD: Campaign Tháng 10"
              autoFocus
            />
          </div>

          <div className="form-group">
            <label htmlFor="bulk-across-description">Mô tả (tùy chọn)</label>
            <textarea
              id="bulk-across-description"
              value={description}
              onChange={(event) => setDescription(event.target.value)}
              rows={2}
            />
          </div>

          <div className="form-group">
            <div className="bulk-across-pages-header">
              <label>Chọn Page ({selectedIds.size}/{channels.length})</label>
              <button type="button" className="btn btn-ghost btn-sm" onClick={toggleAll}>
                {selectedIds.size === channels.length ? 'Bỏ chọn tất cả' : 'Chọn tất cả'}
              </button>
            </div>
            <div className="bulk-across-pages-list">
              {channels.map((channel) => (
                <label key={channel.id} className="bulk-across-pages-item">
                  <input
                    type="checkbox"
                    checked={selectedIds.has(channel.id)}
                    onChange={() => toggleChannel(channel.id)}
                  />
                  {channel.pageName}
                </label>
              ))}
            </div>
          </div>
        </>
      )}
    </Modal>
  )
}
