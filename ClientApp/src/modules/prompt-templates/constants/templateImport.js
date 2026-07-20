/** Prompt dùng với Claude để sinh file JSON import template. */
export const CLAUDE_TEMPLATE_PROMPT = `Bạn là chuyên gia content marketing + prompt engineering cho hệ thống đăng bài tự động (Facebook/Instagram Page).

Nhiệm vụ: tạo bộ TEMPLATE THEO DANH MỤC cho nhiều ngành hàng Việt Nam, mỗi danh mục = 1 object JSON có đủ prompt text + prompt ảnh.

## Output (BẮT BUỘC)
Chỉ trả về JSON array hợp lệ, không markdown, không giải thích. Schema mỗi phần tử:

{
  "name": "Tên danh mục ngắn (VD: Bán hàng)",
  "description": "1 câu mô tả khi nào dùng danh mục này",
  "textBody": "Prompt sinh caption/bài viết, tiếng Việt, có dùng biến {{...}}",
  "imageBody": "Prompt sinh ảnh minh họa, tiếng Việt hoặc Anh rõ ràng, có dùng biến {{...}}",
  "isDefault": false,
  "isActive": true
}

## Biến được phép trong textBody / imageBody
{{title}} {{category}} {{brand}} {{tone}} {{audience}} {{cta}} {{hashtags}} {{caption}} {{imagePrompt}}

Quy tắc:
- textBody: hướng dẫn AI viết bài đăng MXH (hook, lợi ích, CTA, hashtag gợi ý). Luôn nhắc dùng {{title}} làm ý tưởng chính.
- imageBody: mô tả ảnh cần sinh (chủ thể, bố cục, phong cách, tránh chữ quá dày trên ảnh). Có thể tham chiếu {{title}}, {{brand}}, {{caption}}.
- Mỗi danh mục giọng điệu / góc bán khác nhau, không copy-paste giống nhau.
- Chỉ 1 item được "isDefault": true (danh mục phổ biến nhất, VD Branding hoặc Bán hàng).
- Tạo 12–20 danh mục bao quát: bán lẻ thời trang, F&B, làm đẹp/spa, bất động sản, giáo dục/khóa học, tuyển dụng, sự kiện, khuyến mãi flash sale, review sản phẩm, behind-the-scenes, CSR/thiện nguyện, B2B dịch vụ, công nghệ/app, du lịch/homestay, mẹ & bé, thể thao/gym, thú cưng, nông sản/thực phẩm sạch.

## Ví dụ 1 phần tử (tham khảo format, đừng copy nguyên)
{
  "name": "Bán hàng",
  "description": "Đăng bán / đẩy đơn sản phẩm",
  "textBody": "Viết bài đăng bán hàng tiếng Việt cho ý tưởng {{title}}, danh mục {{category}}. Thương hiệu {{brand}}, giọng {{tone}}. Có hook 1 câu, 3 lợi ích, CTA {{cta}}, gợi ý hashtag {{hashtags}}. Không bịa giá nếu chưa có trong ý tưởng.",
  "imageBody": "Create a clean product-focused social image for {{title}}, brand {{brand}}, modern Vietnamese e-commerce style, soft lighting, no cluttered text overlay.",
  "isDefault": true,
  "isActive": true
}

Hãy xuất toàn bộ array JSON ngay.`

export const SAMPLE_TEMPLATES_JSON = [
  {
    name: 'Bán hàng',
    description: 'Đăng bán / đẩy đơn sản phẩm',
    textBody:
      'Viết bài đăng bán hàng tiếng Việt cho ý tưởng {{title}}, danh mục {{category}}. Thương hiệu {{brand}}, giọng {{tone}}. Có hook, 3 lợi ích, CTA {{cta}}, hashtag {{hashtags}}.',
    imageBody:
      'Create a clean product social image for {{title}}, brand {{brand}}, modern e-commerce style, soft lighting, minimal text.',
    isDefault: true,
    isActive: true,
  },
  {
    name: 'Tuyển dụng',
    description: 'Đăng tin tuyển dụng / employer branding',
    textBody:
      'Viết bài tuyển dụng tiếng Việt từ ý tưởng {{title}}. Thương hiệu {{brand}}, giọng {{tone}}. Nêu vị trí, 3 lý do ứng tuyển, cách ứng tuyển qua CTA {{cta}}.',
    imageBody:
      'Professional hiring social graphic for {{title}}, brand {{brand}}, friendly workplace vibe, clear focal subject, no dense paragraphs on image.',
    isDefault: false,
    isActive: true,
  },
]

/** CSV header — bodies nên đặt trong dấu "..." nếu có dấu phẩy. */
export const SAMPLE_CSV_HEADER =
  'name,description,textBody,imageBody,isDefault,isActive'

export function downloadSampleJson() {
  const blob = new Blob([JSON.stringify(SAMPLE_TEMPLATES_JSON, null, 2)], {
    type: 'application/json;charset=utf-8',
  })
  const url = URL.createObjectURL(blob)
  const a = document.createElement('a')
  a.href = url
  a.download = 'prompt-templates-sample.json'
  a.click()
  URL.revokeObjectURL(url)
}

