import { Link } from 'react-router-dom'
import { num, delta, relativeTime, shortDateTime, excerpt } from '../utils/metricFormat'
import { EngagementChart, PostsChart, MixDonut, FollowerChart } from './Charts'
import './CustomerOverview.css'

function HeroCard({ label, value, unit, sub, deltaValue, deltaSuffix, tone = 'neutral', to }) {
  const d = delta(deltaValue)
  const body = (
    <>
      <span className="hero-label">{label}</span>
      <span className="hero-value">
        {value}
        {unit && <span className="hero-unit">{unit}</span>}
      </span>
      <span className="hero-foot">
        {d && (
          <span className={`hero-delta hero-delta--${d.tone}`}>
            <span aria-hidden="true">{d.icon}</span> {d.text}
            {deltaSuffix ? ` ${deltaSuffix}` : ''}
          </span>
        )}
        {sub && <span className="hero-sub">{sub}</span>}
      </span>
    </>
  )

  const className = `hero-card hero-card--${tone}${to ? ' hero-card--link' : ''}`
  return to ? <Link to={to} className={className}>{body}</Link> : <div className={className}>{body}</div>
}

/** Thanh so sánh tương tác giữa các page — đọc nhanh hơn con số khi có 15 dòng. */
function Bar({ value, max }) {
  const pct = max > 0 ? Math.round((value / max) * 100) : 0
  return (
    <span className="lb-bar" title={`${num(value)} tương tác`}>
      <span className="lb-bar-fill" style={{ width: `${Math.max(pct, value > 0 ? 3 : 0)}%` }} />
    </span>
  )
}

