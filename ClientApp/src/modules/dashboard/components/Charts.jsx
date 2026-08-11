import { useId, useState } from 'react'
import { num } from '../utils/metricFormat'
import './Charts.css'

/**
 * Biểu đồ vẽ bằng SVG thuần, không thêm thư viện.
 *
 * Dự án đang có 11 gói phụ thuộc và gói JS đã 600 KB kèm cảnh báo quá ngưỡng. Recharts thêm
 * khoảng 500 KB nữa cho ba biểu đồ cột — không đáng. SVG cột và đường là hình học đơn giản,
 * và tự viết thì màu bám thẳng vào biến CSS sẵn có của dự án.
 *
 * ═══ BA MÀU CHUỖI ĐÃ QUA KIỂM ═══
 *
 * #2d6cb6 xanh dương · #f08a22 cam · #10b981 xanh lá — chạy qua bộ kiểm bảng màu:
 *   dải sáng ĐẠT · sàn độ tươi ĐẠT · tách màu cho người mù màu ĐẠT (ΔE 10,4 protan)
 *   sàn thị lực thường ĐẠT (ΔE 24,7) · tương phản nền CẢNH BÁO (2,5:1 < 3:1)
 *
 * Cảnh báo tương phản bắt buộc phải bù bằng NHÃN CHỮ — nên chú giải luôn hiện kèm tên, tooltip
 * luôn đọc ra số, và bảng xếp hạng phía dưới là bản tra cứu không cần rê chuột. Không được bỏ
 * chú giải để lấy chỗ.
 *
 * MỘT ĐÁNH ĐỔI ĐÃ BIẾT: #10b981 chính là --color-success, vốn là màu TRẠNG THÁI của dự án.
 * Quy tắc chuẩn là không mượn màu trạng thái làm màu chuỗi dữ liệu. Nhưng bảng màu của dự án
 * chỉ có ba sắc độ đủ khác nhau (xanh dương, cam, xanh lá) — --color-warning quá gần cam, còn
 * --color-primary-strong quá gần xanh dương. Chọn giữ màu của hệ thiết kế thay vì bịa một sắc
 * mới, và bù bằng chỗ đặt: trong ba biểu đồ này không có ô nào mã hoá trạng thái, nên xanh lá
 * ở đây chỉ có một nghĩa duy nhất là "Chia sẻ", luôn kèm chữ.
 */
const SERIES = [
  { key: 'likes', label: 'Thích', color: '#2d6cb6' },
  { key: 'comments', label: 'Bình luận', color: '#f08a22' },
  { key: 'shares', label: 'Chia sẻ', color: '#10b981' },
]

const GAP = 2 // khe hở màu nền giữa hai khối chồng nhau và giữa hai cột cạnh nhau

// Bề rộng cột tối đa, tính theo % của viewBox.
//
// Quy cách yêu cầu cột không dày quá 24px. Nhưng SVG ở đây dùng preserveAspectRatio="none" nên
// đơn vị viewBox co giãn theo bề ngang khung — 2,1% ở khung 700px là 15px, ở khung 1400px thành
// 30px, vượt mốc. Lấy 2% để ở khung rộng nhất thực tế (~1200px) vẫn còn 24px.
const MAX_BAR_PCT = 2

// Thang chia mịn hơn [1, 2, 2.5, 5].
//
// Với đỉnh dữ liệu 28, thang thô nhảy thẳng lên 50 và cột cao nhất chỉ chiếm 56% chiều cao khung
// — nhìn như tương tác đang thấp trong khi thật ra đó là đỉnh. Thêm 3 và 4 để đỉnh trục bám sát
// dữ liệu.
function niceTop(value) {
  if (value <= 0) return 1
  const pow = 10 ** Math.floor(Math.log10(value))
  for (const step of [1, 1.5, 2, 2.5, 3, 4, 5, 6, 8, 10]) {
    const candidate = step * pow
    if (candidate >= value) return candidate
  }
  return 10 * pow
}

function labelOf(point, bucketDays) {
  const from = new Date(point.from)
  const d = `${from.getDate()}/${from.getMonth() + 1}`
  if (bucketDays === 1) return d
  const to = new Date(point.to)
  const range = `${d}–${to.getDate()}/${to.getMonth() + 1}`
  // Ô cuối của mốc 90 ngày luôn là tuần đang chạy dở; nói rõ để không ai đọc cột thấp đó thành
  // "tương tác đang tụt".
  return point.partial ? `${range} (chưa hết tuần)` : range
}

