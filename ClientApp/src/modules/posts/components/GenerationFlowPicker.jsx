import { FEATURES } from '@/shared/config/features'

const OPTIONS = [
  {
    value: 'fullai',
    icon: '🎨',
    title: 'Sinh toàn bộ bằng AI',
    description: 'AI viết nội dung và tự vẽ ảnh banner mới hoàn toàn (Full AI).',
    enabled: () => FEATURES.aiFullImage,
  },
  {
    value: 'template',
    icon: '🖼️',
    title: 'AI sinh text, ghép vào ảnh mẫu',
    description:
      'AI viết nội dung, hệ thống tự chọn 1 (hoặc nhiều) ảnh Template có sẵn của page rồi ghép chữ đè lên. '
      + 'Page cần có thư mục Media gắn sẵn cho page này (Media > Thư mục).',
  },
  {
    value: 'media',
    icon: '🖼️',
    title: 'Dùng ảnh có sẵn trong Media',
    description: 'Đăng ảnh đã có, không sinh ảnh mới.',
  },
]

/**
 * Các phương pháp đang hiện — đọc cờ lúc gọi để test bật/tắt được.
 * `allowed`: giới hạn theo trang (vd. Tạo hàng loạt không hỗ trợ 'media'); bỏ trống = tất cả.
 */
export function getVisibleGenerationFlows(allowed) {
  return OPTIONS.filter((opt) => (!allowed || allowed.includes(opt.value)) && (!opt.enabled || opt.enabled()))
}

export default function GenerationFlowPicker({ value, onChange, allowed }) {
  return (
    <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(220px, 1fr))', gap: 12 }}>
      {getVisibleGenerationFlows(allowed).map((opt) => {
        const selected = value === opt.value
        return (
          <button
            key={opt.value}
            type="button"
            onClick={() => onChange(opt.value)}
            className="card"
            style={{
              textAlign: 'left',
              cursor: 'pointer',
              padding: 16,
              border: selected ? '2px solid var(--color-primary, #2563eb)' : '1px solid var(--color-border, #e5e7eb)',
              background: selected ? 'var(--color-primary-bg, rgba(37, 99, 235, 0.06))' : 'transparent',
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
