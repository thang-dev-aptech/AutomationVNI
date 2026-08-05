import { POST_STATUS } from '../constants/postStatus'

export default function PostFilterBar({
  keyword,
  onKeywordChange,
  status,
  onStatusChange,
  isRecycled,
  onIsRecycledChange,
}) {
  return (
    <div
      className="card card-body"
      style={{
        display: 'grid',
        gap: 16,
        gridTemplateColumns: 'repeat(auto-fit, minmax(180px, 1fr))',
      }}
    >
      <div className="form-group" style={{ marginBottom: 0 }}>
        <label htmlFor="post-keyword">Tìm kiếm</label>
        <input
          id="post-keyword"
          value={keyword}
          onChange={(event) => onKeywordChange(event.target.value)}
          placeholder="Tiêu đề bài viết..."
        />
      </div>
      <div className="form-group" style={{ marginBottom: 0 }}>
        <label htmlFor="post-status">Trạng thái</label>
        <select
          id="post-status"
          value={status}
          onChange={(event) => onStatusChange(event.target.value)}
        >
          <option value="">Tất cả</option>
          {Object.entries(POST_STATUS).map(([value, meta]) => (
            <option key={value} value={value}>
              {meta.label}
            </option>
          ))}
        </select>
      </div>
      <div className="form-group" style={{ marginBottom: 0 }}>
        <label htmlFor="post-is-recycled">Loại bài</label>
        <select
          id="post-is-recycled"
          value={isRecycled}
          onChange={(event) => onIsRecycledChange(event.target.value)}
        >
          <option value="">Tất cả</option>
          <option value="false">Tạo mới</option>
          <option value="true">Tái sử dụng</option>
        </select>
      </div>
    </div>
  )
}
