import StatusBadge from '@/shared/components/StatusBadge'
import { formatDateTime } from '@/shared/utils/apiHelpers'
import {
  CRAWLED_ARTICLE_STATUS,
  DEDUP_METHOD_LABEL,
  DUPLICATE_TARGET_LABEL,
  getArticleStatusMeta,
} from '../constants/crawlConstants'

/// Ngưỡng khớp với MinQualityScore=60 ở backend: dưới mức đó tin đã bị lọc, nên tin hiện
/// trên trang gần như luôn từ 60 trở lên — màu chỉ để phân biệt "chắc chắn đăng" với "tạm được".
function scoreClass(score) {
  if (score >= 85) return 'crawl-score crawl-score-high'
  if (score >= 70) return 'crawl-score crawl-score-mid'
  return 'crawl-score crawl-score-low'
}

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
        // Gập lại: điểm và chủ đề đủ để lướt cả trang, phần tóm tắt/lý do chỉ mở khi cần soi
        // một tin cụ thể. Trải hết ra thì mỗi thẻ cao thêm 4-5 dòng, xem 20 tin là cuộn mỏi.
        <details className="crawl-screen">
          <summary>
            <span className={scoreClass(article.qualityScore)}>{article.qualityScore} điểm</span>
            {article.screenTopic && <span className="crawl-screen-topic">{article.screenTopic}</span>}
          </summary>
          {article.screenSummary && <p>{article.screenSummary}</p>}
          {article.screenReason && <p className="crawl-screen-reason">{article.screenReason}</p>}
        </details>
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
