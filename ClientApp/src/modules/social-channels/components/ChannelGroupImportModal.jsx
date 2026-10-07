import { useEffect, useRef, useState } from 'react'
import Modal from '@/shared/components/Modal'
import { getErrorMessage } from '@/shared/utils/apiHelpers'
import { toast } from '@/shared/stores/toastStore'
import {
  useCommitChannelGroupImport,
  usePreviewChannelGroupImport,
} from '../hooks/useChannelGroups'
import { downloadChannelGroupImportTemplate } from '../services/channelGroupApi'

const MODES = [
  {
    value: 'merge',
    label: 'Gộp',
    hint: 'Giữ kênh cũ trong nhóm, chỉ thêm kênh mới từ file.',
  },
  {
    value: 'replace',
    label: 'Thay thế',
    hint: 'Kênh của nhóm đúng bằng tập hợp lệ trong file (nhóm toàn dòng lỗi không bị làm rỗng).',
  },
]

/**
 * Modal nhập nhóm kênh từ CSV (CHANNEL-GROUP-01 / channel-group-csv-import).
 * Preview server-side → xác nhận commit; chỉ Admin/ContentManager mở từ tab.
 */
export default function ChannelGroupImportModal({ open, onClose }) {
  const fileRef = useRef(null)
  const [file, setFile] = useState(null)
  const [mode, setMode] = useState('merge')
  const [preview, setPreview] = useState(null)
  const [error, setError] = useState('')
  const [downloading, setDownloading] = useState(false)

  const previewMutation = usePreviewChannelGroupImport()
  const commitMutation = useCommitChannelGroupImport()

  const reset = () => {
    setFile(null)
    setMode('merge')
    setPreview(null)
    setError('')
    if (fileRef.current) fileRef.current.value = ''
  }

  const handleClose = () => {
    if (previewMutation.isPending || commitMutation.isPending) return
    reset()
    onClose()
  }

  const runPreview = async (nextFile, nextMode) => {
    if (!nextFile) {
      setPreview(null)
      return
    }
    setError('')
    try {
      const data = await previewMutation.mutateAsync({ file: nextFile, mode: nextMode })
      setPreview(data)
    } catch (err) {
      setPreview(null)
      setError(getErrorMessage(err))
    }
  }

  useEffect(() => {
    if (!open || !file) return
    runPreview(file, mode)
    // Chỉ re-preview khi đổi mode / file — không phụ thuộc mutation identity.
    // eslint-disable-next-line react-hooks/exhaustive-deps -- intentional
  }, [open, file, mode])

  const handleFileChange = (event) => {
    const chosen = event.target.files?.[0] ?? null
    setFile(chosen)
    setPreview(null)
    setError('')
  }

  const handleDownloadTemplate = async () => {
    setDownloading(true)
    setError('')
    try {
      await downloadChannelGroupImportTemplate()
      toast.success('Đã tải file mẫu CSV')
    } catch (err) {
      setError(getErrorMessage(err))
    } finally {
      setDownloading(false)
    }
  }

  const handleCommit = async () => {
    if (!file || !preview || (preview.validRowCount ?? 0) < 1) return
    setError('')
    try {
      const result = await commitMutation.mutateAsync({ file, mode })
      toast.success(
        `Nhập xong: tạo ${result.groupsCreated ?? 0}, cập nhật ${result.groupsUpdated ?? 0}`,
      )
      reset()
      onClose()
    } catch (err) {
      setError(getErrorMessage(err))
    }
  }

  const validCount = preview?.validRowCount ?? 0
  const groups = preview?.groups ?? []
  const errors = preview?.errors ?? []
  const busy = previewMutation.isPending || commitMutation.isPending

  return (
    <Modal
      open={open}
      title="Nhập CSV nhóm kênh"
      onClose={handleClose}
      footer={(
        <>
          <button
            type="button"
            className="btn btn-secondary"
            onClick={handleClose}
            disabled={busy}
          >
            Hủy
          </button>
          <button
            type="button"
            className="btn btn-primary"
            onClick={handleCommit}
            disabled={busy || validCount < 1}
          >
            {commitMutation.isPending ? 'Đang nhập…' : 'Xác nhận'}
          </button>
        </>
      )}
    >
      <div className="channel-group-import" style={{ display: 'flex', flexDirection: 'column', gap: 16 }}>
        <p style={{ margin: 0, fontSize: 14, lineHeight: 1.5, color: 'var(--color-text-muted, #64748b)' }}>
          Mở file mẫu bằng Excel, điền xong chọn Lưu thành → CSV UTF-8.
        </p>

        <div style={{ display: 'flex', flexWrap: 'wrap', gap: 8, alignItems: 'center' }}>
          <button
            type="button"
            className="btn btn-secondary"
            onClick={handleDownloadTemplate}
            disabled={downloading || busy}
          >
            {downloading ? 'Đang tải…' : 'Tải file mẫu'}
          </button>
          <label className="btn btn-ghost" style={{ cursor: 'pointer', margin: 0 }}>
            Chọn file .csv
            <input
              ref={fileRef}
              type="file"
              accept=".csv,text/csv"
              onChange={handleFileChange}
              style={{ display: 'none' }}
              data-testid="channel-group-import-file"
            />
          </label>
          {file ? (
            <span style={{ fontSize: 13 }} data-testid="channel-group-import-filename">
              {file.name}
            </span>
          ) : null}
        </div>

        <fieldset style={{ border: '1px solid var(--color-border, #e2e8f0)', borderRadius: 8, padding: 12, margin: 0 }}>
          <legend style={{ padding: '0 6px', fontWeight: 600 }}>Chế độ</legend>
          <div style={{ display: 'flex', flexDirection: 'column', gap: 10 }}>
            {MODES.map((item) => (
              <label
                key={item.value}
                style={{ display: 'flex', gap: 8, alignItems: 'flex-start', cursor: 'pointer' }}
              >
                <input
                  type="radio"
                  name="channel-group-import-mode"
                  value={item.value}
                  checked={mode === item.value}
                  onChange={() => setMode(item.value)}
                  disabled={busy}
                />
                <span>
                  <strong>{item.label}</strong>
                  <div style={{ fontSize: 13, color: 'var(--color-text-muted, #64748b)' }}>
                    {item.hint}
                  </div>
                </span>
              </label>
            ))}
          </div>
        </fieldset>

        {previewMutation.isPending ? (
          <p style={{ margin: 0, fontSize: 14 }}>Đang xem trước…</p>
        ) : null}

        {preview && !previewMutation.isPending ? (
          <>
            <div>
              <h3 style={{ margin: '0 0 8px', fontSize: 15 }}>
                Nhóm sẽ tạo / cập nhật
                {validCount > 0 ? ` (${validCount} dòng hợp lệ)` : ''}
              </h3>
              {groups.length === 0 ? (
                <p style={{ margin: 0, fontSize: 14 }}>Không có nhóm hợp lệ để nhập.</p>
              ) : (
                <div className="card" style={{ padding: 0, overflow: 'auto' }}>
                  <table className="channel-group-table" data-testid="channel-group-import-preview">
                    <thead>
                      <tr>
                        <th>Tên nhóm</th>
                        <th>Trạng thái</th>
                        <th>Thêm</th>
                        <th>Gỡ</th>
                      </tr>
                    </thead>
                    <tbody>
                      {groups.map((g) => (
                        <tr key={`${g.name}-${g.existingGroupId ?? 'new'}`}>
                          <td>{g.name}</td>
                          <td>{g.isNew ? 'Tạo mới' : 'Cập nhật'}</td>
                          <td>{g.channelsAdded ?? 0}</td>
                          <td>{g.channelsRemoved ?? 0}</td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              )}
            </div>

            {errors.length > 0 ? (
              <div>
                <h3 style={{ margin: '0 0 8px', fontSize: 15 }}>
                  Dòng lỗi ({errors.length})
                </h3>
                <ul
                  data-testid="channel-group-import-errors"
                  style={{ margin: 0, paddingLeft: 18, fontSize: 14, lineHeight: 1.5 }}
                >
                  {errors.map((e) => (
                    <li key={`${e.line}-${e.reason}`}>
                      Dòng {e.line}: {e.reason}
                    </li>
                  ))}
                </ul>
              </div>
            ) : null}
          </>
        ) : null}

        {error ? (
          <p className="form-error" role="alert">{error}</p>
        ) : null}
      </div>
    </Modal>
  )
}
