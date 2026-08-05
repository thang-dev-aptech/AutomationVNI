import { useState } from 'react'
import Modal from '@/shared/components/Modal'
import { toast } from '@/shared/stores/toastStore'
import { getErrorMessage, formatDateTime } from '@/shared/utils/apiHelpers'
import {
  CRAWL_SOURCE_TYPE,
  CRAWL_SOURCE_TYPE_OPTIONS,
  SUGGESTED_FEEDS,
} from '../constants/crawlConstants'
import {
  useCrawlNow,
  useCrawlSources,
  useCreateCrawlSource,
  useDeleteCrawlSource,
  useFacebookStatus,
  useTestCrawlSource,
  useUpdateCrawlSource,
} from '../hooks/useCrawl'

const EMPTY = {
  name: '',
  url: '',
  sourceType: CRAWL_SOURCE_TYPE.WEB_PAGE,
  crawlTimes: ['08:00', '14:00'],
  intervalMinutes: 30,
  maxItemsPerRun: 4,
  lookbackHours: 48,
}

export default function CrawlSourceModal({ open, onClose, canManage }) {
  const { data: sources = [], isLoading } = useCrawlSources()
  const createSource = useCreateCrawlSource()
  const updateSource = useUpdateCrawlSource()
  const deleteSource = useDeleteCrawlSource()
  const testSource = useTestCrawlSource()
  const crawlNow = useCrawlNow()

  const [form, setForm] = useState(EMPTY)
  const [preview, setPreview] = useState(null)

  const isFacebook = Number(form.sourceType) === CRAWL_SOURCE_TYPE.FACEBOOK_PAGE
  const typeMeta = CRAWL_SOURCE_TYPE_OPTIONS.find((t) => t.value === Number(form.sourceType))
    ?? CRAWL_SOURCE_TYPE_OPTIONS[0]
  // Chỉ hỏi trạng thái đăng nhập khi thực sự chọn fanpage — mỗi lần hỏi là một lượt
  // điều hướng trình duyệt sang facebook.com, đừng chạy vô cớ.
  const { data: fbStatus, isFetching: fbChecking } = useFacebookStatus(open && isFacebook)

  const setField = (key) => (e) => setForm((f) => ({ ...f, [key]: e.target.value }))

  const setTimeAt = (idx) => (e) => setForm((f) => {
    const next = [...f.crawlTimes]
    next[idx] = e.target.value
    return { ...f, crawlTimes: next }
  })
  const addTime = () => setForm((f) => ({ ...f, crawlTimes: [...f.crawlTimes, '20:00'] }))
  const removeTime = (idx) => setForm((f) => ({
    ...f, crawlTimes: f.crawlTimes.filter((_, i) => i !== idx),
  }))

  const handleTest = async () => {
    setPreview(null)
    try {
      const result = await testSource.mutateAsync({ ...form, maxItemsPerRun: 5 })
      setPreview(result)
      if (!result.ok) toast.error(result.error || 'Không đọc được feed')
    } catch (e) { toast.error(getErrorMessage(e)) }
  }

  const handleCreate = async () => {
    try {
      await createSource.mutateAsync(form)
      toast.success('Đã thêm nguồn')
      setForm(EMPTY)
      setPreview(null)
    } catch (e) { toast.error(getErrorMessage(e)) }
  }

  const handleCrawlNow = async (id) => {
    try {
      const run = await crawlNow.mutateAsync(id)
      toast.success(`Cào xong: ${run.itemsNew} bài mới, ${run.itemsFiltered} bị lọc`)
    } catch (e) { toast.error(getErrorMessage(e)) }
  }

  const handleToggle = async (source) => {
    try {
      await updateSource.mutateAsync({ id: source.id, payload: { isActive: !source.isActive } })
    } catch (e) { toast.error(getErrorMessage(e)) }
  }

  const handleDelete = async (source) => {
    if (!window.confirm(`Xoá nguồn "${source.name}"?`)) return
    try {
      await deleteSource.mutateAsync(source.id)
      toast.success('Đã xoá nguồn')
    } catch (e) { toast.error(getErrorMessage(e)) }
  }

  return (
    <Modal open={open} title="Nguồn cào tin" onClose={onClose}>
      <div className="crawl-sources">
        {isLoading && <p>Đang tải...</p>}
        <div className="crawl-source-table-wrap">
        <table className="crawl-source-table">
          <thead>
            <tr><th>Nguồn</th><th>Giờ cào</th><th>Lần cào gần nhất</th><th /></tr>
          </thead>
          <tbody>
            {sources.map((s) => (
              <tr key={s.id} className={s.isActive ? '' : 'is-off'}>
                <td>
                  <strong>{s.name}</strong>
                  <div className="crawl-source-url">{s.siteDomain || s.url}</div>
                  {s.lastError && <div className="crawl-source-error">{s.lastError}</div>}
                </td>
                <td>
                  {s.crawlTimes?.length
                    ? s.crawlTimes.join(', ')
                    : <span className="crawl-source-url">mỗi {s.intervalMinutes} phút</span>}
                </td>
                <td>
                  {s.lastRunAt ? formatDateTime(s.lastRunAt) : 'chưa cào'}
                  {s.consecutiveFailures > 0 && (
                    <div className="crawl-source-error">lỗi {s.consecutiveFailures} lần liên tiếp</div>
                  )}
                </td>
                <td className="crawl-source-actions">
                  {canManage && (
                    <>
                      <button type="button" className="btn btn-ghost btn-sm"
                        onClick={() => handleCrawlNow(s.id)} disabled={crawlNow.isPending}>
                        Cào ngay
                      </button>
                      <button type="button" className="btn btn-ghost btn-sm" onClick={() => handleToggle(s)}>
                        {s.isActive ? 'Tắt' : 'Bật'}
                      </button>
                      <button type="button" className="btn btn-ghost btn-sm" onClick={() => handleDelete(s)}>
                        Xoá
                      </button>
                    </>
                  )}
                </td>
              </tr>
            ))}
            {sources.length === 0 && !isLoading && (
              <tr><td colSpan={4}>Chưa có nguồn nào.</td></tr>
            )}
          </tbody>
        </table>
        </div>

        {canManage && (
          <div className="crawl-source-form">
            <h4>Thêm nguồn cào</h4>
            {!isFacebook && (
              <div className="crawl-suggest">
                {SUGGESTED_FEEDS.map((f) => (
                  <button key={f.url} type="button" className="btn btn-ghost btn-sm"
                    onClick={() => setForm((v) => ({ ...v, name: f.name, url: f.url }))}>
                    {f.name}
                  </button>
                ))}
              </div>
            )}
            <span className="crawl-times-label">Loại nguồn</span>
            <div className="crawl-type-row">
              {CRAWL_SOURCE_TYPE_OPTIONS.map((t) => (
                <label key={t.value} className="crawl-type-option">
                  <input
                    type="radio"
                    name="crawl-source-type"
                    checked={Number(form.sourceType) === t.value}
                    onChange={() => setForm((f) => ({ ...f, sourceType: t.value, url: '' }))}
                  />
                  <span>{t.label}</span>
                </label>
              ))}
            </div>

            {isFacebook && (
              <div className={`crawl-note ${fbStatus?.loggedIn ? 'crawl-note-success' : 'crawl-note-warning'}`}>
                {fbChecking && 'Đang kiểm tra đăng nhập Facebook...'}
                {!fbChecking && fbStatus?.loggedIn && '✓ Trình duyệt đã đăng nhập Facebook.'}
                {!fbChecking && fbStatus && !fbStatus.loggedIn && (
                  <>
                    <strong>Chưa đăng nhập Facebook</strong> — cào fanpage sẽ không đọc được gì.
                    <div>{fbStatus.reason}</div>
                  </>
                )}
              </div>
            )}

            <input className="form-control" placeholder="Tên hiển thị"
              value={form.name} onChange={setField('name')} />
            <span className="crawl-times-label">{typeMeta.urlLabel}</span>
            <input className="form-control" placeholder={typeMeta.placeholder}
              value={form.url} onChange={setField('url')} />
            <p className="form-hint">{typeMeta.hint}</p>
            <div className="crawl-times">
              <span className="crawl-times-label">Giờ cào mỗi ngày</span>
              <div className="crawl-times-list">
                {form.crawlTimes.map((t, i) => (
                  // eslint-disable-next-line react/no-array-index-key
                  <span className="crawl-time-item" key={i}>
                    <input type="time" className="form-control" value={t} onChange={setTimeAt(i)} />
                    <button type="button" className="btn btn-ghost btn-sm"
                      onClick={() => removeTime(i)} title="Bỏ mốc này">×</button>
                  </span>
                ))}
                <button type="button" className="btn btn-ghost btn-sm" onClick={addTime}>+ Thêm giờ</button>
              </div>
              <p className="form-hint">
                {form.crawlTimes.length > 0
                  ? `Cào ${form.crawlTimes.length} lượt/ngày vào ${form.crawlTimes.join(', ')} (giờ Việt Nam). Máy tắt lúc đến giờ thì bật lên sẽ cào bù.`
                  : 'Không đặt giờ nào thì chạy theo chu kỳ lặp bên dưới.'}
              </p>
            </div>

            <div className="crawl-form-row">
              {form.crawlTimes.length === 0 && (
                <label>Chu kỳ (phút)
                  <input className="form-control" type="number" value={form.intervalMinutes}
                    onChange={setField('intervalMinutes')} />
                </label>
              )}
              <label>Tối đa mỗi lượt
                <input className="form-control" type="number" value={form.maxItemsPerRun}
                  onChange={setField('maxItemsPerRun')} />
              </label>
              <label>Chỉ lấy bài trong (giờ)
                <input className="form-control" type="number" value={form.lookbackHours}
                  onChange={setField('lookbackHours')} />
              </label>
            </div>
            <div className="crawl-form-actions">
              <button type="button" className="btn btn-ghost"
                onClick={handleTest} disabled={!form.url || testSource.isPending}>
                {testSource.isPending ? 'Đang cào thử...' : 'Cào thử (không lưu)'}
              </button>
              <button type="button" className="btn btn-primary"
                onClick={handleCreate} disabled={!form.name || !form.url || createSource.isPending}>
                Thêm nguồn
              </button>
            </div>

            {preview?.ok && (
              <div className="crawl-preview">
                <strong>Bóc được {preview.itemCount} bài toàn văn:</strong>
                <ul>{preview.items.map((i) => (
                    <li key={i.link}>{i.title} <em>({i.contentLength} ký tự)</em></li>
                  ))}</ul>
              </div>
            )}
            {preview && !preview.ok && (
              <div className="crawl-note crawl-note-warning">{preview.error}</div>
            )}
          </div>
        )}
      </div>
    </Modal>
  )
}
