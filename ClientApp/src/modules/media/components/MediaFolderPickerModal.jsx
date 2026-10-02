import { useEffect, useState } from 'react'
import Modal from '@/shared/components/Modal'
import { getErrorMessage } from '@/shared/utils/apiHelpers'
import { isImageMime } from '../constants/mediaConstants'
import { MEDIA_POST_SELECTION_LIMIT } from '../constants.js'
import { useMediaAssets } from '../hooks/useMediaAssets'
import { useLocalMediaBrowser } from '../hooks/useLocalMediaBrowser'
import { useMediaSelection } from '../hooks/useMediaSelection'
import './MediaFolderPickerModal.css'

export default function MediaFolderPickerModal({
  open,
  onClose,
  onConfirm,
  initialSelected = [],
  limit = MEDIA_POST_SELECTION_LIMIT,
}) {
  const [keyword, setKeyword] = useState('')
  const selection = useMediaSelection({ limit, initial: initialSelected })
  const browser = useLocalMediaBrowser({ enabled: open })
  const trimmed = keyword.trim()
  const inFolder = browser.currentFolderId !== null

  useEffect(() => {
    if (!open) return
    selection.reset(initialSelected)
    setKeyword('')
    browser.openRoot()
    // Chỉ reset khi popup mở lại. reset/openRoot ổn định.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open])

  const fileQuery = useMediaAssets(
    {
      keyword: trimmed,
      index: 1,
      size: 48,
      folderId: trimmed || !inFolder ? undefined : browser.currentFolderId,
    },
    { enabled: open && (inFolder || Boolean(trimmed)) },
  )
  const files = fileQuery.data?.items ?? []

  const confirm = () => {
    if (selection.count === 0) return
    onConfirm?.(selection.items)
  }

  return (
    <Modal
      open={open}
      title="Chọn ảnh từ Media"
      onClose={onClose}
      footer={(
        <>
          <span className="media-folder-picker-count">Đã chọn {selection.count} ảnh</span>
          <button type="button" className="btn btn-ghost" onClick={onClose}>Huỷ</button>
          <button
            type="button"
            className="btn btn-primary"
            disabled={selection.count === 0}
            onClick={confirm}
          >
            Dùng ảnh đã chọn
          </button>
        </>
      )}
    >
      <div className="media-folder-picker">
        <label className="media-folder-picker-search">
          <span className="sr-only">Tìm ảnh</span>
          <input
            type="search"
            value={keyword}
            placeholder="Tìm ảnh theo tên..."
            aria-label="Tìm ảnh"
            onChange={(event) => setKeyword(event.target.value)}
          />
        </label>

        <nav className="media-folder-picker-breadcrumb" aria-label="Đường dẫn thư mục">
          <button type="button" onClick={browser.openRoot}>Tất cả</button>
          {browser.ancestors.map((ancestor) => (
            <button key={ancestor.id} type="button" onClick={() => browser.openBreadcrumb(ancestor)}>
              {ancestor.name}
            </button>
          ))}
        </nav>

        {browser.isLoading && <p>Đang tải thư mục...</p>}
        {browser.isError && <p>{getErrorMessage(browser.error, 'Không tải được thư mục')}</p>}
        <ul className="media-folder-picker-folders">
          {browser.items.map((folder) => (
            <li key={folder.id}>
              <button type="button" onClick={() => browser.openFolder(folder)}>
                📁 {folder.name}
                {folder.pageName ? ` · ${folder.pageName}` : ''}
              </button>
            </li>
          ))}
        </ul>

        {fileQuery.isLoading && <p>Đang tải ảnh...</p>}
        {fileQuery.isError && <p>{getErrorMessage(fileQuery.error, 'Không tải được ảnh')}</p>}
        <ul className="media-folder-picker-files">
          {files.map((asset) => {
            const image = isImageMime(asset.mimeType)
            const picked = selection.isSelected(asset.id)
            return (
              <li key={asset.id}>
                <button
                  type="button"
                  className={picked ? 'is-selected' : undefined}
                  disabled={!image}
                  aria-pressed={image ? picked : undefined}
                  onClick={() => image && selection.toggle(asset)}
                >
                  <span>{asset.originalFileName || asset.fileName}</span>
                  {!image && <span>Không phải ảnh</span>}
                </button>
              </li>
            )
          })}
        </ul>
      </div>
    </Modal>
  )
}