/** Cột chồng: thích + bình luận + chia sẻ theo từng mốc thời gian. */
export function EngagementChart({ series, bucketDays }) {
  const [hover, setHover] = useState(null)
  const clipId = useId()

  const totals = series.map((p) => p.likes + p.comments + p.shares)
  const top = niceTop(Math.max(...totals, 0))
  const H = 180
  const plotH = H - 26 - 8

  const n = series.length
  const slot = 100 / n // phần trăm chiều rộng mỗi ô
  const grand = totals.reduce((a, b) => a + b, 0)

  if (grand === 0) {
    return (
      <p className="chart-empty">
        Chưa có tương tác nào trong khoảng này. Biểu đồ sẽ hiện khi bài bắt đầu có lượt thích
        hoặc bình luận.
      </p>
    )
  }

  return (
    <div className="chart">
      <div className="chart-legend">
        {SERIES.map((s) => (
          <span key={s.key} className="chart-legend-item">
            <span className="chart-swatch" style={{ background: s.color }} aria-hidden="true" />
            {s.label}
          </span>
        ))}
      </div>

      <div className="chart-canvas">
        <svg viewBox={`0 0 100 ${H}`} preserveAspectRatio="none" className="chart-svg" role="img"
             aria-label={`Tương tác theo thời gian, tổng ${grand}`}>
          <defs>
            <clipPath id={clipId}><rect x="0" y="0" width="100" height={H} /></clipPath>
          </defs>
          {[0, 0.5, 1].map((f) => (
            <line key={f} x1="0" x2="100" y1={8 + plotH * f} y2={8 + plotH * f}
                  className="chart-grid" vectorEffect="non-scaling-stroke" />
          ))}

          {series.map((p, i) => {
            const total = totals[i]
            const barW = Math.min(slot - 1.2, MAX_BAR_PCT)
            const x = i * slot + (slot - barW) / 2
            let y = 8 + plotH
            const isHover = hover === i
            return (
              <g key={i} clipPath={`url(#${clipId})`}>
                {SERIES.map((s, si) => {
                  const v = p[s.key]
                  if (v <= 0) return null
                  const h = (v / top) * plotH
                  y -= h
                  const isTop = SERIES.slice(si + 1).every((o) => p[o.key] <= 0)
                  return (
                    <rect
                      key={s.key}
                      x={x} y={y} width={barW}
                      height={Math.max(h - (si === 0 ? 0 : GAP * (plotH / H) * 0.9), 0.6)}
                      fill={s.color}
                      rx={isTop ? 1 : 0}
                      opacity={hover === null || isHover ? 1 : 0.45}
                    />
                  )
                })}
                {/* Vùng bắt chuột rộng hết ô, không chỉ phần cột đã tô — cột 4px thì không ai trỏ trúng. */}
                <rect x={i * slot} y="0" width={slot} height={H} fill="transparent"
                      onMouseEnter={() => setHover(i)} onMouseLeave={() => setHover(null)} />
                {total > 0 && isHover && (
                  <line x1={i * slot + slot / 2} x2={i * slot + slot / 2} y1="8" y2={8 + plotH}
                        className="chart-crosshair" vectorEffect="non-scaling-stroke" />
                )}
              </g>
            )
          })}
        </svg>

        <div className="chart-yaxis" style={{ height: plotH, top: 8 }}>
          <span>{num(top)}</span>
          <span>{num(Math.round(top / 2))}</span>
          <span>0</span>
        </div>

        {hover !== null && (
          <div
            className="chart-tip"
            style={{ left: `${Math.min(Math.max((hover + 0.5) * slot, 12), 88)}%` }}
          >
            <div className="chart-tip-head">{labelOf(series[hover], bucketDays)}</div>
            {SERIES.map((s) => (
              <div key={s.key} className="chart-tip-row">
                <span className="chart-tip-key" style={{ background: s.color }} aria-hidden="true" />
                <span className="chart-tip-val">{num(series[hover][s.key])}</span>
                <span className="chart-tip-lbl">{s.label}</span>
              </div>
            ))}
            <div className="chart-tip-row chart-tip-total">
              <span className="chart-tip-val">{num(totals[hover])}</span>
              <span className="chart-tip-lbl">tổng · {series[hover].posts} bài đăng</span>
            </div>
          </div>
        )}
      </div>

      <div className="chart-xaxis">
        <span>{labelOf(series[0], bucketDays)}</span>
        <span>{labelOf(series[series.length - 1], bucketDays)}</span>
      </div>
    </div>
  )
}

