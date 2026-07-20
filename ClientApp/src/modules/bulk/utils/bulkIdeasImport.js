/** File mẫu ý tưởng cho tạo bài hàng loạt. */

export const BULK_IDEAS_SAMPLE_HEADER = 'idea'

export const BULK_IDEAS_SAMPLE_ROWS = [
  'Set đồ công sở nữ thanh lịch mùa hè',
  'Flash sale giảm 30% toàn bộ áo thun cuối tuần này',
  'Ra mắt BST linen mới — nhẹ, thoáng, dễ phối',
  'Tips phối sneakers với quần âu đi làm',
  'Feedback khách: chất vải mềm, form đứng phom',
]

export function downloadBulkIdeasSampleCsv() {
  // BOM để Excel mở UTF-8 đúng tiếng Việt
  const lines = [BULK_IDEAS_SAMPLE_HEADER, ...BULK_IDEAS_SAMPLE_ROWS]
  const blob = new Blob(['\uFEFF' + lines.join('\n')], {
    type: 'text/csv;charset=utf-8',
  })
  const url = URL.createObjectURL(blob)
  const a = document.createElement('a')
  a.href = url
  a.download = 'bulk-ideas-sample.csv'
  a.click()
  URL.revokeObjectURL(url)
}

/**
 * Parse CSV/TXT ý tưởng: cột đầu (idea|title), bỏ header, hỗ trợ quoted field.
 * @returns {string[]}
 */
export function parseBulkIdeasFile(text) {
  const raw = String(text || '').replace(/^\uFEFF/, '')
  const lines = raw.split(/\r?\n/).map((l) => l.trim()).filter(Boolean)
  if (lines.length === 0) return []

  const ideas = []
  for (let i = 0; i < lines.length; i += 1) {
    const firstCell = readFirstCsvCell(lines[i])
    if (!firstCell) continue
    const lower = firstCell.toLowerCase()
    if (i === 0 && (lower === 'idea' || lower === 'title' || lower === 'ý tưởng')) continue
    ideas.push(firstCell)
  }
  return ideas
}

function readFirstCsvCell(line) {
  const s = line.trim()
  if (!s) return ''
  if (s.startsWith('"')) {
    let out = ''
    for (let i = 1; i < s.length; i += 1) {
      if (s[i] === '"' && s[i + 1] === '"') {
        out += '"'
        i += 1
        continue
      }
      if (s[i] === '"') break
      out += s[i]
    }
    return out.trim()
  }
  const comma = s.indexOf(',')
  return (comma < 0 ? s : s.slice(0, comma)).trim()
}
