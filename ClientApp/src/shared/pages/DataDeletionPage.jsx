import './DataDeletionPage.css'

const SUPPORT_EMAIL = 'support@phattan.xyz'

export default function DataDeletionPage() {
  return (
    <main className="legal-page">
      <div className="legal-card card">
        <header className="legal-header">
          <img className="legal-logo" src="/vni-logo.png" alt="VNI Education" />
          <h1 className="legal-title">Hướng dẫn xóa dữ liệu người dùng</h1>
          <p className="legal-subtitle">
            User Data Deletion Instructions — VNI Automation
          </p>
        </header>

        <section className="legal-section">
          <p>
            VNI Automation kết nối các trang Facebook, tài khoản Instagram
            Business và Threads của bạn để đăng bài, đọc và trả lời bình luận,
            tin nhắn. Chúng tôi lưu trữ token truy cập, thông tin trang/kênh và
            nội dung do bạn tạo. Bạn có toàn quyền yêu cầu xóa các dữ liệu này
            bất kỳ lúc nào.
          </p>
        </section>

        <section className="legal-section">
          <h2>Cách 1 — Tự gỡ kết nối trong ứng dụng</h2>
          <ol>
            <li>
              Đăng nhập vào ứng dụng tại{' '}
              <a href="https://phattan.xyz">phattan.xyz</a>.
            </li>
            <li>
              Vào mục <strong>Platforms</strong> (Nền tảng).
            </li>
            <li>
              Chọn tài khoản Meta / Threads đã kết nối và bấm{' '}
              <strong>Disconnect</strong> (Ngắt kết nối).
            </li>
            <li>
              Thao tác này sẽ xóa token truy cập và toàn bộ kênh (Page,
              Instagram, Group, Threads) liên kết với tài khoản đó khỏi hệ thống.
            </li>
          </ol>
        </section>

        <section className="legal-section">
          <h2>Cách 2 — Gửi yêu cầu xóa qua email</h2>
          <p>
            Nếu bạn không còn truy cập được ứng dụng, hãy gửi email tới{' '}
            <a href={`mailto:${SUPPORT_EMAIL}`}>{SUPPORT_EMAIL}</a> với:
          </p>
          <ul>
            <li>
              Tiêu đề: <strong>Yêu cầu xóa dữ liệu</strong>
            </li>
            <li>Tên và email tài khoản của bạn</li>
            <li>Tên trang Facebook / tài khoản Meta liên quan (nếu có)</li>
          </ul>
          <p>
            Chúng tôi sẽ xử lý và xóa toàn bộ dữ liệu liên quan trong vòng{' '}
            <strong>30 ngày</strong>, sau đó gửi email xác nhận cho bạn.
          </p>
        </section>

        <section className="legal-section">
          <h2>Xóa quyền từ phía Facebook</h2>
          <p>
            Bạn cũng có thể thu hồi quyền truy cập của ứng dụng bất kỳ lúc nào
            tại{' '}
            <a
              href="https://www.facebook.com/settings?tab=business_tools"
              target="_blank"
              rel="noreferrer"
            >
              Facebook → Cài đặt → Business Integrations
            </a>
            . Khi bạn gỡ ứng dụng khỏi Facebook, token của bạn sẽ ngừng hoạt
            động và không còn được sử dụng.
          </p>
        </section>

        <footer className="legal-footer">
          <p>
            Mọi thắc mắc về quyền riêng tư, liên hệ{' '}
            <a href={`mailto:${SUPPORT_EMAIL}`}>{SUPPORT_EMAIL}</a>.
          </p>
        </footer>
      </div>
    </main>
  )
}
