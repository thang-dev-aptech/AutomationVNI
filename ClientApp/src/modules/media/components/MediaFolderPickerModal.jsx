import { useEffect, useState } from 'react'
import Modal from '@/shared/components/Modal'
import { MEDIA_POST_SELECTION_LIMIT } from '../constants.js'
import { useMediaAssets } from '../hooks/useMediaAssets'
import { useLocalMediaBrowser } from '../hooks/useLocalMediaBrowser'
import { useMediaSelection } from '../hooks/useMediaSelection'
import MediaBrowserGrid from './MediaBrowserGrid'
// Breadcrumb và ô tìm kiếm dùng đúng class của màn Media (media-folder-breadcrumb, media-folder-search).
import '../pages/MediaPage.css'
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
          <span className="post-media-picker-count">Đã chọn {selection.count} ảnh</span>
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
      <div className="post-media-picker">
        <div className="media-folder-search">
          <input
            type="search"
            value={keyword}
            placeholder="Tìm ảnh theo tên..."
            aria-label="Tìm ảnh"
            onChange={(event) => setKeyword(event.target.value)}
          />
        </div>

        <MediaBrowserGrid
          folders={browser.items}
          files={files}
          isLoading={browser.isLoading || fileQuery.isLoading}
          isError={browser.isError || fileQuery.isError}
          error={browser.error ?? fileQuery.error}
          onRetry={() => {
            if (browser.isError) browser.refetch?.()
            if (fileQuery.isError) fileQuery.refetch?.()
          }}
          isRootLevel={!inFolder}
          onFolderClick={browser.openFolder}
          selectable
          isFileSelected={(asset) => selection.isSelected(asset.id)}
          onFileSelect={selection.toggle}
          folderBreadcrumb={(
            <nav className="media-folder-breadcrumb" aria-label="Đường dẫn thư mục">
              <button
                type="button"
                className={`media-folder-breadcrumb-item${!inFolder ? ' is-current' : ''}`}
                onClick={browser.openRoot}
              >
                Thư mục gốc
              </button>
              {browser.ancestors.map((ancestor, index) => (
                <span key={ancestor.id}>
                  <span className="media-folder-breadcrumb-sep">/</span>
                  <button
                    type="button"
                    className={`media-folder-breadcrumb-item${index === browser.ancestors.length - 1 ? ' is-current' : ''}`}
                    onClick={() => browser.openBreadcrumb(ancestor)}
                  >
                    {ancestor.name}
                  </button>
                </span>
              ))}
            </nav>
          )}
        />
      </div>
    </Modal>
  )
}
