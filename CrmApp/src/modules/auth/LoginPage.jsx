import React, { useState } from 'react'
import { useNavigate, useLocation } from 'react-router-dom'
import { useAuth } from '../../auth/useAuth'
import Button from '../../shared/components/Button'
import './LoginPage.css'

export const LoginPage = () => {
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [errorMsg, setErrorMsg] = useState('')
  const [submitting, setSubmitting] = useState(false)

  const { login } = useAuth()
  const navigate = useNavigate()
  const location = useLocation()
  const from = location.state?.from?.pathname || '/inbox'

  const handleSubmit = async (e) => {
    e.preventDefault()
    setErrorMsg('')

    if (!email.trim() || !password) {
      setErrorMsg('Vui lòng nhập đầy đủ email và mật khẩu')
      return
    }

    setSubmitting(true)
    try {
      await login({ email: email.trim(), password })
      navigate(from, { replace: true })
    } catch (err) {
      const msg =
        err?.response?.data?.message ||
        err?.message ||
        'Đăng nhập không thành công. Vui lòng kiểm tra lại thông tin.'
      setErrorMsg(msg)
    } finally {
      setSubmitting(false)
    }
  }

  return (
    <div className="crm-login-container">
      <div className="crm-login-card">
        <div className="crm-login-header">
          <div className="crm-login-logo">CRM</div>
          <h2 className="crm-login-title">Đăng nhập VNI CRM</h2>
          <p className="crm-login-subtitle">Hộp thư hợp nhất & Quản lý khách hàng</p>
        </div>

        {errorMsg && (
          <div className="crm-alert-error" role="alert" data-testid="login-error-alert">
            <span>{errorMsg}</span>
          </div>
        )}

        <form onSubmit={handleSubmit} data-testid="login-form">
          <div className="crm-form-group">
            <label className="crm-form-label" htmlFor="email-input">
              Email tài khoản
            </label>
            <input
              id="email-input"
              data-testid="email-input"
              type="email"
              className="crm-form-input"
              placeholder="admin@vni.local hoặc nhân viên..."
              value={email}
              onChange={(e) => setEmail(e.target.value)}
              disabled={submitting}
              autoComplete="username"
              required
            />
          </div>

          <div className="crm-form-group">
            <label className="crm-form-label" htmlFor="password-input">
              Mật khẩu
            </label>
            <input
              id="password-input"
              data-testid="password-input"
              type="password"
              className="crm-form-input"
              placeholder="Nhập mật khẩu..."
              value={password}
              onChange={(e) => setPassword(e.target.value)}
              disabled={submitting}
              autoComplete="current-password"
              required
            />
          </div>

          <Button
            type="submit"
            variant="primary"
            size="lg"
            style={{ width: '100%', marginTop: '8px' }}
            isLoading={submitting}
            data-testid="login-submit-btn"
          >
            Đăng nhập hệ thống
          </Button>
        </form>
      </div>
    </div>
  )
}

export default LoginPage
