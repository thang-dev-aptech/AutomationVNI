import '@/modules/media/components/GoogleDrivePipelineSwitch.css'
import './CampaignRunSwitch.css'

/**
 * Slider bật/tắt chiến dịch trên dòng bảng (Running ↔ Paused).
 */
export default function CampaignRunSwitch({
  checked = false,
  disabled = false,
  loading = false,
  onChange,
  title,
  'aria-label': ariaLabel,
}) {
  const handleChange = (event) => {
    if (disabled || loading) return
    onChange?.(event.target.checked)
  }

  return (
    <label
      className={`gdrive-pipeline-switch campaign-run-switch${disabled || loading ? ' is-disabled' : ''}${checked ? ' is-on' : ''}`}
      title={title}
      data-testid="campaign-run-switch"
      onClick={(e) => e.stopPropagation()}
    >
      <input
        type="checkbox"
        className="gdrive-pipeline-switch-input"
        role="switch"
        aria-label={ariaLabel || title || (checked ? 'Đang chạy' : 'Đã tạm dừng')}
        aria-checked={checked}
        checked={checked}
        disabled={disabled || loading}
        onChange={handleChange}
      />
      <span className="gdrive-pipeline-switch-track" aria-hidden="true">
        <span className="gdrive-pipeline-switch-thumb" />
      </span>
    </label>
  )
}
