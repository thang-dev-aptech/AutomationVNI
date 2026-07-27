/**
 * Xuất khung lịch hàng loạt với page_code ngắn (1,2,3…) để gửi GPT tiết kiệm token.
 * Map page_code → page_id lưu localStorage + file map riêng.
 */

export const BULK_PAGE_MAP_STORAGE_KEY = 'vni.bulkPageMap.v1'
export const BULK_IMPORT_HEADER = 'page_code,scheduled_at,idea,objective'
export const BULK_PAGE_MAP_HEADER = 'page_code,page_id,page_name'

function escapeCsvCell(value) {
  const s = String(value ?? '')
  if (/[",\r\n]/.test(s)) return `"${s.replace(/"/g, '""')}"`
  return s
}

function downloadCsv(filename, header, rows) {
  const lines = [header, ...rows.map((row) => row.map(escapeCsvCell).join(','))]
  const blob = new Blob(['\uFEFF' + lines.join('\n')], { type: 'text/csv;charset=utf-8' })
  const url = URL.createObjectURL(blob)
  const a = document.createElement('a')
  a.href = url
  a.download = filename
  a.click()
  URL.revokeObjectURL(url)
}

function pad2(n) {
  return String(n).padStart(2, '0')
}

/** yyyy-MM-dd HH:mm */
export function formatLocalScheduleFromDate(dt) {
  return `${dt.getFullYear()}-${pad2(dt.getMonth() + 1)}-${pad2(dt.getDate())} ${pad2(dt.getHours())}:${pad2(dt.getMinutes())}`
}

/** @deprecated dùng formatLocalScheduleFromDate */
export function formatLocalSchedule(date, hour, minute) {
  const d = new Date(date)
  d.setHours(hour, minute, 0, 0)
  return formatLocalScheduleFromDate(d)
}

/**
 * Lệch ngẫu nhiên ±jitter phút quanh khung giờ (mỗi ngày / mỗi page khác nhau — chống spam FB).
 * @returns {Date}
 */
export function jitteredSlotDate(day, hour, minute, jitterMinutes, extraOffsetMinutes = 0) {
  const d = new Date(day)
  d.setHours(hour, minute, 0, 0)
  const jitter = Math.max(0, Number(jitterMinutes) || 0)
  if (jitter > 0) {
    const delta = Math.floor(Math.random() * (jitter * 2 + 1)) - jitter
    d.setMinutes(d.getMinutes() + delta)
  }
  if (extraOffsetMinutes) d.setMinutes(d.getMinutes() + extraOffsetMinutes)
  return d
}

/**
 * Với 1 page trong 1 ngày: sinh các mốc giờ từ slots + jitter, giữ thứ tự tăng dần.
 * @returns {Date[]}
 */
export function buildJitteredTimesForDay(day, slots, jitterMinutes, pageIndex = 0) {
  // Stagger nhẹ theo page để nhiều page không trùng phút (0..min(25,jitter) phút).
  const staggerCap = Math.min(25, Math.max(0, Number(jitterMinutes) || 0))
  const pageStagger =
    staggerCap > 0 ? (pageIndex * 7 + Math.floor(Math.random() * 5)) % (staggerCap + 1) : 0

  const times = slots.map((slot, slotIdx) =>
    jitteredSlotDate(day, slot.hour, slot.minute, jitterMinutes, pageStagger + slotIdx),
  )

  // Giữ thứ tự: khung sau không được ≤ khung trước (tránh đảo sáng/chiều do jitter lớn).
  for (let i = 1; i < times.length; i += 1) {
    if (times[i] <= times[i - 1]) {
      times[i] = new Date(times[i - 1].getTime() + 45 * 60 * 1000)
    }
  }
  return times
}

/**
 * Parse "09:00,15:00" → [{ hour, minute }]
 * @returns {{ hour: number, minute: number }[]}
 */
export function parseTimeSlots(slotsText) {
  return String(slotsText || '')
    .split(/[,;]+/)
    .map((s) => s.trim())
    .filter(Boolean)
    .map((s) => {
      const m = /^(\d{1,2}):(\d{2})$/.exec(s)
      if (!m) return null
      const hour = Number(m[1])
      const minute = Number(m[2])
      if (hour < 0 || hour > 23 || minute < 0 || minute > 59) return null
      return { hour, minute }
    })
    .filter(Boolean)
}

function eachDateInclusive(fromYmd, toYmd) {
  const from = new Date(`${fromYmd}T00:00:00`)
  const to = new Date(`${toYmd}T00:00:00`)
  if (Number.isNaN(from.getTime()) || Number.isNaN(to.getTime()) || to < from) {
    throw new Error('Khoảng ngày không hợp lệ')
  }
  const days = []
  const cur = new Date(from)
  while (cur <= to) {
    days.push(new Date(cur))
    cur.setDate(cur.getDate() + 1)
  }
  return days
}

/**
 * Gán page_code 1..N theo tên page (ổn định trong một lần xuất).
 * @param {Array<{ id: string, pageName?: string, name?: string }>} channels
 */
export function buildPageCodeMap(channels) {
  const sorted = [...channels].sort((a, b) => {
    const na = (a.pageName || a.name || '').localeCompare(b.pageName || b.name || '', 'vi')
    return na !== 0 ? na : String(a.id).localeCompare(String(b.id))
  })
  const entries = sorted.map((ch, i) => ({
    pageCode: String(i + 1),
    pageId: String(ch.id),
    pageName: ch.pageName || ch.name || '',
  }))
  return entries
}

export function savePageMapToStorage(entries) {
  const byCode = {}
  for (const e of entries) byCode[e.pageCode] = { pageId: e.pageId, pageName: e.pageName }
  localStorage.setItem(
    BULK_PAGE_MAP_STORAGE_KEY,
    JSON.stringify({ exportedAt: new Date().toISOString(), byCode }),
  )
}

export function loadPageMapFromStorage() {
  try {
    const raw = localStorage.getItem(BULK_PAGE_MAP_STORAGE_KEY)
    if (!raw) return null
    const parsed = JSON.parse(raw)
    return parsed?.byCode && typeof parsed.byCode === 'object' ? parsed : null
  } catch {
    return null
  }
}

/**
 * @param {{
 *   channels: Array<{ id: string, pageName?: string, name?: string }>,
 *   dateFrom: string,
 *   dateTo: string,
 *   slotsText: string,
 *   jitterMinutes?: number,
 * }} opts
 * @returns {{ rowCount: number, pageCount: number, entries: ReturnType<typeof buildPageCodeMap> }}
 */
export function downloadBulkScheduleSkeleton({
  channels,
  dateFrom,
  dateTo,
  slotsText,
  jitterMinutes = 35,
}) {
  if (!channels?.length) throw new Error('Chưa chọn page nào để xuất khung')
  const slots = parseTimeSlots(slotsText)
  if (slots.length === 0) throw new Error('Nhập ít nhất 1 khung giờ, ví dụ 09:00,15:00')

  const entries = buildPageCodeMap(channels)
  savePageMapToStorage(entries)

  const days = eachDateInclusive(dateFrom, dateTo)
  const jitter = Math.max(0, Math.min(240, Number(jitterMinutes) || 0))
  const skeletonRows = []
  entries.forEach((e, pageIndex) => {
    for (const day of days) {
      const times = buildJitteredTimesForDay(day, slots, jitter, pageIndex)
      for (const t of times) {
        skeletonRows.push([e.pageCode, formatLocalScheduleFromDate(t), '', ''])
      }
    }
  })

  // 1) Bản gửi GPT — chỉ page_code ngắn
  downloadCsv('bulk-gpt-skeleton.csv', BULK_IMPORT_HEADER, skeletonRows)

  return {
    rowCount: skeletonRows.length,
    pageCount: entries.length,
    entries,
    jitterMinutes: jitter,
  }
}

/**
 * Text brief gửi GPT 1 lần: page_code + tên + brand/tone từ PageContext.
 * Không kèm Guid — tiết kiệm token; GPT biết page hướng tới ai / giọng nào.
 *
 * @param {Array<{ pageCode: string, pageId: string, pageName: string }>} entries
 * @param {Map<string, { brandName?: string, toneOfVoice?: string }> | Record<string, any>} [contextByChannelId]
 */
export function buildGptPageListText(entries, contextByChannelId) {
  const getCtx = (pageId) => {
    if (!contextByChannelId) return null
    if (typeof contextByChannelId.get === 'function') return contextByChannelId.get(pageId)
    return contextByChannelId[pageId] || null
  }

  const lines = entries.map((e) => {
    const ctx = getCtx(e.pageId)
    const brand = ctx?.brandName?.trim()
    const tone = ctx?.toneOfVoice?.trim()
    const parts = [`${e.pageCode}`, e.pageName]
    if (brand) parts.push(`brand: ${brand}`)
    if (tone) parts.push(`tone: ${tone}`)
    return parts.join('\t')
  })

  return [
    'Danh sách page (page_code → ngữ cảnh). Khi điền CSV chỉ dùng page_code (1,2,3…), không viết lại tên dài.',
    'Mỗi idea phải đúng chủ đề/ngành của page đó và khớp tone. Không trộn nội dung giữa các page.',
    '',
    ...lines,
  ].join('\n')
}

/** Tải file brief riêng để đính kèm GPT (cùng nội dung copy). */
export function downloadGptPageBrief(entries, contextByChannelId) {
  const text = buildGptPageListText(entries, contextByChannelId)
  const blob = new Blob(['\uFEFF' + text], { type: 'text/plain;charset=utf-8' })
  const url = URL.createObjectURL(blob)
  const a = document.createElement('a')
  a.href = url
  a.download = 'bulk-gpt-page-brief.txt'
  a.click()
  URL.revokeObjectURL(url)
}

