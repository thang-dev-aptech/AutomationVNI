import { useState } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import PageHeader from '@/shared/components/PageHeader'
import LoadingState from '@/shared/components/LoadingState'
import ErrorState from '@/shared/components/ErrorState'
import EmptyState from '@/shared/components/EmptyState'
import Modal from '@/shared/components/Modal'
import { getErrorMessage } from '@/shared/utils/apiHelpers'
import { toast } from '@/shared/stores/toastStore'
import { useSocialChannelAll } from '@/modules/social-channels/hooks/useSocialChannels'
import { usePromptTemplateList } from '@/modules/prompt-templates/hooks/usePromptTemplates'
import { usePageContextList } from '@/modules/page-contexts/hooks/usePageContexts'
import { useCategoryList } from '@/modules/categories/hooks/useCategories'
import PostCreateForm from '../components/PostCreateForm'
import GenerationFlowPicker from '../components/GenerationFlowPicker'
import { useCreateAndGeneratePost } from '../hooks/usePosts'

export default function PostCreatePage() {
  const navigate = useNavigate()
  const [flow, setFlow] = useState(null)
  const [pickerValue, setPickerValue] = useState('fullai')
  const createMutation = useCreateAndGeneratePost()
  const {
    data: channels = [],
    isLoading: channelsLoading,
    isError: channelsError,
    error: channelsErrorObj,
    refetch: refetchChannels,
  } = useSocialChannelAll()
  const {
    data: tplData,
    isLoading: templatesLoading,
  } = usePromptTemplateList({
    isActive: true,
    index: 1,
    size: 100,
  })
  const {
    data: pageContextData,
    isLoading: pageContextsLoading,
  } = usePageContextList({ index: 1, size: 200 })
  const {
    data: categoryData,
    isLoading: categoriesLoading,
  } = useCategoryList({ index: 1, size: 200 })
  const [errorMessage, setErrorMessage] = useState('')

  const categoryTemplates = tplData?.items ?? []
  const pageContexts = pageContextData?.items ?? []
  const categories = categoryData?.items ?? []
  const isLoading = channelsLoading || templatesLoading || pageContextsLoading || categoriesLoading

  const handleSubmit = async (payload) => {
    setErrorMessage('')
    try {
      const created = await createMutation.mutateAsync(payload)
      if (created?.batchId) {
        toast.success(`Đã tạo ${created.created ?? ''} bài — đang sinh nội dung nền`)
        navigate(`/bulk/${created.batchId}`)
        return
      }
      toast.success('AI đã sinh xong nội dung — kiểm tra bản preview')
      navigate(created?.id ? `/posts/${created.id}` : '/posts')
    } catch (error) {
      setErrorMessage(getErrorMessage(error))
    }
  }

  return (
    <section>
      <PageHeader
        title="Tạo bài viết"
        description="Chọn page (nhiều được) → ý tưởng. Page đã setup PageContext thì không cần chọn danh mục."
        actions={(
          <Link to="/posts" className="btn btn-secondary">
            Quay lại
          </Link>
        )}
      />

      <Modal
        open={flow === null}
        title="Chọn phương pháp tạo ảnh"
        onClose={() => navigate('/posts')}
        footer={(
          <button type="button" className="btn btn-primary" onClick={() => setFlow(pickerValue)}>
            Tiếp tục
          </button>
        )}
      >
        <GenerationFlowPicker value={pickerValue} onChange={setPickerValue} />
      </Modal>

      {flow !== null && (
        <div className="card card-body" style={{ maxWidth: 720 }}>
          <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: 12 }}>
            <span style={{ fontSize: '0.85rem', color: 'var(--text-muted, #888)' }}>
              Phương pháp: <strong>{flow === 'template' ? 'AI sinh text, ghép vào ảnh mẫu' : 'Sinh toàn bộ bằng AI'}</strong>
            </span>
            <button type="button" className="btn btn-ghost" onClick={() => setFlow(null)}>
              Đổi phương pháp
            </button>
          </div>

          {isLoading && <LoadingState message="Đang tải dữ liệu form..." />}
          {channelsError && (
            <ErrorState
              message={getErrorMessage(channelsErrorObj, 'Không thể tải danh sách kênh')}
              onRetry={refetchChannels}
            />
          )}
          {!isLoading && !channelsError && channels.length === 0 && (
            <EmptyState
              message="Chưa có kênh nào được kết nối. Hãy kết nối kênh trước khi tạo bài."
              action={(
                <Link to="/platforms" className="btn btn-primary">
                  Đến Platforms
                </Link>
              )}
            />
          )}
          {!isLoading && !channelsError && channels.length > 0 && (
            <PostCreateForm
              flow={flow}
              channels={channels}
              categoryTemplates={categoryTemplates}
              pageContexts={pageContexts}
              categories={categories}
              isSubmitting={createMutation.isPending}
              errorMessage={errorMessage}
              onSubmit={handleSubmit}
            />
          )}
        </div>
      )}
    </section>
  )
}
