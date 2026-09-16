import { useEffect, useRef, useState } from 'react'
import Modal from '@/shared/components/Modal'
import { getErrorMessage } from '@/shared/utils/apiHelpers'
import { useBulkCreateMediaFolder } from '../hooks/useMediaFolders'

/** Khớp backend BulkDuplicatePolicy (MediaFolderDtos.cs): Error = 0, Skip = 1. */
const DUPLICATE_POLICY = { ERROR: 0, SKIP: 1 }

function buildPayload({ socialChannelId, parentFolderId, duplicatePolicy, rows, validateOnly }) {
  return {
    socialChannelId,
    parentFolderId: parentFolderId || null,
    duplicatePolicy: Number(duplicatePolicy),
    validateOnly,
    folders: rows.map((r) => ({
      clientRef: r.ref,
      name: r.name.trim(),
      parentRef: r.parentRef || null,
      sortOrder: 0,
    })),
  }
}

function snapshotKey(duplicatePolicy, rows) {
  return JSON.stringify({
    duplicatePolicy,
    rows: rows.map((r) => ({ name: r.name.trim(), parentRef: r.parentRef })),
  })
}

/**
 * MEDIA-06: modal tạo nhiều MediaFolder cùng lúc theo hierarchy (clientRef/parentRef),
 * dùng chung endpoint bulk của MEDIA-03. Luôn bắt buộc Preview (validateOnly) khớp với
 * input hiện tại trước khi cho Submit — sửa bất kỳ dòng nào sau khi preview sẽ vô hiệu
 * preview cũ (so theo snapshot), không cho submit dữ liệu chưa được xem trước.
 */
