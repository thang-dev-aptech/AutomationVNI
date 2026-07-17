import { useEffect, useState } from 'react'
import Modal from '@/shared/components/Modal'
import { SOCIAL_PLATFORM_OPTIONS } from '../constants/socialPlatform'

function toDatetimeLocalValue(isoString) {
  if (!isoString) return ''
  const date = new Date(isoString)
  if (Number.isNaN(date.getTime())) return ''
  const pad = (n) => String(n).padStart(2, '0')
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}T${pad(date.getHours())}:${pad(date.getMinutes())}`
}

const emptyForm = {
  platform: '1',
  pageName: '',
  externalPageId: '',
  accessToken: '',
  tokenExpiresAt: '',
  isActive: true,
}

export default function SocialChannelFormModal({
  open,
  onClose,
  initialData,
  defaultPlatform,
  onSubmit,
  isSubmitting,
  errorMessage,
}) {
  const [form, setForm] = useState(emptyForm)
  const isEdit = Boolean(initialData?.id)

  useEffect(() => {
    if (!open) return
    setForm(
      initialData
        ? {
            platform: String(initialData.platform),
            pageName: initialData.pageName || '',
            externalPageId: initialData.externalPageId || '',
            accessToken: '',
            tokenExpiresAt: toDatetimeLocalValue(initialData.tokenExpiresAt),
            isActive: initialData.isActive ?? true,
          }
        : {
            ...emptyForm,
            platform: defaultPlatform ? String(defaultPlatform) : emptyForm.platform,
          },
    )
  }, [open, initialData, defaultPlatform])

  const handleChange = (field) => (event) => {
    const value = event.target.type === 'checkbox'
      ? event.target.checked
      : event.target.value
    setForm((prev) => ({ ...prev, [field]: value }))
  }

  const handleSubmit = (event) => {
    event.preventDefault()

    if (!isEdit && !form.accessToken.trim()) {
      return
    }

    if (isEdit) {
      const payload = {
        pageName: form.pageName.trim(),
        isActive: form.isActive,
        tokenExpiresAt: form.tokenExpiresAt
          ? new Date(form.tokenExpiresAt).toISOString()
          : null,
      }
      if (form.accessToken.trim()) {
        payload.accessToken = form.accessToken.trim()
      }
      onSubmit(payload)
      return
    }

    onSubmit({
      platform: Number(form.platform),
      pageName: form.pageName.trim(),
      externalPageId: form.externalPageId.trim(),
      accessToken: form.accessToken.trim(),
      isActive: form.isActive,
      tokenExpiresAt: form.tokenExpiresAt
        ? new Date(form.tokenExpiresAt).toISOString()
        : null,
    })
  }

  return (
    <Modal
      open={open}
      title={isEdit ? 'Cập nhật kênh MXH' : 'Kết nối kênh MXH'}
      onClose={onClose}
      footer={(
        <>
          <button type="button" className="btn btn-secondary" onClick={onClose}>
            Hủy
          </button>
          <button
            type="submit"
            form="social-channel-form"
            className="btn btn-primary"
            disabled={isSubmitting}
          >
            {isSubmitting ? 'Đang lưu...' : 'Lưu'}
          </button>
        </>
      )}
    >
      <form id="social-channel-form" onSubmit={handleSubmit}>
        {errorMessage && <div className="alert alert-error">{errorMessage}</div>}

        <div className="form-group">
          <label htmlFor="channel-platform">Nền tảng</label>
          <select
            id="channel-platform"
            value={form.platform}
            onChange={handleChange('platform')}
            disabled={isEdit}
            required
          >
            {SOCIAL_PLATFORM_OPTIONS.map((option) => (
              <option key={option.value} value={option.value}>
                {option.label}
              </option>
            ))}
          </select>
        </div>

        <div className="form-group">
          <label htmlFor="channel-page-name">Tên page</label>
          <input
            id="channel-page-name"
            value={form.pageName}
            onChange={handleChange('pageName')}
            required
          />
        </div>

        {!isEdit && (
          <div className="form-group">
            <label htmlFor="channel-external-id">External Page ID</label>
            <input
              id="channel-external-id"
              value={form.externalPageId}
              onChange={handleChange('externalPageId')}
              required
            />
          </div>
        )}

        {isEdit && (
          <div className="form-group">
            <label htmlFor="channel-external-id-readonly">External Page ID</label>
            <input
              id="channel-external-id-readonly"
              value={form.externalPageId}
              disabled
            />
          </div>
        )}

        <div className="form-group">
          <label htmlFor="channel-token-expires">Token hết hạn (tuỳ chọn)</label>
          <input
            id="channel-token-expires"
            type="datetime-local"
            value={form.tokenExpiresAt}
            onChange={handleChange('tokenExpiresAt')}
          />
        </div>

        <div className="form-group">
          <label htmlFor="channel-access-token">
            Access Token {isEdit ? '(để trống nếu không đổi)' : ''}
          </label>
          <input
            id="channel-access-token"
            type="password"
            value={form.accessToken}
            onChange={handleChange('accessToken')}
            required={!isEdit}
            autoComplete="off"
            placeholder={isEdit ? '••••••••' : ''}
          />
        </div>

        <div className="form-group">
          <label>
            <input
              type="checkbox"
              checked={form.isActive}
              onChange={handleChange('isActive')}
            />
            {' '}Kênh đang hoạt động
          </label>
        </div>
      </form>
    </Modal>
  )
}
