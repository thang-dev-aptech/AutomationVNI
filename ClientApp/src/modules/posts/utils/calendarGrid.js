import { toUtcDate } from '@/shared/utils/apiHelpers'

/**
 * Tiện ích dựng lưới lịch tháng.
 *
 * Nguyên tắc xuyên suốt: lưới là lịch THEO GIỜ VN, không theo giờ máy người dùng. Backend trả
 * mốc thời gian dạng UTC, nên mọi chỗ quy đổi đều phải đi qua đây — tự `new Date().getDate()`
 * trên máy lệch múi giờ sẽ gom bài vào sai ô ngày.
 */

const VN_TIMEZONE = 'Asia/Ho_Chi_Minh'

/** VN cố định UTC+7, không có DST — nên bù trừ bằng hằng số là an toàn. */
const VN_OFFSET_MS = 7 * 60 * 60 * 1000

const ymdFormatter = new Intl.DateTimeFormat('en-CA', {
  timeZone: VN_TIMEZONE,
  year: 'numeric',
  month: '2-digit',
  day: '2-digit',
})

/** Đổi một mốc thời gian bất kỳ sang khoá ngày 'YYYY-MM-DD' THEO GIỜ VN. */
export function toVnYmd(value) {
  if (!value) return ''
  const date = value instanceof Date ? value : toUtcDate(value)
  if (Number.isNaN(date.getTime())) return ''
  const parts = ymdFormatter.formatToParts(date)
  const get = (type) => parts.find((p) => p.type === type)?.value ?? ''
  return `${get('year')}-${get('month')}-${get('day')}`
}

/** Giờ-phút theo giờ VN của một mốc thời gian, dạng { hour, minute }. */
export function getVnTimeParts(value) {
  const date = value instanceof Date ? value : toUtcDate(value)
  const parts = new Intl.DateTimeFormat('en-GB', {
    timeZone: VN_TIMEZONE,
    hour: '2-digit',
    minute: '2-digit',
    hour12: false,
  }).formatToParts(date)
  const get = (type) => Number(parts.find((p) => p.type === type)?.value ?? 0)
  return { hour: get('hour'), minute: get('minute') }
}

/** Mốc UTC ứng với 00:00 giờ VN của ngày 'YYYY-MM-DD'. */
function vnMidnightToUtc(ymd) {
  const [y, m, d] = ymd.split('-').map(Number)
  return new Date(Date.UTC(y, m - 1, d) - VN_OFFSET_MS)
}

function pad2(n) {
  return String(n).padStart(2, '0')
}

/**
 * Dựng lưới 6 tuần × 7 ngày (42 ô) cho tháng chỉ định, tuần bắt đầu THỨ 2 theo thói quen VN.
 *
 * Lưới được tính thuần số học trên trục UTC (Date.UTC + getUTC*) — cố ý không dính múi giờ, vì
 * ở đây chỉ cần biết "tháng 8/2026 có 31 ngày và mùng 1 rơi vào thứ mấy". Việc quy đổi múi giờ
 * chỉ xảy ra khi gom bài vào ô, qua toVnYmd().
 *
 * @param {number} year năm, ví dụ 2026
 * @param {number} month tháng 1-12 (không phải 0-11 như Date)
 */
export function buildMonthGrid(year, month) {
  const firstOfMonth = new Date(Date.UTC(year, month - 1, 1))

  // getUTCDay(): 0=CN..6=T7 → đổi sang 0=T2..6=CN để tuần bắt đầu từ thứ Hai.
  const weekdayMondayFirst = (firstOfMonth.getUTCDay() + 6) % 7

  const gridStart = new Date(firstOfMonth)
  gridStart.setUTCDate(gridStart.getUTCDate() - weekdayMondayFirst)

  const todayYmd = toVnYmd(new Date())
  const cells = []
  const cursor = new Date(gridStart)

  for (let i = 0; i < 42; i += 1) {
    const y = cursor.getUTCFullYear()
    const m = cursor.getUTCMonth() + 1
    const d = cursor.getUTCDate()
    const ymd = `${y}-${pad2(m)}-${pad2(d)}`

    cells.push({
      ymd,
      day: d,
      isCurrentMonth: m === month && y === year,
      isToday: ymd === todayYmd,
      isPast: ymd < todayYmd,
    })

    cursor.setUTCDate(d + 1)
  }

  return cells
}

/**
 * Khoảng UTC bao trọn lưới 42 ô (kể cả ngày rơi sang tháng trước/sau), để gọi API.
 * Trả nửa khoảng mở [fromUtc, toUtc).
 */
export function monthRangeUtc(year, month) {
  const cells = buildMonthGrid(year, month)
  const first = cells[0].ymd
  const last = cells[cells.length - 1].ymd

  const toExclusive = vnMidnightToUtc(last)
  toExclusive.setUTCDate(toExclusive.getUTCDate() + 1)

  return {
    fromUtc: vnMidnightToUtc(first).toISOString(),
    toUtc: toExclusive.toISOString(),
  }
}

/**
 * Giữ nguyên giờ-phút (theo giờ VN) của mốc cũ, chỉ đổi phần NGÀY sang ymd mới.
 * Dùng khi kéo-thả bài sang ô ngày khác. Trả về Date (mốc UTC) để gọi .toISOString().
 */
export function withVnDate(originalValue, targetYmd) {
  const { hour, minute } = getVnTimeParts(originalValue)
  const [y, m, d] = targetYmd.split('-').map(Number)
  return new Date(Date.UTC(y, m - 1, d, hour, minute) - VN_OFFSET_MS)
}

/** Nhãn "Tháng 8/2026". */
export function monthLabel(year, month) {
  return `Tháng ${month}/${year}`
}

export const WEEKDAY_LABELS = ['T2', 'T3', 'T4', 'T5', 'T6', 'T7', 'CN']
