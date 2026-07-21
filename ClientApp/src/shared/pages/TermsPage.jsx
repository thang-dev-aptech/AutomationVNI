import './DataDeletionPage.css'

const SUPPORT_EMAIL = 'support@phattan.xyz'

export default function TermsPage() {
  return (
    <main className="legal-page">
      <article className="legal-card card">
        <header className="legal-header">
          <img className="legal-logo" src="/vni-logo.png" alt="VNI Education" />
          <h1 className="legal-title">Điều khoản dịch vụ</h1>
          <p className="legal-subtitle">
            Terms of Service — VNI Automation · Cập nhật ngày 21/07/2026
          </p>
        </header>

        <section className="legal-section">
          <p>
            Khi truy cập hoặc sử dụng VNI Automation, bạn đồng ý với các điều
            khoản dưới đây. Nếu không đồng ý, vui lòng không sử dụng dịch vụ.
          </p>
        </section>

        <section className="legal-section">
          <h2>1. Điều kiện sử dụng</h2>
          <p>
            Bạn phải có đủ năng lực pháp lý để chấp nhận điều khoản và có quyền
            hợp lệ đối với các tài khoản, Page, kênh và nội dung được kết nối.
            Bạn chịu trách nhiệm bảo mật thông tin đăng nhập và mọi hoạt động
            phát sinh từ tài khoản của mình.
          </p>
        </section>

        <section className="legal-section">
          <h2>2. Phạm vi dịch vụ</h2>
          <p>
            VNI Automation hỗ trợ tạo, quản lý, lên lịch và đăng nội dung; đồng
            thời hỗ trợ quản lý tương tác trên các nền tảng được tích hợp. Tính
            năng có thể phụ thuộc vào API, quyền truy cập và chính sách của nền
            tảng bên thứ ba như Meta.
          </p>
        </section>

        <section className="legal-section">
          <h2>3. Sử dụng được phép</h2>
          <p>Bạn không được sử dụng dịch vụ để:</p>
          <ul>
            <li>Vi phạm pháp luật, quyền sở hữu trí tuệ hoặc quyền riêng tư.</li>
            <li>Đăng nội dung lừa đảo, độc hại, quấy rối hoặc trái phép.</li>
            <li>Gửi spam, thao túng tương tác hoặc né tránh giới hạn nền tảng.</li>
            <li>Truy cập trái phép, phá hoại hoặc gây quá tải hệ thống.</li>
            <li>Chia sẻ hoặc khai thác token và thông tin đăng nhập của người khác.</li>
          </ul>
        </section>

        <section className="legal-section">
          <h2>4. Nội dung và trách nhiệm của người dùng</h2>
          <p>
            Bạn giữ quyền đối với nội dung của mình và cho phép hệ thống xử lý
            nội dung trong phạm vi cần thiết để cung cấp tính năng đã yêu cầu.
            Bạn chịu trách nhiệm kiểm tra nội dung, quyền sử dụng media và kết
            quả do AI hỗ trợ trước khi xuất bản.
          </p>
        </section>

        <section className="legal-section">
          <h2>5. Dịch vụ bên thứ ba</h2>
          <p>
            Việc kết nối Facebook, Instagram hoặc Threads còn chịu điều khoản
            và chính sách của Meta. Chúng tôi không kiểm soát sự sẵn sàng, thay
            đổi API, giới hạn hoặc quyết định của các nền tảng này.
          </p>
        </section>

        <section className="legal-section">
          <h2>6. Tạm ngừng và chấm dứt</h2>
          <p>
            Bạn có thể ngừng sử dụng hoặc ngắt kết nối tài khoản bất kỳ lúc nào.
            Chúng tôi có thể hạn chế hoặc chấm dứt quyền truy cập khi phát hiện
            vi phạm, rủi ro bảo mật hoặc khi pháp luật yêu cầu.
          </p>
        </section>

        <section className="legal-section">
          <h2>7. Giới hạn trách nhiệm</h2>
          <p>
            Dịch vụ được cung cấp theo hiện trạng trong phạm vi pháp luật cho
            phép. Chúng tôi không bảo đảm dịch vụ luôn liên tục hoặc không có
            lỗi, và không chịu trách nhiệm cho gián đoạn hoặc thay đổi từ nền
            tảng bên thứ ba. Không nội dung nào tại đây loại trừ trách nhiệm mà
            pháp luật không cho phép loại trừ.
          </p>
        </section>

        <section className="legal-section">
          <h2>8. Thay đổi điều khoản</h2>
          <p>
            Điều khoản có thể được cập nhật khi dịch vụ hoặc quy định thay đổi.
            Việc tiếp tục sử dụng sau khi bản mới được công bố đồng nghĩa bạn
            chấp nhận các thay đổi đó.
          </p>
        </section>

        <footer className="legal-footer">
          <p>
            Câu hỏi về điều khoản:{' '}
            <a href={`mailto:${SUPPORT_EMAIL}`}>{SUPPORT_EMAIL}</a>.
          </p>
          <p>
            Xem thêm <a href="/privacy">Chính sách quyền riêng tư</a> và{' '}
            <a href="/data-deletion">Hướng dẫn xóa dữ liệu</a>.
          </p>
        </footer>
      </article>
    </main>
  )
}
