import { useMemo, useState } from 'react'
import PageHeader from '@/shared/components/PageHeader'
import LoadingState from '@/shared/components/LoadingState'
import ErrorState from '@/shared/components/ErrorState'
import EmptyState from '@/shared/components/EmptyState'
import { usePermissions } from '@/shared/hooks/usePermissions'
import { toast } from '@/shared/stores/toastStore'
import { getErrorMessage } from '@/shared/utils/apiHelpers'
import FanpageModal from '../components/FanpageModal'
import { useNewsArticles, useNewsSiteStatus, usePublishToFanpage, useRebuildSite, useUnpublish } from '../hooks/useNewsSite'
import './NewsSitePage.css'

const CATEGORIES = [
  { key: '', label: 'Tất cả' },
  { key: 'giao-duc', label: 'Giáo dục' },
  { key: 'cong-nghe-ai', label: 'AI & Công nghệ' },
  { key: 'ky-nang', label: 'Kỹ năng' },
  { key: 'khac', label: 'Khác' },
]

const CATEGORY_LABEL = Object.fromEntries(CATEGORIES.map((c) => [c.key, c.label]))

function formatDate(value) {
  if (!value) return '—'
  const d = new Date(value)
  return Number.isNaN(d.getTime()) ? '—' : d.toLocaleString('vi-VN', { dateStyle: 'short', timeStyle: 'short' })
}

