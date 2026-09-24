import { Outlet, Link } from 'react-router-dom'
import './AuthLayout.css'

export default function AuthLayout() {
  return (
    <div className="auth-layout">
      <div className="auth-card card">
        <div className="auth-card-body">
          <img className="auth-logo" src="/app-mark.svg" alt="" />
          <h1 className="auth-title">Automation</h1>
          <p className="auth-subtitle">Đăng nhập để quản lý nội dung AI</p>
          <Outlet />
        </div>
      </div>
      {/* Luôn hiện sẵn ngoài khung đăng nhập — không cần đăng nhập, không cần mở menu.
          TikTok/Meta App Review yêu cầu link Privacy Policy + Terms of Service phải thấy
          ngay trên trang web nộp review, không được ẩn sau menu hay cổng đăng nhập. */}
      <footer className="auth-footer">
        <Link to="/privacy">Chính sách quyền riêng tư</Link>
        <span aria-hidden="true">·</span>
        <Link to="/terms">Điều khoản dịch vụ</Link>
      </footer>
    </div>
  )
}
