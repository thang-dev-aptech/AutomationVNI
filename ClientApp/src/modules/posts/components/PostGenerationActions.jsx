import { usePermissions } from '@/shared/hooks/usePermissions'
import { getErrorMessage } from '@/shared/utils/apiHelpers'
import { toast } from '@/shared/stores/toastStore'
import { getAvailableGenerationActions } from '../constants/postStatus'
import {
  useGenerateImage,
  useGenerateText,
  useRegenerateImage,
  useRegenerateText,
  useRenderOverlay,
} from '../hooks/usePosts'

/**
 * Kích hoạt / chỉnh sửa nội dung AI:
 * - Draft: Sinh Text → Sinh Ảnh (queue + process 1 click).
 * - Preview (Approved/Scheduled): Tạo lại nội dung / Tạo lại ảnh.
 */
export default function PostGenerationActions({ post }) {
  const { canManageJobs } = usePermissions()
  const genText = useGenerateText()
  const genImage = useGenerateImage()
  const renderOverlay = useRenderOverlay()
  const regenText = useRegenerateText()
  const regenImage = useRegenerateImage()

  if (!canManageJobs) return null

  const hasContent = Boolean(post.content && post.content.trim())
  const a = getAvailableGenerationActions(post.status, hasContent)
  const isBusy =
    genText.isPending ||
    genImage.isPending ||
    renderOverlay.isPending ||
    regenText.isPending ||
    regenImage.isPending

  const hasAny = a.genText || a.genImage || a.renderOverlay || a.regenText || a.regenImage
  if (!hasAny) return null

  const isPreview = a.regenText || a.regenImage

  const run = async (mutation, okMessage) => {
    try {
      await mutation.mutateAsync(post.id)
      toast.success(okMessage)
    } catch (error) {
      toast.error(getErrorMessage(error))
    }
  }

  return (
    <div className="card card-body" style={{ marginBottom: 16 }}>
      <h2 style={{ margin: '0 0 4px', fontSize: '1.05rem' }}>
        {isPreview ? 'Chỉnh sửa bằng AI' : 'Sinh nội dung (AI)'}
      </h2>
      <p style={{ margin: '0 0 12px', fontSize: '0.85rem', color: 'var(--color-text-muted)' }}>
        {isPreview
          ? 'Chưa ưng? Bấm để AI tạo lại nội dung hoặc ảnh. Chưa cấu hình AI key thì dùng bản mock.'
          : 'Mỗi nút queue job và xử lý ngay. Chưa cấu hình AI key thì dùng bản mock.'}
      </p>
      <div style={{ display: 'flex', flexWrap: 'wrap', gap: 8 }}>
        {a.genText && (
          <button
            type="button"
            className="btn btn-primary"
            disabled={isBusy}
            onClick={() => run(genText, 'Đã sinh text')}
          >
            {genText.isPending ? 'Đang sinh text...' : '1 · Sinh Text (AI)'}
          </button>
        )}
        {a.genImage && (
          <button
            type="button"
            className="btn btn-primary"
            disabled={isBusy}
            onClick={() => run(genImage, 'Đã sinh ảnh')}
          >
            {genImage.isPending ? 'Đang sinh ảnh...' : '2 · Sinh Ảnh (AI)'}
          </button>
        )}
        {a.renderOverlay && (
          <button
            type="button"
            className="btn btn-secondary"
            disabled={isBusy}
            onClick={() => run(renderOverlay, 'Đã render overlay')}
          >
            {renderOverlay.isPending ? 'Đang render...' : '3 · Render Overlay (logo/CTA)'}
          </button>
        )}
        {a.regenText && (
          <button
            type="button"
            className="btn btn-secondary"
            disabled={isBusy}
            onClick={() => run(regenText, 'Đã tạo lại nội dung')}
          >
            {regenText.isPending ? 'Đang tạo lại nội dung...' : '🔄 Tạo lại nội dung'}
          </button>
        )}
        {a.regenImage && (
          <button
            type="button"
            className="btn btn-secondary"
            disabled={isBusy}
            onClick={() => run(regenImage, 'Đã tạo lại ảnh')}
          >
            {regenImage.isPending ? 'Đang tạo lại ảnh...' : '🔄 Tạo lại ảnh'}
          </button>
        )}
      </div>
    </div>
  )
}
