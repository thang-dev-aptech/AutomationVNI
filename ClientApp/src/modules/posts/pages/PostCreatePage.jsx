import { useState } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import PageHeader from '@/shared/components/PageHeader'
import LoadingState from '@/shared/components/LoadingState'
import ErrorState from '@/shared/components/ErrorState'
import EmptyState from '@/shared/components/EmptyState'
import { getErrorMessage } from '@/shared/utils/apiHelpers'
import { useCategoryList } from '@/modules/categories/hooks/useCategories'
import { useSocialChannelAll } from '@/modules/social-channels/hooks/useSocialChannels'
import PostCreateForm from '../components/PostCreateForm'
import { useCreatePost } from '../hooks/usePosts'

export default function PostCreatePage() {
  const navigate = useNavigate()
  const createMutation = useCreatePost()
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
  const [errorMessage, setErrorMessage] = useState('')

  const categories = categoryData?.items ?? []
  const isLoading = channelsLoading || categoriesLoading

  const handleSubmit = async (payload) => {
    setErrorMessage('')
    try {
      const created = await createMutation.mutateAsync(payload)
      navigate(created?.id ? `/posts/${created.id}` : '/posts')
    } catch (error) {
      setErrorMessage(getErrorMessage(error))
    }
  }

  return (
    <section>
      <PageHeader
        title="Tạo bài viết"
        description="Nhập input để hệ thống sinh nội dung và media tự động"
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
            isSubmitting={createMutation.isPending}
            errorMessage={errorMessage}
            onSubmit={handleSubmit}
          />
        )}
      </div>
    </section>
  )
}
