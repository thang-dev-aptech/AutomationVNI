/** Enum backend là số nguyên — mirror bằng tay như các module khác. */

export const CRAWLED_ARTICLE_STATUS = {
  NEW: 1,
  DEDUPING: 2,
  REWRITING: 3,
  PENDING: 4,
  DUPLICATE: 5,
  FILTERED: 6,
  APPROVED: 7,
  REJECTED: 8,
  FAILED: 9,
}

const STATUS_META = {
  [CRAWLED_ARTICLE_STATUS.NEW]: { label: 'Mới cào', tone: 'neutral' },
  [CRAWLED_ARTICLE_STATUS.DEDUPING]: { label: 'Đang chấm trùng', tone: 'info' },
  // REWRITING không còn được gán từ khi bỏ xào nháp — giữ nhãn cho dòng cũ trong DB.
  [CRAWLED_ARTICLE_STATUS.REWRITING]: { label: 'Đang xử lý', tone: 'info' },
  [CRAWLED_ARTICLE_STATUS.PENDING]: { label: 'Chờ duyệt', tone: 'warning' },
  [CRAWLED_ARTICLE_STATUS.DUPLICATE]: { label: 'Trùng', tone: 'muted' },
  [CRAWLED_ARTICLE_STATUS.FILTERED]: { label: 'Bị lọc', tone: 'muted' },
  [CRAWLED_ARTICLE_STATUS.APPROVED]: { label: 'Đã duyệt', tone: 'success' },
  [CRAWLED_ARTICLE_STATUS.REJECTED]: { label: 'Đã loại', tone: 'danger' },
  [CRAWLED_ARTICLE_STATUS.FAILED]: { label: 'Lỗi', tone: 'danger' },
}

export function getArticleStatusMeta(status) {
  return STATUS_META[Number(status)] ?? { label: `Trạng thái ${status}`, tone: 'neutral' }
}

export const DEDUP_METHOD_LABEL = {
  0: '—',
  1: 'Trùng guid/URL',
  2: 'Nội dung y hệt',
  3: 'Giống nhau',
  4: 'AI xác nhận',
  5: 'Người duyệt',
}

export const DUPLICATE_TARGET_LABEL = {
  0: '',
  1: 'tin đã cào',
  2: 'bài đã đăng',
}

/** Trạng thái còn đang xử lý — dùng để bật/tắt polling. */
export const IN_FLIGHT_STATUSES = [
  CRAWLED_ARTICLE_STATUS.NEW,
  CRAWLED_ARTICLE_STATUS.DEDUPING,
  CRAWLED_ARTICLE_STATUS.REWRITING,
]

export const STATUS_TABS = [
  { key: 'pending', label: 'Chờ duyệt', status: CRAWLED_ARTICLE_STATUS.PENDING },
  { key: 'duplicate', label: 'Trùng', status: CRAWLED_ARTICLE_STATUS.DUPLICATE },
  { key: 'approved', label: 'Đã duyệt', status: CRAWLED_ARTICLE_STATUS.APPROVED },
  { key: 'filtered', label: 'Bị lọc', status: CRAWLED_ARTICLE_STATUS.FILTERED },
  { key: 'rejected', label: 'Đã loại', status: CRAWLED_ARTICLE_STATUS.REJECTED },
]

export const CRAWL_SOURCE_TYPE = {
  WEB_PAGE: 1,
  FACEBOOK_PAGE: 2,
}

export const CRAWL_SOURCE_TYPE_OPTIONS = [
  {
    value: CRAWL_SOURCE_TYPE.WEB_PAGE,
    label: 'Trang web / báo',
    urlLabel: 'URL trang chuyên mục',
    placeholder: 'https://dantri.com.vn/giao-duc.htm',
    hint: 'Dán link TRANG CHUYÊN MỤC (nơi liệt kê nhiều bài), không phải link một bài lẻ. '
      + 'Trình duyệt sẽ mở trang này lấy danh sách bài rồi vào từng bài bóc toàn văn.',
  },
  {
    value: CRAWL_SOURCE_TYPE.FACEBOOK_PAGE,
    label: 'Fanpage Facebook',
    urlLabel: 'URL fanpage',
    placeholder: 'https://www.facebook.com/tenfanpage',
    hint: 'Cần trình duyệt OpenClaw ĐANG ĐĂNG NHẬP Facebook thì mới đọc được. '
      + 'Nên dùng tài khoản phụ, không phải tài khoản đang quản trị Page của bạn.',
  },
]

/**
 * Trang CHUYÊN MỤC của báo (không phải feed RSS) — trình duyệt sẽ mở trang này để lấy
 * danh sách bài, rồi mở từng bài bóc toàn văn.
 */
export const SUGGESTED_FEEDS = [
  { name: 'Dân trí — Giáo dục', url: 'https://dantri.com.vn/giao-duc.htm' },
  { name: 'VnExpress — Giáo dục', url: 'https://vnexpress.net/giao-duc' },
  { name: 'Thanh Niên — Giáo dục', url: 'https://thanhnien.vn/giao-duc.htm' },
  { name: 'Tuổi Trẻ — Giáo dục', url: 'https://tuoitre.vn/giao-duc.htm' },
  { name: 'Giáo dục Việt Nam', url: 'https://giaoduc.net.vn/giao-duc-24h' },
  { name: 'VietnamNet — Giáo dục', url: 'https://vietnamnet.vn/giao-duc' },
]

/** Từ khoá nên chặn với fanpage trường học — tin đúng chủ đề nhưng không hợp đăng. */
export const SUGGESTED_EXCLUDE = [
  'bạo hành', 'tử vong', 'khởi tố', 'hiếp dâm', 'đánh nhau', 'tự tử', 'bắt giam',
]
