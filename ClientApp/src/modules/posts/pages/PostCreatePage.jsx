import { useState } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import PageHeader from '@/shared/components/PageHeader'
import LoadingState from '@/shared/components/LoadingState'
import ErrorState from '@/shared/components/ErrorState'
import EmptyState from '@/shared/components/EmptyState'
import { getErrorMessage } from '@/shared/utils/apiHelpers'
import { toast } from '@/shared/stores/toastStore'
import { useCategoryList } from '@/modules/categories/hooks/useCategories'
import { useSocialChannelAll } from '@/modules/social-channels/hooks/useSocialChannels'
import { usePromptTemplateList } from '@/modules/prompt-templates/hooks/usePromptTemplates'
import { TEMPLATE_TYPE } from '@/modules/prompt-templates/constants/promptTemplateType'
import PostCreateForm from '../components/PostCreateForm'
import { useCreateAndGeneratePost } from '../hooks/usePosts'

export default function PostCreatePage() {
  const navigate = useNavigate()
  const createMutation = useCreateAndGeneratePost()
  const { data: categoryData, isLoading: categoriesLoading } = useCategoryList({
    index: 1,
    size: 100,
  })
  const {
    data: channels = [],
    isLoading: channelsLoading,
    isError: channelsError,
    error: channelsErrorObj,
    refetch: refetchChannels,
  } = useSocialChannelAll()
  const { data: textTplData } = usePromptTemplateList({
    templateType: TEMPLATE_TYPE.TEXT,
    isActive: true,
    index: 1,
    size: 100,
  })
  const { data: imageTplData } = usePromptTemplateList({
    templateType: TEMPLATE_TYPE.IMAGE,
    isActive: true,
    index: 1,
    size: 100,
  })
  const [errorMessage, setErrorMessage] = useState('')

  const categories = categoryData?.items ?? []
  const textTemplates = textTplData?.items ?? []
  const imageTemplates = imageTplData?.items ?? []
  const isLoading = channelsLoading || categoriesLoading

  const handleSubmit = async (payload) => {
    setErrorMessage('')
    try {
      const created = await createMutation.mutateAsync(payload)
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
        description="Nhập ý tưởng + mục tiêu → AI tự sinh text & ảnh, rồi mở bản preview để bạn xem / tạo lại / đăng"
        actions={(
          <Link to="/posts" className="btn btn-secondary">
            Quay lại
          </Link>
        )}
      />

      <div className="card card-body" style={{ maxWidth: 720 }}>
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
            channels={channels}
            categories={categories}
            textTemplates={textTemplates}
            imageTemplates={imageTemplates}
            isSubmitting={createMutation.isPending}
            errorMessage={errorMessage}
            onSubmit={handleSubmit}
          />
        )}
      </div>
    </section>
  )
}
