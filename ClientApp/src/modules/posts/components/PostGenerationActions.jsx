import { usePermissions } from '@/shared/hooks/usePermissions'
import { getErrorMessage } from '@/shared/utils/apiHelpers'
import { toast } from '@/shared/stores/toastStore'
import { getAvailableGenerationActions } from '../constants/postStatus'
import { useRegenerateImage, useRegenerateText } from '../hooks/usePosts'

/**
 * Preview sau create-and-generate / bulk: chỉ giữ «Tạo lại nội dung / ảnh».
 * Bước Sinh Text / Sinh Ảnh thủ công (cũ) đã bỏ — AI chạy lúc tạo bài.
 */
export default function PostGenerationActions({ post }) {
  const { canEditPost } = usePermissions()
  const regenText = useRegenerateText()
  const regenImage = useRegenerateImage()

  if (!canEditPost(post.userId)) return null

  const hasContent = Boolean(post.content && post.content.trim())
  const a = getAvailableGenerationActions(post.status, hasContent)
  const isBusy = regenText.isPending || regenImage.isPending

  if (!a.regenText && !a.regenImage) return null

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
        Chỉnh sửa bằng AI
      </h2>
      <p style={{ margin: '0 0 12px', fontSize: '0.85rem', color: 'var(--color-text-muted)' }}>
        Chưa ưng? Bấm để AI tạo lại nội dung hoặc ảnh (giống lúc tạo bài).
      </p>
      <div style={{ display: 'flex', flexWrap: 'wrap', gap: 8 }}>
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
