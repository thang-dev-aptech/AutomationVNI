import './GoogleDrivePipelineSwitch.css'

/**
 * GDRIVE-04: iOS-style toggle — thuần markup/CSS.
 * onChange được gọi khi user bấm; logic confirm/API ở parent (handleToggleGoogleDrive).
 */
export default function GoogleDrivePipelineSwitch({
  checked = false,
  disabled = false,
  loading = false,
  onChange,
  label,
  title,
}) {
  const handleChange = (event) => {
    if (disabled || loading) return
    onChange?.(event.target.checked)
  }

  return (
    <label
      className={`gdrive-pipeline-switch${disabled || loading ? ' is-disabled' : ''}${checked ? ' is-on' : ''}`}
      title={title}
    >
      <span className="gdrive-pipeline-switch-label">
        {loading ? 'Đang cập nhật…' : (label ?? (checked ? 'Google Drive: đang bật' : 'Google Drive: đang tắt'))}
      </span>
      <input
        type="checkbox"
        className="gdrive-pipeline-switch-input"
        role="switch"
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
