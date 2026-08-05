import { useMemo, useState } from 'react'
import PageHeader from '@/shared/components/PageHeader'
import LoadingState from '@/shared/components/LoadingState'
import ErrorState from '@/shared/components/ErrorState'
import EmptyState from '@/shared/components/EmptyState'
import { usePermissions } from '@/shared/hooks/usePermissions'
import { toast } from '@/shared/stores/toastStore'
import { getErrorMessage } from '@/shared/utils/apiHelpers'
import CrawlArticleCard from '../components/CrawlArticleCard'
import ApproveArticleModal from '../components/ApproveArticleModal'
import CrawlSourceModal from '../components/CrawlSourceModal'
import { STATUS_TABS } from '../constants/crawlConstants'
import {
  useApproveArticle,
  useCrawlSummary,
  useCrawledArticles,
  useMarkNotDuplicate,
  useRededupArticle,
  useRejectArticle,
} from '../hooks/useCrawl'
import './CrawlInboxPage.css'

export default function CrawlInboxPage() {
  const { canApproveCrawl, canManageCrawlSources } = usePermissions()
  const [tab, setTab] = useState('pending')
  const [page, setPage] = useState(1)
  const [approving, setApproving] = useState(null)
  const [sourcesOpen, setSourcesOpen] = useState(false)

  const activeTab = STATUS_TABS.find((t) => t.key === tab) ?? STATUS_TABS[0]
  const params = useMemo(
    () => ({ status: activeTab.status, index: page, size: 20 }),
    [activeTab.status, page],
  )

  const { data, isLoading, isError, error, refetch } = useCrawledArticles(params)
  const { data: summary } = useCrawlSummary()
  const approve = useApproveArticle()
  const reject = useRejectArticle()
  const notDuplicate = useMarkNotDuplicate()
  const rededup = useRededupArticle()

  const busy = approve.isPending || reject.isPending || notDuplicate.isPending || rededup.isPending

  const handleApprove = async (payload) => {
    try {
      const result = await approve.mutateAsync({ id: approving.id, payload })
      toast.success(result.message)
      setApproving(null)
    } catch (e) { toast.error(getErrorMessage(e)) }
  }

  const handleReject = async (article) => {
    const reason = window.prompt('Lý do loại tin này? (có thể bỏ trống)')
    if (reason === null) return
    try {
      await reject.mutateAsync({ id: article.id, reason })
      toast.success('Đã loại tin')
    } catch (e) { toast.error(getErrorMessage(e)) }
  }

  const handleNotDuplicate = async (article) => {
    try {
      await notDuplicate.mutateAsync(article.id)
      toast.success('Đã bỏ đánh dấu trùng')
    } catch (e) { toast.error(getErrorMessage(e)) }
  }

  const handleRededup = async (article) => {
    try {
      await rededup.mutateAsync(article.id)
      toast.success('Đã chấm lại')
    } catch (e) { toast.error(getErrorMessage(e)) }
  }

  const items = data?.items ?? []
  const totalPages = Math.max(1, Math.ceil((data?.total ?? 0) / 20))

  return (
    <div className="crawl-page">
      <PageHeader
        title="Tin đã cào"
        description="Tin từ báo giáo dục, AI đã xào bản nháp và chấm trùng. Duyệt một tin sẽ tạo bài cho từng page."
        actions={(
          <button type="button" className="btn btn-ghost" onClick={() => setSourcesOpen(true)}>
            Nguồn cào {summary?.totalActiveSources ? `(${summary.totalActiveSources})` : ''}
          </button>
        )}
      />

      <div className="crawl-tabs">
        {STATUS_TABS.map((t) => {
          const count = summary?.byStatus?.[
            { pending: 'Pending', duplicate: 'Duplicate', approved: 'Approved',
              filtered: 'Filtered', rejected: 'Rejected' }[t.key]
          ]
          return (
            <button
              key={t.key}
              type="button"
              className={`crawl-tab ${tab === t.key ? 'is-active' : ''}`}
              onClick={() => { setTab(t.key); setPage(1) }}
            >
              {t.label}{count != null && <span className="crawl-tab-count">{count}</span>}
            </button>
          )
        })}
      </div>

      {isLoading && <LoadingState />}
      {isError && <ErrorState message={getErrorMessage(error)} onRetry={refetch} />}
      {!isLoading && !isError && items.length === 0 && (
        <EmptyState
          title="Chưa có tin nào"
          description={tab === 'pending'
            ? 'Thêm nguồn RSS rồi bấm "Cào ngay" để lấy tin về.'
            : 'Không có tin ở trạng thái này.'}
        />
      )}

      <div className="crawl-list">
        {items.map((article) => (
          <CrawlArticleCard
            key={article.id}
            article={article}
            canApprove={canApproveCrawl}
            busy={busy}
            onApprove={setApproving}
            onReject={handleReject}
            onNotDuplicate={handleNotDuplicate}
            onRededup={handleRededup}
          />
        ))}
      </div>

      {totalPages > 1 && (
        <div className="crawl-pager">
          <button type="button" className="btn btn-ghost"
            disabled={page <= 1} onClick={() => setPage((p) => p - 1)}>Trước</button>
          <span>Trang {page}/{totalPages}</span>
          <button type="button" className="btn btn-ghost"
            disabled={page >= totalPages} onClick={() => setPage((p) => p + 1)}>Sau</button>
        </div>
      )}

      <ApproveArticleModal
        open={Boolean(approving)}
        article={approving}
        onClose={() => setApproving(null)}
        onSubmit={handleApprove}
        submitting={approve.isPending}
      />
      <CrawlSourceModal
        open={sourcesOpen}
        onClose={() => setSourcesOpen(false)}
        canManage={canManageCrawlSources}
      />
    </div>
  )
}