export function downloadSampleCsv() {
  const rows = [
    SAMPLE_CSV_HEADER,
    ...SAMPLE_TEMPLATES_JSON.map((t) =>
      [
        csvEscape(t.name),
        csvEscape(t.description),
        csvEscape(t.textBody),
        csvEscape(t.imageBody),
        t.isDefault ? 'true' : 'false',
        t.isActive ? 'true' : 'false',
      ].join(','),
    ),
  ]
  const blob = new Blob([rows.join('\n')], { type: 'text/csv;charset=utf-8' })
  const url = URL.createObjectURL(blob)
  const a = document.createElement('a')
  a.href = url
  a.download = 'prompt-templates-sample.csv'
  a.click()
  URL.revokeObjectURL(url)
}

function csvEscape(value) {
  const s = String(value ?? '')
  if (/[",\n\r]/.test(s)) return `"${s.replace(/"/g, '""')}"`
  return s
}

/**
 * Parse uploaded file text into CreatePromptTemplateRequest[].
 * Supports JSON array or CSV with header.
 */
export function parseTemplateUploadFile(fileName, text) {
  const trimmed = text.trim()
  if (!trimmed) throw new Error('File trống')

  const lower = (fileName || '').toLowerCase()
  if (lower.endsWith('.json') || trimmed.startsWith('[')) {
    const data = JSON.parse(trimmed)
    const list = Array.isArray(data) ? data : data.items
    if (!Array.isArray(list)) throw new Error('JSON phải là array các template')
    return list.map(normalizeItem)
  }

  return parseCsvTemplates(trimmed)
}

function normalizeItem(raw, index = 0) {
  const name = String(raw.name ?? raw.Name ?? '').trim()
  const textBody = String(raw.textBody ?? raw.TextBody ?? '').trim()
  const imageBody = String(raw.imageBody ?? raw.ImageBody ?? '').trim()
  if (!name) throw new Error(`Dòng/item ${index + 1}: thiếu name`)
  if (!textBody) throw new Error(`"${name}": thiếu textBody`)
  if (!imageBody) throw new Error(`"${name}": thiếu imageBody`)
  return {
    name,
    description: String(raw.description ?? raw.Description ?? '').trim() || null,
    textBody,
    imageBody,
    isDefault: parseBool(raw.isDefault ?? raw.IsDefault, false),
    isActive: parseBool(raw.isActive ?? raw.IsActive, true),
  }
}

function parseBool(value, fallback) {
  if (value === undefined || value === null || value === '') return fallback
  if (typeof value === 'boolean') return value
  const s = String(value).trim().toLowerCase()
  if (['1', 'true', 'yes', 'y'].includes(s)) return true
  if (['0', 'false', 'no', 'n'].includes(s)) return false
  return fallback
}

function parseCsvTemplates(text) {
  const rows = parseCsvRows(text)
  if (rows.length < 2) throw new Error('CSV cần header + ít nhất 1 dòng dữ liệu')

  const header = rows[0].map((h) => h.trim().toLowerCase())
  const idx = (key) => header.indexOf(key)
  const required = ['name', 'textbody', 'imagebody']
  for (const key of required) {
    if (idx(key) < 0) throw new Error(`CSV thiếu cột bắt buộc: ${key}`)
  }

  return rows.slice(1).filter((r) => r.some((c) => c.trim())).map((cols, i) =>
    normalizeItem(
      {
        name: cols[idx('name')],
        description: idx('description') >= 0 ? cols[idx('description')] : '',
        textBody: cols[idx('textbody')],
        imageBody: cols[idx('imagebody')],
        isDefault: idx('isdefault') >= 0 ? cols[idx('isdefault')] : false,
        isActive: idx('isactive') >= 0 ? cols[idx('isactive')] : true,
      },
      i,
    ),
  )
}

/** Minimal RFC4180-ish CSV parser (quoted fields, commas, newlines). */
function parseCsvRows(text) {
  const rows = []
  let row = []
  let cell = ''
  let inQuotes = false

  for (let i = 0; i < text.length; i++) {
    const ch = text[i]
    const next = text[i + 1]
    if (inQuotes) {
      if (ch === '"' && next === '"') {
        cell += '"'
        i++
      } else if (ch === '"') {
        inQuotes = false
      } else {
        cell += ch
      }
      continue
    }
    if (ch === '"') {
      inQuotes = true
      continue
    }
    if (ch === ',') {
      row.push(cell)
      cell = ''
      continue
    }
    if (ch === '\n' || (ch === '\r' && next === '\n')) {
      if (ch === '\r') i++
      row.push(cell)
      rows.push(row)
      row = []
      cell = ''
      continue
    }
    if (ch === '\r') {
      row.push(cell)
      rows.push(row)
      row = []
      cell = ''
      continue
    }
    cell += ch
  }
  row.push(cell)
  if (row.some((c) => c.length)) rows.push(row)
  return rows
}