export default function CustomerOverview({ data, days, onChangeDays, onSync, syncing }) {
  const {
    followers, engagement, posts, todo, news, pages = [], topPosts = [], upcoming = [],
    sync, unavailable = [], series = [], followerSeries = [], bucketDays = 1,
  } = data
  const maxEngagement = pages.reduce((m, p) => Math.max(m, p.engagement || 0), 0)
  const needAction = (todo?.waitingReview ?? 0) + (todo?.newsPending ?? 0)
  const followersPartial = followers.measured < followers.totalPages

  return (
    <div className="cust-dash">
      <div className="cust-toolbar">
        <div className="cust-range" role="group" aria-label="Khoảng thời gian">
          {[7, 30, 90].map((d) => (
            <button
              key={d}
              type="button"
              className={`cust-range-btn${days === d ? ' is-active' : ''}`}
              onClick={() => onChangeDays(d)}
            >
              {d} ngày
            </button>
          ))}
        </div>
        <div className="cust-toolbar-right">
          <span className="cust-synced">
            Số liệu cập nhật {relativeTime(sync?.lastAt)}
          </span>
          <button type="button" className="btn btn-secondary btn-sm" onClick={onSync} disabled={syncing}>
            {syncing ? 'Đang lấy số...' : 'Lấy số mới'}
          </button>
        </div>
      </div>

      {sync?.failedPages > 0 && (
        <p className="cust-alert cust-alert--warn">
          <strong>{sync.failedPages} page</strong> chưa lấy được số ở lần đồng bộ gần nhất, nên số của
          những page đó vẫn là số của lần trước. Phần lớn là mạng chập nhất thời và lượt sau sẽ tự
          khỏi. Nếu vài lượt liên tiếp vẫn báo, kiểm tra kết nối page ở mục{' '}
          <Link to="/platforms">Nền tảng</Link>.
        </p>
      )}

      <div className="cust-hero">
        <HeroCard
          label="Người theo dõi"
          value={num(followers.total)}
          deltaValue={followers.delta}
          deltaSuffix={`trong ${days} ngày`}
          sub={followersPartial
            ? `mới đo được ${followers.measured}/${followers.totalPages} page`
            : `cộng từ ${followers.totalPages} page`}
          tone="primary"
        />
        <HeroCard
          label="Tương tác nhận được"
          value={num(engagement.total)}
          sub={`${num(engagement.likes)} thích · ${num(engagement.comments)} bình luận · ${num(engagement.shares)} chia sẻ`}
          tone="accent"
        />
        <HeroCard
          label="Bài đã đăng"
          value={num(posts.published)}
          sub={`trong ${days} ngày qua`}
          tone="neutral"
        />
        <HeroCard
          label="Cần bạn xử lý"
          value={num(needAction)}
          sub={needAction > 0
            ? `${todo.newsPending} tin chờ duyệt · ${todo.waitingReview} bài chờ duyệt`
            : 'không còn việc tồn'}
          tone={needAction > 0 ? 'action' : 'ok'}
          to={needAction > 0 ? '/crawl' : undefined}
        />
      </div>

      <section className="card cust-panel">
        <header className="cust-panel-head">
          <h2>Tương tác theo {bucketDays === 1 ? 'ngày' : 'tuần'}</h2>
          <p>Tính theo ngày đăng bài — trả lời "bài hôm đó chạy tốt không"</p>
        </header>
        <EngagementChart series={series} bucketDays={bucketDays} />
        <MixDonut engagement={engagement} />
      </section>

      <div className="cust-split">
        <section className="card cust-panel">
          <header className="cust-panel-head">
            <h2>Bài đăng theo {bucketDays === 1 ? 'ngày' : 'tuần'}</h2>
            <p>Nhịp đăng có đều không</p>
          </header>
          <div className="chart--short">
            <PostsChart series={series} bucketDays={bucketDays} />
          </div>
        </section>

        <section className="card cust-panel">
          <header className="cust-panel-head">
            <h2>Người theo dõi</h2>
            <p>Cộng {followers.totalPages} page</p>
          </header>
          <div className="chart--short">
            <FollowerChart points={followerSeries} />
          </div>
        </section>
      </div>

      <section className="card cust-panel">
        <header className="cust-panel-head">
          <h2>Từng page chạy thế nào</h2>
          <p>Sắp theo tương tác nhận được trong {days} ngày</p>
        </header>

        {pages.length === 0 ? (
          <p className="cust-empty">Chưa có page nào đang bật.</p>
        ) : (
          <div className="lb-scroll">
            <table className="lb-table">
              <thead>
                <tr>
                  <th>Page</th>
                  <th className="lb-num">Người theo dõi</th>
                  <th className="lb-num">Bài</th>
                  <th className="lb-num">Tương tác</th>
                  <th className="lb-num" title="Tương tác trung bình mỗi bài — so sánh được giữa page to và page nhỏ">
                    TB / bài
                  </th>
                  <th className="lb-barcol" aria-label="So sánh" />
                </tr>
              </thead>
              <tbody>
                {pages.map((p) => {
                  const d = delta(p.followersDelta)
                  return (
                    <tr key={p.id} className={p.syncError ? 'is-stale' : undefined}>
                      <td>
                        <span className="lb-name">{p.name}</span>
                        {p.syncError && (
                          <span className="lb-warn" title={p.syncError}>số cũ</span>
                        )}
                      </td>
                      <td className="lb-num">
                        {num(p.followers)}
                        {d && d.tone !== 'flat' && (
                          <span className={`lb-delta lb-delta--${d.tone}`}>{d.icon} {d.text}</span>
                        )}
                      </td>
                      <td className="lb-num">{num(p.posts)}</td>
                      <td className="lb-num lb-strong">{num(p.engagement)}</td>
                      <td className="lb-num">{p.engagementPerPost ?? '—'}</td>
                      <td className="lb-barcol"><Bar value={p.engagement} max={maxEngagement} /></td>
                    </tr>
                  )
                })}
              </tbody>
            </table>
          </div>
        )}
      </section>

      <div className="cust-split">
        <section className="card cust-panel">
          <header className="cust-panel-head">
            <h2>Bài được tương tác nhiều nhất</h2>
            <p>Trong {days} ngày qua</p>
          </header>
          {topPosts.length === 0 ? (
            <p className="cust-empty">Chưa có bài nào nhận được tương tác trong khoảng này.</p>
          ) : (
            <ol className="tp-list">
              {topPosts.map((p) => (
                <li key={p.id} className="tp-item">
                  <div className="tp-main">
                    {p.url ? (
                      <a href={p.url} target="_blank" rel="noreferrer" className="tp-text">
                        {excerpt(p.message)}
                      </a>
                    ) : (
                      <span className="tp-text">{excerpt(p.message)}</span>
                    )}
                    <span className="tp-meta">{p.pageName} · {shortDateTime(p.postedAt)}</span>
                  </div>
                  <div className="tp-stats">
                    <span className="tp-total">{num(p.engagement)}</span>
                    <span className="tp-break">{p.likes} thích · {p.comments} b.luận · {p.shares} chia sẻ</span>
                  </div>
                </li>
              ))}
            </ol>
          )}
        </section>

        <div className="cust-stack">
          <section className="card cust-panel">
            <header className="cust-panel-head">
              <h2>Sắp đăng</h2>
              <p>{upcoming.length > 0 ? `${upcoming.length} bài đã hẹn giờ` : 'Chưa có bài nào chờ đăng'}</p>
            </header>
            {upcoming.length === 0 ? (
              <p className="cust-empty">
                <Link to="/posts">Tạo bài mới →</Link>
              </p>
            ) : (
              <ul className="up-list">
                {upcoming.map((u) => (
                  <li key={u.id} className="up-item">
                    <span className="up-time">{shortDateTime(u.scheduledPublishAt)}</span>
                    <span className="up-title">{excerpt(u.title, 60)}</span>
                    <span className="up-page">{u.pageName || '—'}</span>
                  </li>
                ))}
              </ul>
            )}
          </section>

          <section className="card cust-panel">
            <header className="cust-panel-head">
              <h2>Trang tin</h2>
              <p>Bài viết tự động lên website</p>
            </header>
            <div className="news-figs">
              <div>
                <span className="news-num">{num(news.published)}</span>
                <span className="news-lbl">bài đang trên web</span>
              </div>
              <div>
                <span className="news-num">{num(news.inWindow)}</span>
                <span className="news-lbl">đăng trong {days} ngày</span>
              </div>
            </div>
            <Link to="/news-site" className="news-link">Mở trang tin →</Link>
          </section>
        </div>
      </div>

      {unavailable.length > 0 && (
        <p className="cust-note">
          <strong>Chưa hiển thị được:</strong>{' '}
          {unavailable.map((u) => u.metric).join(', ')} — {unavailable[0].reason}.
        </p>
      )}
    </div>
  )
}
