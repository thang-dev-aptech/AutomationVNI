import { useState } from 'react'
import { num } from '../utils/metricFormat'
import './Charts.css'

/**
 * Biểu đồ vẽ bằng SVG thuần, không thêm thư viện.
 *
 * Dự án đang có 11 gói phụ thuộc và gói JS đã 600 KB kèm cảnh báo quá ngưỡng. Recharts thêm
 * khoảng 500 KB cho mấy biểu đồ này — không đáng. Tự viết thì màu bám thẳng vào biến CSS
 * sẵn có của dự án.
 *
 * ═══ BA MÀU CHUỖI ĐÃ QUA KIỂM ═══
 *
 * #2d6cb6 xanh dương · #f08a22 cam · #10b981 xanh lá:
 *   dải sáng ĐẠT · sàn độ tươi ĐẠT · tách màu cho người mù màu ĐẠT (ΔE 10,4 protan)
 *   sàn thị lực thường ĐẠT (ΔE 24,7) · tương phản nền CẢNH BÁO (2,5:1 < 3:1)
 *
 * Cảnh báo tương phản bắt buộc bù bằng NHÃN CHỮ — nên chú giải luôn hiện kèm tên, tooltip luôn
 * đọc ra số, và bảng xếp hạng phía dưới là bản tra cứu không cần rê chuột.
 *
 * MỘT ĐÁNH ĐỔI ĐÃ BIẾT: #10b981 chính là --color-success, vốn là màu TRẠNG THÁI. Bảng màu dự
 * án không có sắc thứ tư đủ khác (--color-warning quá gần cam). Trong ba biểu đồ này không ô
 * nào mã hoá trạng thái, nên xanh lá chỉ mang một nghĩa duy nhất là "Chia sẻ", luôn kèm chữ.
 *
 * ═══ VÌ SAO CHẤM TRÒN LÀ THẺ HTML, KHÔNG PHẢI <circle> ═══
 *
 * Các biểu đồ đường dùng preserveAspectRatio="none" để căng hết bề ngang khung. Kiểu căng đó
 * kéo trục X mà không kéo trục Y, nên mọi <circle> vẽ trong đó biến thành hình bầu dục méo.
 * Đặt chấm bằng thẻ HTML định vị tuyệt đối thì luôn tròn đều ở mọi bề rộng màn hình.
 */
const SERIES = [
  { key: 'likes', label: 'Thích', color: '#2d6cb6' },
  { key: 'comments', label: 'Bình luận', color: '#f08a22' },
  { key: 'shares', label: 'Chia sẻ', color: '#10b981' },
]

// Thang chia bám sát dữ liệu. Thang thô [1,2,2.5,5] đẩy đỉnh 28 lên trục 50 và đường cao nhất
// chỉ chiếm 56% khung — nhìn như tương tác đang thấp trong khi đó là đỉnh.
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
  return point.partial ? `${range} (chưa hết tuần)` : range
}

/** Toạ độ X theo phần trăm — dùng chung cho đường, chấm cuối, và vùng bắt chuột. */
const xAt = (i, n) => (n <= 1 ? 50 : (i / (n - 1)) * 100)

/**
 * Nhãn vạch giữa trục dọc.
 *
 * Phải in ĐÚNG giá trị của vạch đó. Đỉnh trục 25 thì vạch giữa là 12,5 — làm tròn thành "13"
 * là dán một con số sai lên một đường kẻ có thật, và ai đọc giá trị theo vạch sẽ lệch.
 */
const midLabel = (top) => {
  const mid = top / 2
  return Number.isInteger(mid) ? num(mid) : mid.toLocaleString('vi-VN')
}

function Grid({ plotH, top: padTop = 8, lines = [0, 0.5, 1] }) {
  return lines.map((f) => (
    <line key={f} x1="0" x2="100" y1={padTop + plotH * f} y2={padTop + plotH * f}
          className="chart-grid" vectorEffect="non-scaling-stroke" />
  ))
}

