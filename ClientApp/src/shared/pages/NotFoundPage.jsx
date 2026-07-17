import { Link } from 'react-router-dom'
import PageHeader from '@/shared/components/PageHeader'

export default function NotFoundPage() {
  return (
    <section style={{ minHeight: '60vh', display: 'grid', placeItems: 'center' }}>
      <div className="card card-body" style={{ maxWidth: 480, textAlign: 'center' }}>
        <PageHeader
          title="Không tìm thấy trang"
          description="Đường dẫn bạn truy cập không tồn tại hoặc đã bị di chuyển."
        />
        <div style={{ display: 'flex', gap: 12, justifyContent: 'center' }}>
          <Link to="/dashboard" className="btn btn-primary">
            Về dashboard
          </Link>
          <Link to="/posts" className="btn btn-secondary">
            Xem bài viết
          </Link>
        </div>
      </div>
    </section>
  )
}
