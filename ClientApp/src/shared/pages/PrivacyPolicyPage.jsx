import './DataDeletionPage.css'

const SUPPORT_EMAIL = 'support@phattan.xyz'

export default function PrivacyPolicyPage() {
  return (
    <main className="legal-page">
      <article className="legal-card card">
        <header className="legal-header">
          <img className="legal-logo" src="/vni-logo.png" alt="VNI Education" />
          <h1 className="legal-title">Chính sách quyền riêng tư</h1>
          <p className="legal-subtitle">
            Privacy Policy — VNI Automation · Cập nhật ngày 21/07/2026
          </p>
        </header>

        <section className="legal-section">
          <p>
            Chính sách này mô tả cách VNI Automation thu thập, sử dụng, lưu trữ
            và bảo vệ dữ liệu khi bạn sử dụng ứng dụng hoặc kết nối tài khoản
            Facebook, Instagram Business và Threads.
          </p>
        </section>

        <section className="legal-section">
          <h2>1. Dữ liệu chúng tôi thu thập</h2>
          <ul>
            <li>Thông tin tài khoản dùng để đăng nhập VNI Automation.</li>
            <li>
              ID, tên, ảnh đại diện và thông tin các Page, kênh hoặc tài khoản
              mạng xã hội mà bạn cho phép kết nối.
            </li>
            <li>
              Token truy cập và các quyền do bạn cấp thông qua quy trình OAuth
              của Meta.
            </li>
            <li>
              Bài viết, tệp media, bình luận, tin nhắn và dữ liệu tương tác cần
              thiết cho các tính năng bạn chủ động sử dụng.
            </li>
            <li>
              Nhật ký kỹ thuật và thông tin lỗi phục vụ bảo mật, vận hành và hỗ
              trợ người dùng.
            </li>
          </ul>
        </section>

        <section className="legal-section">
          <h2>2. Mục đích sử dụng dữ liệu</h2>
          <ul>
            <li>Kết nối và đồng bộ các kênh do bạn quản lý.</li>
            <li>Tạo, lên lịch và đăng nội dung theo yêu cầu của bạn.</li>
            <li>Hiển thị, quản lý và phản hồi bình luận hoặc tin nhắn.</li>
            <li>Duy trì bảo mật, chẩn đoán lỗi và cải thiện dịch vụ.</li>
            <li>Tuân thủ nghĩa vụ pháp lý và yêu cầu hợp lệ từ cơ quan có thẩm quyền.</li>
          </ul>
          <p>
            Chúng tôi không bán dữ liệu cá nhân hoặc token truy cập của bạn.
          </p>
        </section>

        <section className="legal-section">
          <h2>3. Chia sẻ và nhà cung cấp dịch vụ</h2>
          <p>
            Dữ liệu chỉ được chia sẻ khi cần thiết với Meta và các nhà cung cấp
            hạ tầng, lưu trữ hoặc AI phục vụ chức năng bạn sử dụng. Các bên này
            chỉ được xử lý dữ liệu trong phạm vi cung cấp dịch vụ và theo các
            nghĩa vụ bảo mật áp dụng. Chúng tôi cũng có thể cung cấp dữ liệu khi
            pháp luật yêu cầu.
          </p>
        </section>

        <section className="legal-section">
          <h2>4. Lưu trữ và bảo mật</h2>
          <p>
            Chúng tôi áp dụng các biện pháp kỹ thuật và tổ chức hợp lý để hạn
            chế truy cập trái phép. Dữ liệu được giữ trong thời gian cần thiết
            để cung cấp dịch vụ, giải quyết tranh chấp và tuân thủ pháp luật.
            Không hệ thống truyền hoặc lưu trữ điện tử nào có thể bảo đảm an
            toàn tuyệt đối.
          </p>
        </section>

        <section className="legal-section">
          <h2>5. Quyền và lựa chọn của bạn</h2>
          <p>
            Bạn có thể ngắt kết nối kênh, thu hồi quyền trong cài đặt Meta hoặc
            yêu cầu truy cập, sửa và xóa dữ liệu. Xem{' '}
            <a href="/data-deletion">hướng dẫn xóa dữ liệu</a> để biết chi tiết.
          </p>
        </section>

        <section className="legal-section">
          <h2>6. Thay đổi chính sách</h2>
          <p>
            Chính sách có thể được cập nhật khi dịch vụ hoặc quy định thay đổi.
            Phiên bản mới sẽ được đăng tại trang này cùng ngày cập nhật.
          </p>
        </section>

        <footer className="legal-footer">
          <p>
            Liên hệ về quyền riêng tư:{' '}
            <a href={`mailto:${SUPPORT_EMAIL}`}>{SUPPORT_EMAIL}</a>.
          </p>
        </footer>
      </article>
    </main>
  )
}