export default function NewsSitePage() {
  const { canApproveCrawl, canManageCrawlSources } = usePermissions()
  const [category, setCategory] = useState('')
  const [posting, setPosting] = useState(null)

  const params = useMemo(() => ({ category: category || undefined, size: 50 }), [category])
  const { data, isLoading, isError, error, refetch } = useNewsArticles(params)
  const { data: status } = useNewsSiteStatus()
  const publish = usePublishToFanpage()
  const rebuild = useRebuildSite()
  const unpublish = useUnpublish()

  const articles = useMemo(() => data?.items ?? data ?? [], [data])
  const published = articles.filter((a) => a.status === 'Published')
  const composing = articles.filter((a) => a.status === 'Composing')
  const failed = articles.filter((a) => a.status === 'Failed')

  const handlePublish = async ({ channelIds, autoPublish }) => {
    try {
      const result = await publish.mutateAsync({ id: posting.id, payload: { channelIds, autoPublish } })
      toast.success(`Đã tạo ${result.created} bài cho: ${result.channels.join(', ')}`)
      setPosting(null)
    } catch (e) { toast.error(getErrorMessage(e)) }
  }

  const handleUnpublish = async (a) => {
    if (!window.confirm(`Gỡ "${a.title}" khỏi web?\n\nLink cũ vẫn mở được, bài chỉ biến khỏi trang chủ.`)) return
    try {
      await unpublish.mutateAsync(a.id)
      toast.success('Đã gỡ khỏi web')
    } catch (e) { toast.error(getErrorMessage(e)) }
  }

  const handleRebuild = async () => {
    try {
      const r = await rebuild.mutateAsync()
      toast.success(`Đã dựng lại ${r.articles} bài, ${r.pages} trang`)
    } catch (e) { toast.error(getErrorMessage(e)) }
  }

  return (
    <div className="news-site-page">
      <PageHeader
        title="Bài đã lên web"
        description="Bước cuối: chọn page để đăng fanpage. Bình luận sẽ dẫn về link của mình, không phải link báo gốc."
        actions={canManageCrawlSources && (
          <button type="button" className="btn btn-ghost" onClick={handleRebuild} disabled={rebuild.isPending}>
            {rebuild.isPending ? 'Đang dựng…' : 'Dựng lại trang'}
          </button>
        )}
      />

      {/* Chưa gắn tên miền: nút "Xem trên web" mở bản XEM THỬ trên máy chủ nội bộ, còn link
          dán vào bình luận Facebook vẫn là tên miền thật. Nói rõ, nếu không người duyệt xem
          thấy bài chạy ngon rồi tưởng độc giả cũng mở được — trong khi tên miền chưa sống. */}
      {status?.isPreviewOnly && (
        <div className="news-site-preview-note">
          Đang <b>xem thử tại máy</b> ({status.previewBaseUrl}). Tên miền thật
          {' '}<b>{status.publicBaseUrl || 'chưa đặt'}</b> chưa trỏ về máy chủ, nên link trong bài
          Facebook sẽ chưa mở được cho tới khi gắn xong.
        </div>
      )}

      {/* Thư mục không ghi được thì bài "lên web" vẫn báo thành công nhưng trang ngoài KHÔNG
          đổi — không lỗi nào hiện ra. Cảnh báo ngay đầu trang, đừng để phải đi tìm. */}
      {status && status.canWrite === false && (
        <div className="news-site-warn">
          Không ghi được thư mục xuất bản: <code>{status.path}</code> — bài sẽ không lên trang ngoài.
        </div>
      )}

      <div className="news-site-tabs">
        {CATEGORIES.map((c) => (
          <button
            key={c.key}
            type="button"
            className={`news-site-tab${category === c.key ? ' is-active' : ''}`}
            onClick={() => setCategory(c.key)}
          >
            {c.label}
          </button>
        ))}
      </div>

      {/* AI viết ở nền ~40 giây. Không hiện khối này thì người duyệt bấm Duyệt xong nhìn
          danh sách thấy y như cũ, và tưởng vừa bấm hụt. */}
      {composing.length > 0 && (
        <div className="news-site-composing">
          <span className="news-site-spin" aria-hidden />
          AI đang viết {composing.length} bài — khoảng 40 giây mỗi bài, danh sách tự cập nhật.
          <span className="news-site-composing-list">
            {composing.map((a) => a.title).join(' · ')}
          </span>
        </div>
      )}

      {isLoading && <LoadingState />}
      {isError && <ErrorState message={getErrorMessage(error)} onRetry={refetch} />}

      {!isLoading && !isError && published.length === 0 && composing.length === 0 && (
        <EmptyState message="Chưa có bài nào. Sang mục Tin đã cào, bấm Duyệt để đưa tin lên website." />
      )}

      {published.length > 0 && (
        <div className="news-site-list">
          {published.map((a) => (
            <article key={a.id} className="news-site-card">
              <div className="news-site-card-main">
                <span className="news-site-kicker">
                  {CATEGORY_LABEL[a.categorySlug] ?? a.categorySlug}
                  {a.suggestFanpage && <b className="news-site-pick">nên đăng fanpage</b>}
                  {typeof a.qualityScore === 'number' && (
                    <em className="news-site-score">{a.qualityScore} điểm</em>
                  )}
                </span>
                <h3>{a.title}</h3>
                <p className="news-site-sapo">{a.sapo}</p>
                <p className="news-site-meta">
                  {formatDate(a.publishedAt)} · {a.readMinutes} phút đọc
                  {a.sourceName ? ` · nguồn ${a.sourceName}` : ''}
                  {a.viewCount ? ` · ${a.viewCount} lượt xem` : ''}
                </p>
              </div>
              <div className="news-site-card-actions">
                {a.url
                  ? <a className="btn btn-ghost" href={a.url} target="_blank" rel="noreferrer">Xem trên web</a>
                  : <span className="news-site-nourl" title="Bài chưa có file HTML">chưa có link</span>}
                {canApproveCrawl && (
                  <button type="button" className="btn btn-primary" onClick={() => setPosting(a)}>
                    Đăng fanpage
                  </button>
                )}
                {canManageCrawlSources && (
                  <button
                    type="button"
                    className="btn btn-ghost news-site-remove"
                    onClick={() => handleUnpublish(a)}
                    disabled={unpublish.isPending}
                  >
                    Gỡ
                  </button>
                )}
              </div>
            </article>
          ))}
        </div>
      )}

      {/* Bài hỏng để riêng chứ không trộn vào danh sách: trộn vào thì người dùng bấm "Đăng
          fanpage" trên một bài chưa hề tồn tại trên web. */}
      {failed.length > 0 && (
        <>
          <h2 className="news-site-section">Chưa lên web được ({failed.length})</h2>
          <div className="news-site-list">
            {failed.map((a) => (
              <article key={a.id} className="news-site-card is-failed">
                <div className="news-site-card-main">
                  <h3>{a.title || '(chưa có tiêu đề)'}</h3>
                  <p className="news-site-error">{a.errorMessage ?? 'Chưa rõ lý do'}</p>
                </div>
              </article>
            ))}
          </div>
        </>
      )}

      <FanpageModal
        open={Boolean(posting)}
        article={posting}
        onClose={() => setPosting(null)}
        onSubmit={handlePublish}
        submitting={publish.isPending}
      />
    </div>
  )
}
