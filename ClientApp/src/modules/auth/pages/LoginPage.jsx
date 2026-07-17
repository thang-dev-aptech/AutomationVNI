import { useState } from 'react'
import { useNavigate, useLocation } from 'react-router-dom'
import { getErrorMessage } from '@/shared/utils/apiHelpers'
import { useLogin } from '../hooks/useAuth'

export default function LoginPage() {
  const navigate = useNavigate()
  const location = useLocation()
  const loginMutation = useLogin()
  const [email, setEmail] = useState('admin@vni.local')
  const [password, setPassword] = useState('')
  const [errorMessage, setErrorMessage] = useState('')

  const redirectTo = location.state?.from || '/dashboard'

  const handleSubmit = async (event) => {
    event.preventDefault()
    setErrorMessage('')
    try {
      await loginMutation.mutateAsync({ email, password })
      navigate(redirectTo, { replace: true })
    } catch (error) {
      setErrorMessage(getErrorMessage(error, 'Đăng nhập thất bại'))
    }
  }

  return (
    <form onSubmit={handleSubmit}>
      {errorMessage && <div className="alert alert-error">{errorMessage}</div>}
      <div className="form-group">
        <label htmlFor="login-email">Email</label>
        <input
          id="login-email"
          type="email"
          value={email}
          onChange={(event) => setEmail(event.target.value)}
          required
          autoComplete="username"
        />
      </div>
      <div className="form-group">
        <label htmlFor="login-password">Mật khẩu</label>
        <input
          id="login-password"
          type="password"
          value={password}
          onChange={(event) => setPassword(event.target.value)}
          required
          autoComplete="current-password"
        />
      </div>
      <button
        type="submit"
        className="btn btn-primary"
        style={{ width: '100%' }}
        disabled={loginMutation.isPending}
      >
        {loginMutation.isPending ? 'Đang đăng nhập...' : 'Đăng nhập'}
      </button>
    </form>
  )
}
