/**
 * Parser CSV tạo hàng loạt.
 * - Bản mới (GPT): page_code,scheduled_at,idea,objective — resolve page_code qua map.
 * - Bản đầy đủ: có thêm page_id.
 * - Bản cũ: idea[,objective] (fan-out UI).
 */

import { loadPageMapFromStorage } from './bulkScheduleSkeleton'

const IDEA_HEADERS = new Set(['idea', 'title', 'ý tưởng', 'y tuong', 'tiêu đề', 'tieu de'])
const OBJECTIVE_HEADERS = new Set(['objective', 'mục tiêu', 'muc tieu', 'goal'])
const PAGE_CODE_HEADERS = new Set(['page_code', 'code', 'pagecode', 'stt', 'page'])
const PAGE_ID_HEADERS = new Set(['page_id', 'pageid', 'social_channel_id', 'channel_id', 'id'])
const SCHEDULE_HEADERS = new Set([
  'scheduled_at',
  'schedule_at',
  'scheduledat',
  'lich_dang',
  'lịch đăng',
  'datetime',
])

export const BULK_CREATE_SAMPLE_HEADER = 'page_code,scheduled_at,idea,objective'

export const BULK_CREATE_SAMPLE_ROWS = [
  ['1', '2026-08-01 09:00', 'Set đồ công sở nữ thanh lịch mùa hè', 'Gợi ý outfit đi làm'],
  ['1', '2026-08-01 15:00', 'Flash sale giảm 30% áo thun cuối tuần', 'Đẩy đơn cuối tuần'],
  ['2', '2026-08-01 09:00', 'Ra mắt BST linen mới — nhẹ, thoáng', 'Giới thiệu BST'],
]

/** @deprecated */
export const BULK_IDEAS_SAMPLE_HEADER = 'idea'
/** @deprecated */
export const BULK_IDEAS_SAMPLE_ROWS = BULK_CREATE_SAMPLE_ROWS.map((r) => r[2])

