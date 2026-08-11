const NUMBER = new Intl.NumberFormat('vi-VN')

/** Số nguyên có dấu phân cách nghìn. null/undefined ra dấu gạch, KHÔNG ra số 0. */
export function num(value) {
  if (value === null || value === undefined) return '—'
  return NUMBER.format(value)
}

/**
 * Biến động kèm dấu và chiều.
 *
 * Trả về null khi chưa có mốc so sánh — giao diện nhờ vậy ẩn hẳn dòng biến động thay vì hiện
 * "▲ 0", thứ trông y hệt "đo được rồi và không đổi". Ngày đầu chạy hệ thống thì mọi page đều
 * chưa có mốc, và "▲ 0" ở khắp nơi làm người xem tưởng page nào cũng đứng im.
 */
export function delta(value) {
  if (value === null || value === undefined) return null
  if (value === 0) return { text: 'không đổi', tone: 'flat', icon: '—' }
  return value > 0
    ? { text: `+${NUMBER.format(value)}`, tone: 'up', icon: '▲' }
    : { text: NUMBER.format(value), tone: 'down', icon: '▼' }
}

/** "3 phút trước", "2 giờ trước", "hôm qua"... Rỗng thì trả "chưa từng". */
export function relativeTime(iso) {
  if (!iso) return 'chưa từng'
  const then = new Date(iso)
  if (Number.isNaN(then.getTime())) return 'chưa từng'

  const mins = Math.floor((Date.now() - then.getTime()) / 60000)
  if (mins < 1) return 'vừa xong'
  if (mins < 60) return `${mins} phút trước`
  const hours = Math.floor(mins / 60)
  if (hours < 24) return `${hours} giờ trước`
  const days = Math.floor(hours / 24)
  if (days === 1) return 'hôm qua'
  if (days < 30) return `${days} ngày trước`
  return then.toLocaleDateString('vi-VN')
}

export function shortDateTime(iso) {
  if (!iso) return '—'
  const d = new Date(iso)
  if (Number.isNaN(d.getTime())) return '—'
  return d.toLocaleString('vi-VN', {
    day: '2-digit', month: '2-digit', hour: '2-digit', minute: '2-digit',
  })
}

/** Cắt chữ ở ranh giới từ, không cắt giữa từ. */
export function excerpt(text, max = 90) {
  if (!text) return '(không có nội dung chữ)'
  const clean = text.replace(/\s+/g, ' ').trim()
  if (clean.length <= max) return clean
  const cut = clean.slice(0, max)
  const lastSpace = cut.lastIndexOf(' ')
  return `${lastSpace > max * 0.6 ? cut.slice(0, lastSpace) : cut}…`
}
