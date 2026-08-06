import StatusBadge from '@/shared/components/StatusBadge'
import { formatDateTime } from '@/shared/utils/apiHelpers'
import {
  CRAWLED_ARTICLE_STATUS,
  DEDUP_METHOD_LABEL,
  DUPLICATE_TARGET_LABEL,
  getArticleStatusMeta,
} from '../constants/crawlConstants'

export default function CrawlArticleCard({
  article,
  canApprove,
  onApprove,
  onReject,
  onNotDuplicate,
  onRededup,
  busy,
}) {
  const meta = getArticleStatusMeta(article.status)
  const isDuplicate = Number(article.status) === CRAWLED_ARTICLE_STATUS.DUPLICATE
  const isPending = Number(article.status) === CRAWLED_ARTICLE_STATUS.PENDING

  return (
    <article className="crawl-card card">
      <div className="crawl-card-head">
        {article.thumbnailUrl && (
          // CDN báo hay chặn hotlink theo Referer — ảnh vỡ là bình thường, đừng để vỡ layout.
          <img
            className="crawl-thumb"
            src={article.thumbnailUrl}
            alt=""
            referrerPolicy="no-referrer"
            loading="lazy"
            onError={(e) => { e.currentTarget.style.display = 'none' }}
          />
        )}
        <div className="crawl-card-title">
          <div className="crawl-badges">
            <StatusBadge label={meta.label} tone={meta.tone} />
            {article.sourceName && <span className="crawl-source">{article.sourceName}</span>}
            {article.publishedAt && (
              <span className="crawl-date">{formatDateTime(article.publishedAt)}</span>
            )}
          </div>
          <h3>
            <a href={article.sourceUrl} target="_blank" rel="noreferrer noopener">
              {article.title}
            </a>
          </h3>
          {article.summary && <p className="crawl-summary">{article.summary}</p>}
        </div>
      </div>

      {article.qualityScore != null && (
        <div className="crawl-note crawl-note-muted">
          <strong>{article.qualityScore} điểm</strong>
          {article.screenTopic && ` · ${article.screenTopic}`}
          {article.screenSummary && <div>{article.screenSummary}</div>}
          {article.screenReason && <div><em>{article.screenReason}</em></div>}
        </div>
      )}

      {isDuplicate && (
        <div className="crawl-note crawl-note-muted">
          <strong>{DEDUP_METHOD_LABEL[article.duplicateMethod] ?? 'Trùng'}</strong>
          {article.duplicateScore != null && ` · điểm ${article.duplicateScore.toFixed(2)}`}
          {DUPLICATE_TARGET_LABEL[article.duplicateTarget]
            && ` · so với ${DUPLICATE_TARGET_LABEL[article.duplicateTarget]}`}
          {article.duplicateReason && <div>{article.duplicateReason}</div>}
        </div>
      )}

      {article.rejectReason && !isDuplicate && (
        <div className="crawl-note crawl-note-muted">{article.rejectReason}</div>
      )}

      {article.resultBatchId && (
        <div className="crawl-note crawl-note-success">
          Đã tạo {article.resultPostCount} bài ·{' '}
          <a href={`/bulk/${article.resultBatchId}`}>xem tiến độ batch</a>
        </div>
      )}

      <div className="crawl-actions">
        {canApprove && (isPending || isDuplicate) && (
          <button type="button" className="btn btn-primary" disabled={busy} onClick={() => onApprove(article)}>
            Duyệt & tạo bài
          </button>
        )}
        {canApprove && isDuplicate && (
          <button type="button" className="btn btn-ghost" disabled={busy} onClick={() => onNotDuplicate(article)}>
            Không trùng
          </button>
        )}
        {canApprove && isPending && (
          <button type="button" className="btn btn-ghost" disabled={busy} onClick={() => onReject(article)}>
            Loại
          </button>
        )}
        <button type="button" className="btn btn-ghost" disabled={busy} onClick={() => onRededup(article)}>
          Chấm lại trùng
        </button>
      </div>
    </article>
  )
}
