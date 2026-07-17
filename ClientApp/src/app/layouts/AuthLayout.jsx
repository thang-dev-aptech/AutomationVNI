import { Outlet } from 'react-router-dom'
import './AuthLayout.css'

export default function AuthLayout() {
  return (
    <div className="auth-layout">
      <div className="auth-card card">
        <div className="auth-card-body">
          <h1 className="auth-title">VNI Automation</h1>
          <p className="auth-subtitle">Đăng nhập để quản lý nội dung AI</p>
          <Outlet />
        </div>
      </div>
    </div>
  )
}