export default function MediaFolderBulkCreateModal({
  open,
  socialChannelId,
  parentFolderId = null,
  onClose,
  onSuccess,
}) {
  const refCounter = useRef(1)
  const makeRow = () => ({ ref: `n${refCounter.current++}`, name: '', parentRef: '' })

  const [rows, setRows] = useState(() => [makeRow()])
  const [duplicatePolicy, setDuplicatePolicy] = useState(DUPLICATE_POLICY.ERROR)
  const [preview, setPreview] = useState(null)
  const [previewSnapshot, setPreviewSnapshot] = useState('')
  const [previewError, setPreviewError] = useState('')
  const [submitError, setSubmitError] = useState('')

  const bulkCreateMutation = useBulkCreateMediaFolder()

  useEffect(() => {
    if (!open) return
    refCounter.current = 1
    setRows([makeRow()])
    setDuplicatePolicy(DUPLICATE_POLICY.ERROR)
    setPreview(null)
    setPreviewSnapshot('')
    setPreviewError('')
    setSubmitError('')
  }, [open])

  const hasEmptyName = rows.some((r) => !r.name.trim())
  const isPreviewFresh = Boolean(preview) && previewSnapshot === snapshotKey(duplicatePolicy, rows)

  const addRow = () => setRows((prev) => [...prev, makeRow()])

  const updateRow = (ref, patch) =>
    setRows((prev) => prev.map((r) => (r.ref === ref ? { ...r, ...patch } : r)))

  const removeRow = (ref) =>
    setRows((prev) =>
      prev
        .filter((r) => r.ref !== ref)
        .map((r) => (r.parentRef === ref ? { ...r, parentRef: '' } : r)),
    )

  const handlePreview = async () => {
    setPreviewError('')
    try {
      const result = await bulkCreateMutation.mutateAsync(
        buildPayload({ socialChannelId, parentFolderId, duplicatePolicy, rows, validateOnly: true }),
      )
      setPreview(result)
      setPreviewSnapshot(snapshotKey(duplicatePolicy, rows))
    } catch (err) {
      setPreview(null)
      setPreviewSnapshot('')
      setPreviewError(getErrorMessage(err))
    }
  }

  const handleSubmit = async () => {
    setSubmitError('')
    try {
      await bulkCreateMutation.mutateAsync(
        buildPayload({ socialChannelId, parentFolderId, duplicatePolicy, rows, validateOnly: false }),
      )
      onSuccess?.()
      onClose()
    } catch (err) {
      // Batch nguyên tử: lỗi = không gì được tạo. Giữ nguyên rows để sửa, không đóng
      // modal, không báo thành công một phần (MEDIA-06-AC2).
      setSubmitError(getErrorMessage(err))
    }
  }

  return (
    <Modal
      open={open}
      title="Tạo thư mục hàng loạt"
      onClose={onClose}
      footer={(
        <>
          <button type="button" className="btn btn-secondary" onClick={onClose}>Hủy</button>
          <button
            type="button"
            className="btn btn-secondary"
            disabled={hasEmptyName || bulkCreateMutation.isPending}
            onClick={handlePreview}
          >
            {bulkCreateMutation.isPending && !isPreviewFresh ? 'Đang xem trước...' : 'Xem trước'}
          </button>
          <button
            type="button"
            className="btn btn-primary"
            disabled={!isPreviewFresh || bulkCreateMutation.isPending}
            onClick={handleSubmit}
          >
            {bulkCreateMutation.isPending && isPreviewFresh
              ? 'Đang tạo...'
              : `Tạo ${preview?.totalRequested ?? rows.length} thư mục`}
          </button>
        </>
      )}
    >
      {submitError && <div className="alert alert-error">{submitError}</div>}
      <p className="form-hint">
        Tạo nhiều thư mục cùng lúc trong Page hiện tại
        {parentFolderId ? ', dưới thư mục đang mở' : ', ở thư mục gốc'}. Mỗi dòng có thể chọn
        làm con của một dòng khác trong danh sách để dựng hierarchy nhiều cấp.
      </p>

      <div className="form-group">
        <label htmlFor="bulk-duplicate-policy">Trùng tên trong cùng thư mục cha</label>
        <select
          id="bulk-duplicate-policy"
          value={duplicatePolicy}
          onChange={(event) => setDuplicatePolicy(Number(event.target.value))}
        >
          <option value={DUPLICATE_POLICY.ERROR}>Báo lỗi</option>
          <option value={DUPLICATE_POLICY.SKIP}>Bỏ qua — dùng thư mục có sẵn</option>
        </select>
      </div>

      <div className="bulk-folder-rows">
        {rows.map((row) => (
          <div className="bulk-folder-row" key={row.ref}>
            <input
              value={row.name}
              placeholder="Tên thư mục"
              aria-label="Tên thư mục"
              onChange={(event) => updateRow(row.ref, { name: event.target.value })}
            />
            <select
              value={row.parentRef}
              aria-label="Thư mục cha trong batch"
              onChange={(event) => updateRow(row.ref, { parentRef: event.target.value })}
            >
              <option value="">— Gốc của batch —</option>
              {rows.filter((r) => r.ref !== row.ref).map((r) => (
                <option key={r.ref} value={r.ref}>{r.name.trim() || '(chưa đặt tên)'}</option>
              ))}
            </select>
            <button
              type="button"
              className="btn btn-ghost btn-sm"
              title="Xóa dòng"
              disabled={rows.length <= 1}
              onClick={() => removeRow(row.ref)}
            >
              🗑
            </button>
          </div>
        ))}
      </div>
      <button type="button" className="btn btn-secondary btn-sm" onClick={addRow}>+ Thêm dòng</button>

      {previewError && <div className="alert alert-error" style={{ marginTop: 12 }}>{previewError}</div>}

      {isPreviewFresh && preview && (
        <div className="bulk-folder-preview">
          <p className="form-hint">
            Sẽ tạo {preview.totalCreated} thư mục mới
            {preview.totalSkipped > 0 ? `, dùng lại ${preview.totalSkipped} thư mục có sẵn` : ''}.
          </p>
          <ul>
            {preview.folders.map((f) => (
              <li key={f.clientRef} style={{ paddingLeft: 8 + f.depth * 16 }}>
                📁 {f.name} {f.isSkipped && <span className="badge">dùng lại</span>}
              </li>
            ))}
          </ul>
        </div>
      )}
    </Modal>
  )
}