/** Ba đường: thích · bình luận · chia sẻ. Cùng một trục, không bao giờ hai trục. */
export function EngagementChart({ series, bucketDays }) {
  const [hover, setHover] = useState(null)

  const n = series.length
  const maxVal = Math.max(...series.flatMap((p) => SERIES.map((s) => p[s.key])), 0)
  const grand = series.reduce((a, p) => a + p.likes + p.comments + p.shares, 0)
  const top = niceTop(maxVal)
  const H = 180
  const PAD = 8
  const plotH = H - 26 - PAD

  if (grand === 0) {
    return (
      <p className="chart-empty">
        Chưa có tương tác nào trong khoảng này. Biểu đồ sẽ hiện khi bài bắt đầu có lượt thích
        hoặc bình luận.
      </p>
    )
  }

  const yAt = (v) => PAD + plotH - (v / top) * plotH

  return (
    <div className="chart">
      <div className="chart-legend">
        {SERIES.map((s) => (
          <span key={s.key} className="chart-legend-item">
            <span className="chart-key" style={{ background: s.color }} aria-hidden="true" />
            {s.label}
          </span>
        ))}
      </div>

      <div className="chart-canvas">
        <svg viewBox={`0 0 100 ${H}`} preserveAspectRatio="none" className="chart-svg" role="img"
             aria-label={`Tương tác theo thời gian, tổng ${grand}`}>
          <Grid plotH={plotH} />
          {SERIES.map((s) => (
            <polyline
              key={s.key}
              points={series.map((p, i) => `${xAt(i, n)},${yAt(p[s.key])}`).join(' ')}
              fill="none" stroke={s.color} strokeWidth="2"
              strokeLinejoin="round" strokeLinecap="round"
              vectorEffect="non-scaling-stroke"
              opacity={hover === null ? 1 : 0.85}
            />
          ))}
          {hover !== null && (
            <line x1={xAt(hover, n)} x2={xAt(hover, n)} y1={PAD} y2={PAD + plotH}
                  className="chart-crosshair" vectorEffect="non-scaling-stroke" />
          )}
          {/* Vùng bắt chuột trải hết ô — người ta nhắm vào một NGÀY, không ai nhắm trúng nét 2px. */}
          {series.map((_, i) => (
            <rect key={i} x={Math.max(xAt(i, n) - 50 / (n - 1 || 1), 0)} y="0"
                  width={100 / (n - 1 || 1)} height={H} fill="transparent"
                  onMouseEnter={() => setHover(i)} onMouseLeave={() => setHover(null)} />
          ))}
        </svg>

        {/* Chấm cuối mỗi đường + chấm trên đường lúc rê chuột. Vòng viền màu nền để chấm không
            lẫn vào nhau chỗ hai đường cắt nhau. */}
        {SERIES.map((s) => {
          const i = hover ?? n - 1
          return (
            <span key={s.key} className="chart-dot"
                  style={{
                    left: `${xAt(i, n)}%`,
                    top: `${yAt(series[i][s.key])}px`,
                    background: s.color,
                  }} />
          )
        })}

        <div className="chart-yaxis" style={{ height: plotH, top: PAD }}>
          <span>{num(top)}</span>
          <span>{midLabel(top)}</span>
          <span>0</span>
        </div>

        {hover !== null && (
          <div className="chart-tip"
               style={{ left: `${Math.min(Math.max(xAt(hover, n), 14), 86)}%` }}>
            <div className="chart-tip-head">{labelOf(series[hover], bucketDays)}</div>
            {SERIES.map((s) => (
              <div key={s.key} className="chart-tip-row">
                <span className="chart-tip-key" style={{ background: s.color }} aria-hidden="true" />
                <span className="chart-tip-val">{num(series[hover][s.key])}</span>
                <span className="chart-tip-lbl">{s.label}</span>
              </div>
            ))}
            <div className="chart-tip-row chart-tip-total">
              <span className="chart-tip-val">{num(series[hover].posts)}</span>
              <span className="chart-tip-lbl">bài đăng hôm đó</span>
            </div>
          </div>
        )}
      </div>

      <div className="chart-xaxis">
        <span>{labelOf(series[0], bucketDays)}</span>
        <span>{labelOf(series[n - 1], bucketDays)}</span>
      </div>
    </div>
  )
}

/**
 * Một chuỗi duy nhất → đường kèm mảng nền nhạt, và KHÔNG cần hộp chú giải: chỉ có một màu,
 * tiêu đề đã nói rõ đang vẽ gì. Mảng nền để 10% độ đục — một lớp phủ mờ, không phải khối đặc.
 */
