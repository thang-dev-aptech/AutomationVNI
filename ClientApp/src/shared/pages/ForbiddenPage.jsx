import { Link } from 'react-router-dom'
import PageHeader from '@/shared/components/PageHeader'

export default function ForbiddenPage() {
  return (
    <section style={{ minHeight: '60vh', display: 'grid', placeItems: 'center' }}>
      <div className="card card-body" style={{ maxWidth: 480, textAlign: 'center' }}>
        <PageHeader
          title="Không có quyền truy cập"
          description="Bạn không có quyền xem trang hoặc thực hiện thao tác này."
        />
        <p style={{ color: 'var(--color-text-muted)', marginBottom: 20 }}>
          Nếu bạn cho rằng đây là nhầm lẫn, hãy liên hệ quản trị viên.
        </p>
        <Link to="/dashboard" className="btn btn-primary">
          Về trang chủ
        </Link>
      </div>
    </section>
  )
}