/** Một chuỗi duy nhất → không cần hộp chú giải, tiêu đề đã nói rõ đang vẽ cái gì. */
export function PostsChart({ series, bucketDays }) {
  const [hover, setHover] = useState(null)
  const values = series.map((p) => p.posts)
  const top = niceTop(Math.max(...values, 0))
  const H = 120
  const plotH = H - 24
  const n = series.length
  const slot = 100 / n
  const peak = values.indexOf(Math.max(...values))

  if (values.every((v) => v === 0)) {
    return <p className="chart-empty">Chưa đăng bài nào trong khoảng này.</p>
  }

  return (
    <div className="chart">
      <div className="chart-canvas">
        <svg viewBox={`0 0 100 ${H}`} preserveAspectRatio="none" className="chart-svg" role="img"
             aria-label={`Số bài đăng theo thời gian, cao nhất ${Math.max(...values)} bài`}>
          <line x1="0" x2="100" y1={8 + plotH} y2={8 + plotH} className="chart-grid"
                vectorEffect="non-scaling-stroke" />
          {series.map((p, i) => {
            const h = (p.posts / top) * plotH
            const barW = Math.min(slot - 1.2, MAX_BAR_PCT)
            const x = i * slot + (slot - barW) / 2
            return (
              <g key={i}>
                {p.posts > 0 && (
                  <rect x={x} y={8 + plotH - h} width={barW} height={Math.max(h, 0.6)} rx="1"
                        fill="#2d6cb6"
                        opacity={hover === null || hover === i ? (i === peak ? 1 : 0.8) : 0.35} />
                )}
                <rect x={i * slot} y="0" width={slot} height={H} fill="transparent"
                      onMouseEnter={() => setHover(i)} onMouseLeave={() => setHover(null)} />
              </g>
            )
          })}
        </svg>

        <div className="chart-yaxis" style={{ height: plotH, top: 8 }}>
          <span>{num(top)}</span>
          <span />
          <span>0</span>
        </div>

        {hover !== null && (
          <div className="chart-tip" style={{ left: `${Math.min(Math.max((hover + 0.5) * slot, 12), 88)}%` }}>
            <div className="chart-tip-head">{labelOf(series[hover], bucketDays)}</div>
            <div className="chart-tip-row">
              <span className="chart-tip-val">{num(series[hover].posts)}</span>
              <span className="chart-tip-lbl">bài đăng</span>
            </div>
          </div>
        )}
      </div>
      <div className="chart-xaxis">
        <span>{labelOf(series[0], bucketDays)}</span>
        <span>{labelOf(series[series.length - 1], bucketDays)}</span>
      </div>
    </div>
  )
}

/** Cơ cấu tương tác: một dải ngang, nhãn chữ đi kèm nên không phụ thuộc màu để đọc. */
export function MixBar({ engagement }) {
  const total = engagement.total || 0
  if (total === 0) return null

  return (
    <div className="mix">
      <div className="mix-bar">
        {SERIES.map((s) => {
          const v = engagement[s.key] ?? 0
          if (v <= 0) return null
          return (
            <span
              key={s.key}
              className="mix-seg"
              style={{ width: `${(v / total) * 100}%`, background: s.color }}
              title={`${s.label}: ${num(v)}`}
            />
          )
        })}
      </div>
      <div className="mix-legend">
        {SERIES.map((s) => {
          const v = engagement[s.key] ?? 0
          return (
            <span key={s.key} className="mix-item">
              <span className="chart-swatch" style={{ background: s.color }} aria-hidden="true" />
              <strong>{num(v)}</strong> {s.label}
              <span className="mix-pct">{total > 0 ? Math.round((v / total) * 100) : 0}%</span>
            </span>
          )
        })}
      </div>
    </div>
  )
}

/**
 * Người theo dõi theo ngày.
 *
 * Dưới hai điểm thì KHÔNG vẽ. Một điểm nối thành đường thẳng nằm ngang trông y hệt "không đổi
 * suốt kỳ", trong khi sự thật là "mới đo được một lần". Nói thẳng còn hơn vẽ một biểu đồ nói dối.
 */
export function FollowerChart({ points }) {
  const usable = (points ?? []).filter((p) => p.measuredPages > 0)
  if (usable.length < 2) {
    return (
      <p className="chart-empty">
        Cần ít nhất 2 ngày số liệu mới vẽ được đường xu hướng — hiện có {usable.length}.
        Hệ thống chốt một mốc mỗi ngày, nên biểu đồ sẽ tự hiện từ ngày mai.
      </p>
    )
  }

  const values = usable.map((p) => p.followers)
  const min = Math.min(...values)
  const max = Math.max(...values)
  // Không neo về 0: người theo dõi dao động vài chục trên nền 30.000, vẽ từ 0 thì đường phẳng lì.
  // Bù lại phải nói rõ trục không bắt đầu từ 0 — nhãn trục dưới đây làm việc đó.
  const lo = min - Math.max((max - min) * 0.2, 1)
  const hi = max + Math.max((max - min) * 0.2, 1)
  const H = 120
  const plotH = H - 20

  const pts = usable.map((p, i) => {
    const x = usable.length === 1 ? 50 : (i / (usable.length - 1)) * 100
    const y = 8 + plotH - ((p.followers - lo) / (hi - lo)) * plotH
    return `${x},${y}`
  })

  return (
    <div className="chart">
      <div className="chart-canvas">
        <svg viewBox={`0 0 100 ${H}`} preserveAspectRatio="none" className="chart-svg" role="img"
             aria-label={`Người theo dõi từ ${num(min)} đến ${num(max)}`}>
          <polyline points={pts.join(' ')} fill="none" stroke="#2d6cb6" strokeWidth="2"
                    strokeLinejoin="round" strokeLinecap="round" vectorEffect="non-scaling-stroke" />
        </svg>
        <div className="chart-yaxis" style={{ height: plotH, top: 8 }}>
          <span>{num(max)}</span>
          <span />
          <span>{num(min)}</span>
        </div>
      </div>
      <div className="chart-xaxis">
        <span>trục dọc bắt đầu từ {num(min)}, không phải 0</span>
        <span>{usable.length} ngày</span>
      </div>
    </div>
  )
}
