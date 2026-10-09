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

/** Nhãn cột tuần đầy đủ. */
export const WEEKDAY_FULL_LABELS = [
  'Thứ 2', 'Thứ 3', 'Thứ 4', 'Thứ 5', 'Thứ 6', 'Thứ 7', 'Chủ Nhật',
]

/** Cộng/trừ ngày trên chuỗi YYYY-MM-DD (lịch dân sự, khớp buildMonthGrid). */
export function shiftYmd(ymd, days) {
  const [y, m, d] = ymd.split('-').map(Number)
  const dt = new Date(Date.UTC(y, m - 1, d))
  dt.setUTCDate(dt.getUTCDate() + days)
  return `${dt.getUTCFullYear()}-${pad2(dt.getUTCMonth() + 1)}-${pad2(dt.getUTCDate())}`
}

/**
 * Thứ Hai (YYYY-MM-DD) của tuần giờ VN chứa `value` (Date/ISO hoặc ymd).
 * Tuần: Thứ 2 → Chủ nhật.
 */
export function startOfVnWeek(value) {
  const ymd = typeof value === 'string' && /^\d{4}-\d{2}-\d{2}$/.test(value)
    ? value
    : toVnYmd(value)
  if (!ymd) return ''
  const [y, m, d] = ymd.split('-').map(Number)
  const dt = new Date(Date.UTC(y, m - 1, d))
  const mondayFirst = (dt.getUTCDay() + 6) % 7
  dt.setUTCDate(dt.getUTCDate() - mondayFirst)
  return `${dt.getUTCFullYear()}-${pad2(dt.getUTCMonth() + 1)}-${pad2(dt.getUTCDate())}`
}

/**
 * Chuẩn hoá tham số `week` trên URL → YYYY-MM-DD Thứ Hai (giờ VN).
 * Sai định dạng / ngày không tồn tại → '' (bỏ). Không phải Thứ 2 → căn về Thứ 2 của tuần chứa ngày đó.
 */
export function normalizeWeekStartYmd(raw) {
  if (!raw || typeof raw !== 'string') return ''
  if (!/^\d{4}-\d{2}-\d{2}$/.test(raw)) return ''
  const [y, m, d] = raw.split('-').map(Number)
  if (!y || m < 1 || m > 12 || d < 1 || d > 31) return ''
  const dt = new Date(Date.UTC(y, m - 1, d))
  if (dt.getUTCFullYear() !== y || dt.getUTCMonth() + 1 !== m || dt.getUTCDate() !== d) {
    return ''
  }
  return startOfVnWeek(raw)
}

/**
 * Tuần mặc định khi mở dạng Tuần: tuần chứa hôm nay nếu cùng tháng cursor, không thì tuần ngày 1.
 */
export function defaultWeekStart(year, month) {
  const todayYmd = toVnYmd(new Date())
  const [ty, tm] = todayYmd.split('-').map(Number)
  if (ty === year && tm === month) return startOfVnWeek(todayYmd)
  return startOfVnWeek(`${year}-${pad2(month)}-01`)
}

/**
 * Gộp khoảng tháng (lưới 42 ô) với khoảng tuần khi dạng Tuần — đủ bài khi tuần vắt tháng khác cursor.
 */
export function mergeMonthAndWeekRangeUtc(year, month, weekStartYmd) {
  const monthR = monthRangeUtc(year, month)
  if (!weekStartYmd) return monthR
  const weekR = weekRangeUtc(weekStartYmd)
  return {
    fromUtc: monthR.fromUtc < weekR.fromUtc ? monthR.fromUtc : weekR.fromUtc,
    toUtc: monthR.toUtc > weekR.toUtc ? monthR.toUtc : weekR.toUtc,
  }
}

/**
 * 7 ô Thứ 2→CN của tuần bắt đầu `weekStartYmd` (phải là Thứ Hai).
 * headerLabel dạng "Thứ 2 05".
 */
export function buildWeekGrid(weekStartYmd) {
  const todayYmd = toVnYmd(new Date())
  const [y, m, d] = weekStartYmd.split('-').map(Number)
  const cursor = new Date(Date.UTC(y, m - 1, d))
  const cells = []

  for (let i = 0; i < 7; i += 1) {
    const cy = cursor.getUTCFullYear()
    const cm = cursor.getUTCMonth() + 1
    const cd = cursor.getUTCDate()
    const ymd = `${cy}-${pad2(cm)}-${pad2(cd)}`
    cells.push({
      ymd,
      day: cd,
      month: cm,
      year: cy,
      weekdayLabel: WEEKDAY_FULL_LABELS[i],
      headerLabel: `${WEEKDAY_FULL_LABELS[i]} ${pad2(cd)}`,
      isToday: ymd === todayYmd,
      isPast: ymd < todayYmd,
      isWeekend: i >= 5,
    })
    cursor.setUTCDate(cd + 1)
  }

  return cells
}

/** Nhãn thanh điều hướng tuần: "05/10 - 11/10". */
export function weekRangeLabel(weekStartYmd) {
  const cells = buildWeekGrid(weekStartYmd)
  const first = cells[0]
  const last = cells[6]
  return `${pad2(first.day)}/${pad2(first.month)} - ${pad2(last.day)}/${pad2(last.month)}`
}

/** Khoảng UTC nửa mở [from, to) bao trọn tuần. */
export function weekRangeUtc(weekStartYmd) {
  const cells = buildWeekGrid(weekStartYmd)
  const toExclusive = vnMidnightToUtc(cells[6].ymd)
  toExclusive.setUTCDate(toExclusive.getUTCDate() + 1)
  return {
    fromUtc: vnMidnightToUtc(cells[0].ymd).toISOString(),
    toUtc: toExclusive.toISOString(),
  }
}
