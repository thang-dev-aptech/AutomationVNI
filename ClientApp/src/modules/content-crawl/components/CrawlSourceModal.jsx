import { useState } from 'react'
import Modal from '@/shared/components/Modal'
import { toast } from '@/shared/stores/toastStore'
import { getErrorMessage, formatDateTime } from '@/shared/utils/apiHelpers'
import { SUGGESTED_FEEDS } from '../constants/crawlConstants'
import {
  useCrawlNow,
  useCrawlSources,
  useCreateCrawlSource,
  useDeleteCrawlSource,
  useTestCrawlSource,
  useUpdateCrawlSource,
} from '../hooks/useCrawl'

const EMPTY = { name: '', url: '', intervalMinutes: 30, maxItemsPerRun: 30, lookbackHours: 48 }

export default function CrawlSourceModal({ open, onClose, canManage }) {
  const { data: sources = [], isLoading } = useCrawlSources()
  const createSource = useCreateCrawlSource()
  const updateSource = useUpdateCrawlSource()
  const deleteSource = useDeleteCrawlSource()
  const testSource = useTestCrawlSource()
  const crawlNow = useCrawlNow()

  const [form, setForm] = useState(EMPTY)
  const [preview, setPreview] = useState(null)

  const setField = (key) => (e) => setForm((f) => ({ ...f, [key]: e.target.value }))

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
        <table className="crawl-source-table">
          <thead>
            <tr><th>Nguồn</th><th>Lần cào gần nhất</th><th /></tr>
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
              <tr><td colSpan={3}>Chưa có nguồn nào.</td></tr>
            )}
          </tbody>
        </table>

        {canManage && (
          <div className="crawl-source-form">
            <h4>Thêm nguồn RSS</h4>
            <div className="crawl-suggest">
              {SUGGESTED_FEEDS.map((f) => (
                <button key={f.url} type="button" className="btn btn-ghost btn-sm"
                  onClick={() => setForm((v) => ({ ...v, name: f.name, url: f.url }))}>
                  {f.name}
                </button>
              ))}
            </div>
            <input className="form-control" placeholder="Tên hiển thị"
              value={form.name} onChange={setField('name')} />
            <input className="form-control" placeholder="URL feed RSS"
              value={form.url} onChange={setField('url')} />
            <div className="crawl-form-row">
              <label>Chu kỳ (phút)
                <input className="form-control" type="number" value={form.intervalMinutes}
                  onChange={setField('intervalMinutes')} />
              </label>
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
                {testSource.isPending ? 'Đang đọc thử...' : 'Đọc thử (không lưu)'}
              </button>
              <button type="button" className="btn btn-primary"
                onClick={handleCreate} disabled={!form.name || !form.url || createSource.isPending}>
                Thêm nguồn
              </button>
            </div>

            {preview?.ok && (
              <div className="crawl-preview">
                <strong>Đọc được {preview.itemCount} bài:</strong>
                <ul>{preview.items.map((i) => <li key={i.link}>{i.title}</li>)}</ul>
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