function escapeCsvCell(value) {
  const s = String(value ?? '')
  if (/[",\r\n]/.test(s)) return `"${s.replace(/"/g, '""')}"`
  return s
}

export function downloadBulkIdeasSampleCsv() {
  const lines = [
    BULK_CREATE_SAMPLE_HEADER,
    ...BULK_CREATE_SAMPLE_ROWS.map((row) => row.map(escapeCsvCell).join(',')),
  ]
  const blob = new Blob(['\uFEFF' + lines.join('\n')], { type: 'text/csv;charset=utf-8' })
  const url = URL.createObjectURL(blob)
  const a = document.createElement('a')
  a.href = url
  a.download = 'bulk-import-sample.csv'
  a.click()
  URL.revokeObjectURL(url)
}

/** Parse một dòng CSV, hỗ trợ field trong dấu "..." và "" escape. */
export function parseCsvLine(line) {
  const cells = []
  let i = 0
  const s = String(line ?? '')
  while (i < s.length) {
    if (s[i] === '"') {
      let out = ''
      i += 1
      while (i < s.length) {
        if (s[i] === '"' && s[i + 1] === '"') {
          out += '"'
          i += 2
          continue
        }
        if (s[i] === '"') {
          i += 1
          break
        }
        out += s[i]
        i += 1
      }
      cells.push(out.trim())
      if (s[i] === ',') i += 1
      continue
    }
    const comma = s.indexOf(',', i)
    if (comma < 0) {
      cells.push(s.slice(i).trim())
      break
    }
    cells.push(s.slice(i, comma).trim())
    i = comma + 1
  }
  return cells
}

/**
 * Parse file map: page_code,page_id,page_name
 * @returns {Record<string, { pageId: string, pageName?: string }>}
 */
export function parseBulkPageMapFile(text) {
  const raw = String(text || '').replace(/^\uFEFF/, '')
  const lines = raw.split(/\r?\n/).map((l) => l.trim()).filter(Boolean)
  if (lines.length === 0) return {}

  const rows = lines.map(parseCsvLine)
  const header = rows[0].map((c) => c.toLowerCase())
  let start = 0
  let codeIdx = 0
  let idIdx = 1
  let nameIdx = 2

  if (header.some((h) => PAGE_CODE_HEADERS.has(h) || PAGE_ID_HEADERS.has(h))) {
    codeIdx = header.findIndex((h) => PAGE_CODE_HEADERS.has(h))
    idIdx = header.findIndex((h) => PAGE_ID_HEADERS.has(h))
    nameIdx = header.findIndex((h) => h === 'page_name' || h === 'name' || h === 'tên page')
    if (codeIdx < 0) codeIdx = 0
    if (idIdx < 0) idIdx = 1
    start = 1
  }

  const byCode = {}
  for (let i = start; i < rows.length; i += 1) {
    const code = String(rows[i][codeIdx] ?? '').trim()
    const pageId = String(rows[i][idIdx] ?? '').trim()
    if (!code || !pageId) continue
    byCode[code] = {
      pageId,
      pageName: nameIdx >= 0 ? String(rows[i][nameIdx] ?? '').trim() : '',
    }
  }
  return byCode
}

/**
 * Parse CSV import hàng loạt (1 dòng = 1 bài / 1 page).
 * @param {string} text
 * @param {{ mapByCode?: Record<string, { pageId: string }> | null }} [opts]
 * @returns {{
 *   mode: 'import' | 'ideas',
 *   rows: Array<{ idea: string, objective?: string, pageId?: string, pageCode?: string, scheduledAtLocal?: string }>,
 *   unknownCodes: string[],
 * }}
 */
export function parseBulkImportFile(text, opts = {}) {
  const raw = String(text || '').replace(/^\uFEFF/, '')
  const lines = raw.split(/\r?\n/).map((l) => l.trim()).filter(Boolean)
  if (lines.length === 0) {
    return { mode: 'ideas', rows: [], unknownCodes: [] }
  }

  const table = lines.map(parseCsvLine)
  const header = table[0].map((c) => c.toLowerCase())
  const hasIdea = header.some((h) => IDEA_HEADERS.has(h))
  const hasPageCode = header.some((h) => PAGE_CODE_HEADERS.has(h))
  const hasPageId = header.some((h) => PAGE_ID_HEADERS.has(h))
  const hasSchedule = header.some((h) => SCHEDULE_HEADERS.has(h))

  const stored = loadPageMapFromStorage()
  const mapByCode = opts.mapByCode || stored?.byCode || {}

  // File có page_code / page_id → chế độ import 1-1
  if (hasIdea && (hasPageCode || hasPageId)) {
    const ideaIdx = header.findIndex((h) => IDEA_HEADERS.has(h))
    const objectiveIdx = header.findIndex((h) => OBJECTIVE_HEADERS.has(h))
    const codeIdx = header.findIndex((h) => PAGE_CODE_HEADERS.has(h))
    const idIdx = header.findIndex((h) => PAGE_ID_HEADERS.has(h))
    const scheduleIdx = header.findIndex((h) => SCHEDULE_HEADERS.has(h))

    const rows = []
    const unknownCodes = []
    const seenUnknown = new Set()

    for (let i = 1; i < table.length; i += 1) {
      const idea = String(table[i][ideaIdx] ?? '').trim()
      if (!idea) continue

      let pageId = idIdx >= 0 ? String(table[i][idIdx] ?? '').trim() : ''
      const pageCode = codeIdx >= 0 ? String(table[i][codeIdx] ?? '').trim() : ''

      if (!pageId && pageCode) {
        const hit = mapByCode[pageCode]
        if (hit?.pageId) pageId = hit.pageId
        else if (!seenUnknown.has(pageCode)) {
          seenUnknown.add(pageCode)
          unknownCodes.push(pageCode)
        }
      }

      const scheduledAtLocal =
        scheduleIdx >= 0 ? String(table[i][scheduleIdx] ?? '').trim() : ''
      const objective =
        objectiveIdx >= 0 ? String(table[i][objectiveIdx] ?? '').trim() : ''

      rows.push({
        idea,
        ...(objective ? { objective } : {}),
        ...(pageId ? { pageId } : {}),
        ...(pageCode ? { pageCode } : {}),
        ...(scheduledAtLocal ? { scheduledAtLocal } : {}),
      })
    }

    return { mode: 'import', rows, unknownCodes }
  }

  // Legacy: chỉ ý tưởng (fan-out UI)
  let start = 0
  let ideaIdx = 0
  let objectiveIdx = -1
  if (hasIdea) {
    ideaIdx = header.findIndex((h) => IDEA_HEADERS.has(h))
    objectiveIdx = header.findIndex((h) => OBJECTIVE_HEADERS.has(h))
    start = 1
  } else if (hasSchedule || hasPageCode) {
    // Header lạ nhưng không có idea — bỏ
    return { mode: 'ideas', rows: [], unknownCodes: [] }
  }

  const rows = []
  for (let i = start; i < table.length; i += 1) {
    const idea = String(table[i][ideaIdx] ?? '').trim()
    if (!idea) continue
    const objective =
      objectiveIdx >= 0 ? String(table[i][objectiveIdx] ?? '').trim() : ''
    rows.push(objective ? { idea, objective } : { idea })
  }
  return { mode: 'ideas', rows, unknownCodes: [] }
}

/**
 * @deprecated dùng parseBulkImportFile
 * @returns {{ idea: string, objective?: string }[]}
 */
export function parseBulkCreateFile(text) {
  return parseBulkImportFile(text).rows.map((r) =>
    r.objective ? { idea: r.idea, objective: r.objective } : { idea: r.idea },
  )
}

/** @deprecated */
export function parseBulkIdeasFile(text) {
  return parseBulkCreateFile(text).map((item) => item.idea)
}
