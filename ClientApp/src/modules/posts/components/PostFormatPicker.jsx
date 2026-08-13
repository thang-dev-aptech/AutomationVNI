const OPTIONS = [
  {
    value: false,
    icon: '📄',
    title: 'Bài viết Facebook',
    description: 'Ảnh + chữ như bình thường, đăng thẳng lên newsfeed/trang page.',
  },
  {
    value: true,
    icon: '🎬',
    title: 'Reels (video)',
    description: 'Tự ghép ảnh vừa sinh thành 1 video ngắn rồi đăng dạng Reels — không cần dựng video tay.',
  },
]

/** Bộ chọn Facebook Post vs Reels — dùng chung cho tạo 1 bài và tạo hàng loạt. */
export default function PostFormatPicker({ value, onChange }) {
  return (
    <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(220px, 1fr))', gap: 12 }}>
      {OPTIONS.map((opt) => {
        const selected = value === opt.value
        return (
          <button
            key={String(opt.value)}
            type="button"
            onClick={() => onChange(opt.value)}
            className="card"
            style={{
              textAlign: 'left',
              cursor: 'pointer',
              padding: 16,
              border: selected
                ? (opt.value ? '2px solid #a855f7' : '2px solid var(--color-primary, #2563eb)')
                : '1px solid var(--color-border, #e5e7eb)',
              background: selected
                ? (opt.value ? 'rgba(168, 85, 247, 0.08)' : 'var(--color-primary-bg, rgba(37, 99, 235, 0.06))')
                : 'transparent',
            }}
          >
            <div style={{ fontSize: '1.6rem', marginBottom: 6 }}>{opt.icon}</div>
            <div style={{ fontWeight: 600, marginBottom: 6 }}>{opt.title}</div>
            <div style={{ fontSize: '0.85rem', color: 'var(--text-muted, #888)' }}>{opt.description}</div>
          </button>
        )
      })}
    </div>
  )
}
