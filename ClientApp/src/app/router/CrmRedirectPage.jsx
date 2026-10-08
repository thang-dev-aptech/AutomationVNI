import { useEffect } from 'react'
import { useLocation } from 'react-router-dom'

export function buildCrmRedirectUrl(crmBaseUrl, defaultKind, search = '') {
  if (!crmBaseUrl) return null
  const searchParams = new URLSearchParams(search)
  if (!searchParams.has('kind') && defaultKind) {
    searchParams.set('kind', defaultKind)
  }
  const queryStr = searchParams.toString()
  if (!queryStr) return crmBaseUrl
  const separator = crmBaseUrl.includes('?') ? '&' : '?'
  return `${crmBaseUrl}${separator}${queryStr}`
}

export default function CrmRedirectPage({ defaultKind }) {
  const location = useLocation()
  const crmUrl = import.meta.env.VITE_CRM_URL

  useEffect(() => {
    if (crmUrl) {
      const targetUrl = buildCrmRedirectUrl(crmUrl, defaultKind, location.search)
      if (typeof window !== 'undefined' && window.location?.assign) {
        window.location.assign(targetUrl)
      }
    }
  }, [crmUrl, defaultKind, location.search])

  if (!crmUrl) {
    return (
      <div className="crm-unconfigured" data-testid="crm-unconfigured" style={{ padding: '2.5rem', textAlign: 'center' }}>
        <h2 style={{ fontSize: '1.25rem', fontWeight: 600, marginBottom: '0.75rem', color: 'var(--text-primary, #1e293b)' }}>
          Chưa cấu hình địa chỉ CRM
        </h2>
        <p style={{ color: 'var(--text-secondary, #64748b)', maxWidth: '480px', margin: '0 auto', lineHeight: 1.5 }}>
          Vui lòng cấu hình biến môi trường <code>VITE_CRM_URL</code> để chuyển tiếp sang hộp thư CRM.
        </p>
      </div>
    )
  }

  return (
    <div className="crm-redirecting" data-testid="crm-redirecting" style={{ padding: '2.5rem', textAlign: 'center', color: 'var(--text-secondary, #64748b)' }}>
      <p>Đang chuyển hướng tới CRM...</p>
    </div>
  )
}
