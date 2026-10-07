// Thứ tự hiển thị Page/kênh (R-028): tên chứa "vni" (không phân biệt hoa/thường) lên trước,
// trong mỗi nhóm A→Z theo tên (so sánh tiếng Việt) rồi theo Id. Phải khớp thứ tự backend.
const collator = new Intl.Collator('vi')

const defaultGetName = (channel) => channel?.pageName || channel?.name || ''

export function isVniChannelName(name) {
  return String(name ?? '').toLowerCase().includes('vni')
}

/**
 * Trả về BẢN SAO đã sắp xếp, không đổi mảng đầu vào. `getName` cho phép sắp theo nhãn đang hiển thị
 * khi phần tử không phải kênh (vd. PageContext hiển thị bằng tên kênh).
 */
export function sortChannelsVniFirst(channels, getName = defaultGetName) {
  const keyed = (channels ?? []).map((channel) => {
    const name = String(getName(channel) ?? '')
    return { channel, name, group: isVniChannelName(name) ? 0 : 1 }
  })
  keyed.sort((a, b) => (
    a.group - b.group
    || collator.compare(a.name, b.name)
    || collator.compare(String(a.channel?.id ?? ''), String(b.channel?.id ?? ''))
  ))
  return keyed.map((entry) => entry.channel)
}