export function PostsChart({ series, bucketDays }) {
  const [hover, setHover] = useState(null)
  const n = series.length
  const values = series.map((p) => p.posts)
  const top = niceTop(Math.max(...values, 0))
  const H = 120
  const PAD = 8
  const plotH = H - 24 - PAD

  if (values.every((v) => v === 0)) {
    return <p className="chart-empty">Chưa đăng bài nào trong khoảng này.</p>
  }

  const yAt = (v) => PAD + plotH - (v / top) * plotH
  const line = series.map((p, i) => `${xAt(i, n)},${yAt(p.posts)}`).join(' ')
  const area = `0,${PAD + plotH} ${line} 100,${PAD + plotH}`

  return (
    <div className="chart">
      <div className="chart-canvas">
        <svg viewBox={`0 0 100 ${H}`} preserveAspectRatio="none" className="chart-svg" role="img"
             aria-label={`Số bài đăng theo thời gian, cao nhất ${Math.max(...values)} bài`}>
          <Grid plotH={plotH} lines={[0, 1]} />
          <polygon points={area} fill="#2d6cb6" opacity="0.1" />
          <polyline points={line} fill="none" stroke="#2d6cb6" strokeWidth="2"
                    strokeLinejoin="round" strokeLinecap="round" vectorEffect="non-scaling-stroke" />
          {hover !== null && (
            <line x1={xAt(hover, n)} x2={xAt(hover, n)} y1={PAD} y2={PAD + plotH}
                  className="chart-crosshair" vectorEffect="non-scaling-stroke" />
          )}
          {series.map((_, i) => (
            <rect key={i} x={Math.max(xAt(i, n) - 50 / (n - 1 || 1), 0)} y="0"
                  width={100 / (n - 1 || 1)} height={H} fill="transparent"
                  onMouseEnter={() => setHover(i)} onMouseLeave={() => setHover(null)} />
          ))}
        </svg>

        <span className="chart-dot"
              style={{
                left: `${xAt(hover ?? n - 1, n)}%`,
                top: `${yAt(series[hover ?? n - 1].posts)}px`,
                background: '#2d6cb6',
              }} />

        <div className="chart-yaxis" style={{ height: plotH, top: PAD }}>
          <span>{num(top)}</span>
          <span />
          <span>0</span>
        </div>

        {hover !== null && (
          <div className="chart-tip" style={{ left: `${Math.min(Math.max(xAt(hover, n), 16), 84)}%` }}>
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
        <span>{labelOf(series[n - 1], bucketDays)}</span>
      </div>
    </div>
  )
}

/**
 * Cơ cấu tương tác — biểu đồ tròn khuyết.
 *
 * Tròn chỉ dùng được cho phần-trên-tổng, tối đa 6 miếng, và các giá trị phải CÁCH XA nhau —
 * mắt người so góc rất kém, hai miếng 31% và 34% thì nhìn y hệt nhau. Ở đây ba miếng
 * 68% / 26% / 6% cách nhau rất rộng nên đọc được ngay.
 *
 * Con số phần trăm vẫn in ra chữ bên dưới, nên không ai phải ước lượng góc để lấy số.
 *
 * SVG này KHÔNG dùng preserveAspectRatio="none" — vòng tròn phải tròn.
 */
export function MixDonut({ engagement }) {
  const [hover, setHover] = useState(null)
  const total = engagement.total || 0
  if (total === 0) return null

  const R = 52
  const STROKE = 22
  const C = 2 * Math.PI * R
  const GAP = 3 // khe hở màu nền giữa hai miếng, tính theo đơn vị chu vi

  let offset = 0
  const arcs = SERIES.map((s) => {
    const v = engagement[s.key] ?? 0
    const len = (v / total) * C
    const arc = { ...s, value: v, len, offset, pct: Math.round((v / total) * 100) }
    offset += len
    return arc
  }).filter((a) => a.value > 0)

  const focus = hover === null ? null : arcs[hover]

  return (
    <div className="donut-wrap">
      <div className="donut">
        <svg viewBox="0 0 140 140" className="donut-svg" role="img"
             aria-label={`Cơ cấu tương tác: ${arcs.map((a) => `${a.label} ${a.pct}%`).join(', ')}`}>
          <g transform="rotate(-90 70 70)">
            {arcs.map((a, i) => (
              <circle
                key={a.key}
                cx="70" cy="70" r={R}
                fill="none"
                stroke={a.color}
                strokeWidth={hover === i ? STROKE + 4 : STROKE}
                strokeDasharray={`${Math.max(a.len - GAP, 0.5)} ${C - Math.max(a.len - GAP, 0.5)}`}
                strokeDashoffset={-a.offset}
                opacity={hover === null || hover === i ? 1 : 0.4}
                onMouseEnter={() => setHover(i)}
                onMouseLeave={() => setHover(null)}
                className="donut-arc"
              />
            ))}
          </g>
          <text x="70" y="66" className="donut-num">{num(focus ? focus.value : total)}</text>
          <text x="70" y="84" className="donut-lbl">{focus ? focus.label : 'tương tác'}</text>
        </svg>
      </div>

      <ul className="donut-legend">
        {arcs.map((a, i) => (
          <li key={a.key}
              className={hover === i ? 'is-focus' : undefined}
              onMouseEnter={() => setHover(i)}
              onMouseLeave={() => setHover(null)}>
            <span className="chart-key" style={{ background: a.color }} aria-hidden="true" />
            <span className="donut-legend-label">{a.label}</span>
            <strong>{num(a.value)}</strong>
            <span className="donut-pct">{a.pct}%</span>
          </li>
        ))}
      </ul>
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
  const [hover, setHover] = useState(null)
  const usable = (points ?? []).filter((p) => p.measuredPages > 0)

  if (usable.length < 2) {
    return (
      <p className="chart-empty">
        Cần ít nhất 2 ngày số liệu mới vẽ được đường xu hướng — hiện có {usable.length}.
        Hệ thống chốt một mốc mỗi ngày, nên biểu đồ sẽ tự hiện từ ngày mai.
      </p>
    )
  }

  const n = usable.length
  const values = usable.map((p) => p.followers)
  const min = Math.min(...values)
  const max = Math.max(...values)
  // Không neo về 0: người theo dõi dao động vài chục trên nền 30.000, vẽ từ 0 thì đường phẳng lì.
  // Bù lại PHẢI nói rõ trục không bắt đầu từ 0 — nhãn dưới biểu đồ làm việc đó.
  const lo = min - Math.max((max - min) * 0.2, 1)
  const hi = max + Math.max((max - min) * 0.2, 1)
  const H = 120
  const PAD = 8
  const plotH = H - 24 - PAD

  const yAt = (v) => PAD + plotH - ((v - lo) / (hi - lo)) * plotH
  const line = usable.map((p, i) => `${xAt(i, n)},${yAt(p.followers)}`).join(' ')

  return (
    <div className="chart">
      <div className="chart-canvas">
        <svg viewBox={`0 0 100 ${H}`} preserveAspectRatio="none" className="chart-svg" role="img"
             aria-label={`Người theo dõi từ ${num(min)} đến ${num(max)}`}>
          <Grid plotH={plotH} lines={[0, 1]} />
          <polygon points={`0,${PAD + plotH} ${line} 100,${PAD + plotH}`} fill="#2d6cb6" opacity="0.1" />
          <polyline points={line} fill="none" stroke="#2d6cb6" strokeWidth="2"
                    strokeLinejoin="round" strokeLinecap="round" vectorEffect="non-scaling-stroke" />
          {usable.map((_, i) => (
            <rect key={i} x={Math.max(xAt(i, n) - 50 / (n - 1 || 1), 0)} y="0"
                  width={100 / (n - 1 || 1)} height={H} fill="transparent"
                  onMouseEnter={() => setHover(i)} onMouseLeave={() => setHover(null)} />
          ))}
        </svg>

        <span className="chart-dot"
              style={{
                left: `${xAt(hover ?? n - 1, n)}%`,
                top: `${yAt(usable[hover ?? n - 1].followers)}px`,
                background: '#2d6cb6',
              }} />

        <div className="chart-yaxis" style={{ height: plotH, top: PAD }}>
          <span>{num(max)}</span>
          <span />
          <span>{num(min)}</span>
        </div>

        {hover !== null && (
          <div className="chart-tip" style={{ left: `${Math.min(Math.max(xAt(hover, n), 20), 80)}%` }}>
            <div className="chart-tip-row">
              <span className="chart-tip-val">{num(usable[hover].followers)}</span>
              <span className="chart-tip-lbl">người theo dõi</span>
            </div>
          </div>
        )}
      </div>
      <div className="chart-xaxis">
        <span>trục dọc bắt đầu từ {num(min)}, không phải 0</span>
        <span>{n} ngày</span>
      </div>
    </div>
  )
}
