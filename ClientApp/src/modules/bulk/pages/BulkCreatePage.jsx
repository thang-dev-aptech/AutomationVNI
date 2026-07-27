import { useMemo, useRef, useState } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import PageHeader from '@/shared/components/PageHeader'
import LoadingState from '@/shared/components/LoadingState'
import EmptyState from '@/shared/components/EmptyState'
import { getErrorMessage } from '@/shared/utils/apiHelpers'
import { toast } from '@/shared/stores/toastStore'
import { useSocialChannelAll } from '@/modules/social-channels/hooks/useSocialChannels'
import { usePromptTemplateList } from '@/modules/prompt-templates/hooks/usePromptTemplates'
import { useCategoryList } from '@/modules/categories/hooks/useCategories'
import { usePageContextList } from '@/modules/page-contexts/hooks/usePageContexts'
import { isPageContextTemplateReady } from '@/modules/posts/components/PostCreateForm'
import ChannelMultiSelect from '@/shared/components/ChannelMultiSelect'
import { useBulkCreate, useBulkImport } from '../hooks/useBulk'
import {
  downloadBulkIdeasSampleCsv,
  parseBulkImportFile,
} from '../utils/bulkIdeasImport'
import {
  buildGptPageListText,
  buildPageCodeMap,
  downloadBulkScheduleSkeleton,
  downloadGptPageBrief,
} from '../utils/bulkScheduleSkeleton'
import './BulkCreatePage.css'

const emptyRow = () => ({ idea: '' })
const SKEL_CHANNEL_IDS_KEY = 'vni.bulkSkelChannelIds.v1'

function defaultDateFrom() {
  const d = new Date()
  return d.toISOString().slice(0, 10)
}

function defaultDateTo() {
  const d = new Date()
  d.setDate(d.getDate() + 6)
  return d.toISOString().slice(0, 10)
}

function loadSkelChannelIds() {
  try {
    const raw = localStorage.getItem(SKEL_CHANNEL_IDS_KEY)
    const parsed = raw ? JSON.parse(raw) : []
    return Array.isArray(parsed) ? parsed.filter(Boolean) : []
  } catch {
    return []
  }
}

export default function BulkCreatePage() {
  const navigate = useNavigate()
  const csvInputRef = useRef(null)

  const [rows, setRows] = useState([emptyRow(), emptyRow(), emptyRow()])
  const [channelIds, setChannelIds] = useState([])
  const [promptTemplateId, setPromptTemplateId] = useState('')
  const [useMedia, setUseMedia] = useState(false)
  const [postTypeId, setPostTypeId] = useState('')

  const [skelFrom, setSkelFrom] = useState(defaultDateFrom)
  const [skelTo, setSkelTo] = useState(defaultDateTo)
  const [skelSlots, setSkelSlots] = useState('09:00,15:00')
  const [skelJitter, setSkelJitter] = useState(35)
  const [skelChannelIds, setSkelChannelIds] = useState(loadSkelChannelIds)

  const setSkelChannels = (ids) => {
    setSkelChannelIds(ids)
    try {
      localStorage.setItem(SKEL_CHANNEL_IDS_KEY, JSON.stringify(ids))
    } catch { /* ignore */ }
  }

  const { data: channels = [], isLoading: channelsLoading } = useSocialChannelAll()
  const { data: tplData } = usePromptTemplateList({ isActive: true, index: 1, size: 100 })
  const categoryTemplates = tplData?.items ?? []
  const { data: categoryData } = useCategoryList({ index: 1, size: 200 })
  const categories = categoryData?.items ?? []
  const { data: pageContextData } = usePageContextList({ index: 1, size: 200 })
  const pageContexts = pageContextData?.items ?? []

  const createMutation = useBulkCreate()
  const importMutation = useBulkImport()
  const busy = createMutation.isPending || importMutation.isPending

  const validRows = useMemo(() => rows.filter((r) => r.idea.trim()), [rows])
  const totalPosts = validRows.length * channelIds.length

  const contextByChannel = useMemo(() => {
    const map = new Map()
    for (const ctx of pageContexts) if (ctx?.socialChannelId) map.set(ctx.socialChannelId, ctx)
    return map
  }, [pageContexts])

  const needCategoryCount = useMemo(
    () => channelIds.filter((id) => !isPageContextTemplateReady(contextByChannel.get(id))).length,
    [channelIds, contextByChannel],
  )
  const categoryRequired = needCategoryCount > 0

  const skeletonChannels = useMemo(() => {
    const set = new Set(skelChannelIds)
    return channels.filter((c) => set.has(c.id))
  }, [channels, skelChannelIds])

  const setRow = (i, value) => setRows((prev) => prev.map((r, idx) => (idx === i ? { ...r, idea: value } : r)))
  const addRow = () => setRows((prev) => [...prev, emptyRow()])
  const removeRow = (i) => setRows((prev) => (prev.length <= 1 ? prev : prev.filter((_, idx) => idx !== i)))

  const handleExportSkeleton = () => {
    if (skeletonChannels.length === 0) {
      toast.error('Chọn ít nhất 1 page muốn chạy (page không chọn = nghỉ kỳ này)')
      return
    }
    try {
      const result = downloadBulkScheduleSkeleton({
        channels: skeletonChannels,
        dateFrom: skelFrom,
        dateTo: skelTo,
        slotsText: skelSlots,
        jitterMinutes: Number(skelJitter) || 0,
      })
      toast.success(
        `Đã xuất ${result.pageCount} page / ${result.rowCount} dòng (±${result.jitterMinutes} phút lệch giờ mỗi ngày).`,
      )
    } catch (err) {
      toast.error(err?.message || 'Không xuất được khung')
    }
  }

  const handleCopyGptPageList = async () => {
    try {
      const entries = buildPageCodeMap(skeletonChannels)
      const text = buildGptPageListText(entries, contextByChannel)
      await navigator.clipboard.writeText(text)
      toast.success(`Đã copy ${entries.length} page brief (code + tên + brand/tone) cho GPT`)
    } catch (err) {
      toast.error(err?.message || 'Không copy được')
    }
  }

  const handleDownloadGptBrief = () => {
    try {
      const entries = buildPageCodeMap(skeletonChannels)
      downloadGptPageBrief(entries, contextByChannel)
      toast.success('Đã tải bulk-gpt-page-brief.txt')
    } catch (err) {
      toast.error(err?.message || 'Không tải được brief')
    }
  }

  const runBulkImport = async (parsedRows) => {
    const missing = parsedRows.filter((r) => !r.pageId)
    if (missing.length > 0) {
      toast.error(
        `${missing.length} dòng thiếu page. Hãy xuất khung lại trên máy này rồi import CSV từ lần xuất đó.`,
      )
      return
    }

    const channelIdsInFile = [...new Set(parsedRows.map((r) => r.pageId))]
    const known = new Set(channels.map((c) => c.id))
    const unknown = channelIdsInFile.filter((id) => !known.has(id))
    if (unknown.length > 0) {
      const sample = unknown.slice(0, 3).join(', ')
      toast.error(
        `${unknown.length} page_id không khớp kênh trong hệ thống${unknown.length > 3 ? ` (vd: ${sample}…)` : ` (${sample})`}.`,
      )
      return
    }

    const needCat = channelIdsInFile.filter(
      (id) => !isPageContextTemplateReady(contextByChannel.get(id)),
    ).length
    if (needCat > 0 && !promptTemplateId) {
      toast.error(`${needCat} page chưa có PageContext — chọn danh mục ghi đè trước khi import`)
      return
    }

    const result = await importMutation.mutateAsync({
      timezone: 'Asia/Ho_Chi_Minh',
      generationFlow: useMedia ? 2 : 1,
      categoryId: useMedia ? (postTypeId || null) : null,
      promptTemplateId: promptTemplateId || null,
      rows: parsedRows.map((r) => ({
        idea: r.idea,
        objective: r.objective || null,
        socialChannelId: r.pageId,
        scheduledAtLocal: r.scheduledAtLocal || null,
      })),
    })
    toast.success(result?.message || `Đã import ${result?.created} bài`)
    if (result?.batchId) navigate(`/bulk/${result.batchId}`)
  }

  const buildBulkPayload = (items) => ({
    items: items.map((r) => ({
      idea: r.idea.trim(),
      ...(r.objective?.trim() ? { objective: r.objective.trim() } : {}),
    })),
    channelIds,
    generationFlow: useMedia ? 2 : 1,
    categoryId: useMedia ? (postTypeId || null) : null,
    promptTemplateId: promptTemplateId || null,
  })

  const validateChannels = () => {
    if (channelIds.length === 0) {
      toast.error('Chọn ít nhất 1 kênh trước khi tạo hàng loạt')
      return false
    }
    if (categoryRequired && !promptTemplateId) {
      toast.error('Có page chưa có PageContext — hãy chọn danh mục')
      return false
    }
    return true
  }

  const runBulkCreate = async (items, successLabel) => {
    const result = await createMutation.mutateAsync(buildBulkPayload(items))
    toast.success(successLabel || result?.message || `Đã tạo ${result?.created} bài`)
    if (result?.batchId) navigate(`/bulk/${result.batchId}`)
    return result
  }

  const handleCsv = async (event) => {
    const file = event.target.files?.[0]
    event.target.value = ''
    if (!file) return

    try {
      const text = await file.text()
      const parsed = parseBulkImportFile(text)

      if (parsed.rows.length === 0) {
        toast.error('File không có ý tưởng hợp lệ. Xuất khung lịch hoặc tải file mẫu.')
        return
      }

      if (parsed.unknownCodes.length > 0) {
        const sample = parsed.unknownCodes.slice(0, 5).join(', ')
        toast.error(
          `Không nhận ra page_code: ${sample}${parsed.unknownCodes.length > 5 ? '…' : ''}. Xuất khung lại trên máy này rồi dùng CSV từ lần xuất đó.`,
        )
        return
      }

      // CSV có page_code/page_id → import 1-1 + lịch
      if (parsed.mode === 'import') {
        await runBulkImport(parsed.rows)
        return
      }

      // CSV chỉ có idea → đổ vào danh sách ý tưởng (tạo fan-out sau)
      setRows(parsed.rows.map((item) => ({ idea: item.idea })))
      toast.success(`Đã nạp ${parsed.rows.length} ý tưởng vào danh sách. Chọn kênh rồi bấm tạo.`)
    } catch (error) {
      toast.error(getErrorMessage(error) || error?.message || 'Không đọc được file')
    }
  }

  const handleDownloadSample = () => {
    downloadBulkIdeasSampleCsv()
    toast.success('Đã tải bulk-import-sample.csv')
  }

  const handleSubmit = async () => {
    if (validRows.length === 0) {
      toast.error('Nhập ít nhất 1 ý tưởng')
      return
    }
    if (!validateChannels()) return
    try {
      await runBulkCreate(validRows.map((r) => ({ idea: r.idea.trim() })))
    } catch (error) {
      toast.error(getErrorMessage(error))
    }
  }

  if (channelsLoading) return <LoadingState message="Đang tải..." />

  return (
    <section className="bulk-create">
      <PageHeader
        title="Tạo bài hàng loạt"
        description="Import CSV từ GPT (kèm lịch) hoặc nhập ý tưởng × kênh rồi tạo hàng loạt"
        actions={<Link to="/posts" className="btn btn-secondary">Danh sách bài</Link>}
      />

      {channels.length === 0 && (
        <EmptyState
          message="Chưa có kênh nào. Kết nối kênh trước khi tạo bài."
          action={<Link to="/platforms" className="btn btn-primary">Đến Platforms</Link>}
        />
      )}

      {channels.length > 0 && (
        <>
          <div className="bulk-panel">
            <div className="bulk-panel__head">
              <h3 className="bulk-panel__title">1. Xuất khung lịch cho GPT</h3>
              <p className="bulk-panel__desc">
                Chọn page muốn chạy kỳ này. Page không chọn = nghỉ. Brief gửi GPT bằng copy hoặc file .txt (cùng nội dung).
              </p>
              <ol className="bulk-steps">
                <li><span className="bulk-steps__n">1</span> Chọn page</li>
                <li><span className="bulk-steps__n">2</span> Xuất khung CSV</li>
                <li><span className="bulk-steps__n">3</span> Brief → GPT điền idea</li>
                <li><span className="bulk-steps__n">4</span> Import CSV</li>
              </ol>
            </div>

            <div className="bulk-block">
              <div className="bulk-block__label">
                <span>Page chạy kỳ này</span>
                <span>{skelChannelIds.length}/{channels.length} đã chọn</span>
              </div>
              <ChannelMultiSelect
                label=""
                placeholder="Chọn page cần xuất khung / tạo bài"
                channels={channels}
                value={skelChannelIds}
                onChange={setSkelChannels}
                maxHeight={260}
              />
              <div className="bulk-block__actions">
                <button
                  type="button"
                  className="btn btn-secondary"
                  onClick={() => setSkelChannels(channels.map((c) => c.id))}
                >
                  Chọn tất cả
                </button>
                <button
                  type="button"
                  className="btn btn-ghost"
                  onClick={() => setSkelChannels([])}
                >
                  Bỏ chọn
                </button>
              </div>
            </div>

            <div className="bulk-block">
              <div className="bulk-block__label">
                <span>Lịch đăng</span>
                <span>±{Number(skelJitter) || 0} phút lệch mỗi ngày</span>
              </div>
              <div className="bulk-schedule-grid">
                <div className="form-group">
                  <label htmlFor="skel-from">Từ ngày</label>
                  <input id="skel-from" type="date" value={skelFrom} onChange={(e) => setSkelFrom(e.target.value)} />
                  <p className="bulk-field-hint">&nbsp;</p>
                </div>
                <div className="form-group">
                  <label htmlFor="skel-to">Đến ngày</label>
                  <input id="skel-to" type="date" value={skelTo} onChange={(e) => setSkelTo(e.target.value)} />
                  <p className="bulk-field-hint">&nbsp;</p>
                </div>
                <div className="form-group">
                  <label htmlFor="skel-slots">Khung giờ / ngày</label>
                  <input
                    id="skel-slots"
                    value={skelSlots}
                    onChange={(e) => setSkelSlots(e.target.value)}
                    placeholder="09:00,15:00"
                  />
                  <p className="bulk-field-hint">Ví dụ 09:00,15:00 = 2 bài/ngày</p>
                </div>
                <div className="form-group">
                  <label htmlFor="skel-jitter">Lệch giờ ± phút</label>
                  <input
                    id="skel-jitter"
                    type="number"
                    min={0}
                    max={240}
                    value={skelJitter}
                    onChange={(e) => setSkelJitter(e.target.value)}
                  />
                  <p className="bulk-field-hint">0 = đúng khung · 35 tránh spam FB</p>
                </div>
              </div>
            </div>

            <div className="bulk-toolbar">
              <div className="bulk-toolbar__group">
                <button
                  type="button"
                  className="btn btn-primary"
                  onClick={handleExportSkeleton}
                  disabled={skeletonChannels.length === 0}
                >
                  Xuất khung CSV ({skeletonChannels.length} page)
                </button>
                <button
                  type="button"
                  className="btn btn-secondary"
                  onClick={handleCopyGptPageList}
                  disabled={skeletonChannels.length === 0}
                >
                  Copy brief
                </button>
                <button
                  type="button"
                  className="btn btn-secondary"
                  onClick={handleDownloadGptBrief}
                  disabled={skeletonChannels.length === 0}
                >
                  Tải brief (.txt)
                </button>
              </div>
              <div className="bulk-toolbar__group">
                <button
                  type="button"
                  className="btn btn-primary"
                  onClick={() => csvInputRef.current?.click()}
                  disabled={busy}
                >
                  {importMutation.isPending ? 'Đang import...' : 'Import CSV → tạo + lịch'}
                </button>
                <input ref={csvInputRef} type="file" accept=".csv,.txt" onChange={handleCsv} style={{ display: 'none' }} />
                <button type="button" className="btn btn-ghost" onClick={handleDownloadSample}>
                  File mẫu
                </button>
              </div>
            </div>
          </div>

          <div className="bulk-panel">
            <div className="bulk-panel__head">
              <h3 className="bulk-panel__title">Tuỳ chọn chung</h3>
              <p className="bulk-panel__desc">Áp cho import CSV và tạo hàng loạt bên dưới.</p>
            </div>
            <div className="bulk-schedule-grid" style={{ gridTemplateColumns: 'minmax(220px, 1fr) minmax(220px, 1fr)' }}>
              <div className="form-group">
                <label htmlFor="bulk-category">Danh mục (tuỳ chọn — ghi đè PageContext)</label>
                <select
                  id="bulk-category"
                  value={promptTemplateId}
                  onChange={(e) => setPromptTemplateId(e.target.value)}
                >
                  <option value="">Dùng mặc định PageContext từng page</option>
                  {categoryTemplates.map((t) => (
                    <option key={t.id} value={t.id}>
                      {t.name}{t.isDefault ? ' ⭐' : ''}
                    </option>
                  ))}
                </select>
              </div>
              <div className="form-group">
                <label htmlFor="bulk-media-toggle">Ảnh</label>
                <label style={{ display: 'flex', alignItems: 'center', gap: 8, minHeight: 40, margin: 0, cursor: 'pointer', fontWeight: 500 }}>
                  <input id="bulk-media-toggle" type="checkbox" checked={useMedia} onChange={(e) => setUseMedia(e.target.checked)} />
                  <span>Dùng ảnh từ kho media</span>
                </label>
              </div>
            </div>
            {useMedia && (
              <div className="form-group" style={{ marginTop: 12, marginBottom: 0, maxWidth: 360 }}>
                <label htmlFor="bulk-post-type">Loại bài viết — lọc ảnh (tuỳ chọn)</label>
                <select id="bulk-post-type" value={postTypeId} onChange={(e) => setPostTypeId(e.target.value)}>
                  <option value="">Tất cả loại (không lọc)</option>
                  {categories.map((c) => (
                    <option key={c.id} value={c.id}>{c.name}</option>
                  ))}
                </select>
              </div>
            )}
          </div>

          <div className="bulk-panel">
            <div className="bulk-panel__head">
              <h3 className="bulk-panel__title">2. Nhập ý tưởng × kênh (tạo hàng loạt)</h3>
              <p className="bulk-panel__desc">
                Nhập / dán nhiều ý tưởng, chọn kênh fan-out, rồi tạo. Không dùng AI đề xuất trong app.
              </p>
            </div>

            <ChannelMultiSelect
              label="Kênh đăng (fan-out)"
              placeholder="Chọn page"
              channels={channels}
              value={channelIds}
              onChange={setChannelIds}
              maxHeight={220}
            />
            {channelIds.length > 0 && (
              <p className="bulk-block__hint">
                {categoryRequired
                  ? `${needCategoryCount} page chưa có PageContext — bắt buộc chọn danh mục ở trên.`
                  : 'Tất cả page đã chọn có PageContext — mỗi page dùng prompt riêng.'}
              </p>
            )}

            <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', flexWrap: 'wrap', gap: 8, marginTop: 16, marginBottom: 10 }}>
              <strong style={{ fontSize: '0.9rem' }}>Ý tưởng ({validRows.length})</strong>
              <button type="button" className="btn btn-ghost" onClick={addRow}>+ Thêm dòng</button>
            </div>
            <div>
              {rows.map((row, i) => (
                <div key={i} style={{ display: 'flex', gap: 8, marginBottom: 8, alignItems: 'center' }}>
                  <span style={{ width: 24, textAlign: 'right', color: 'var(--color-text-muted)', flexShrink: 0 }}>{i + 1}.</span>
                  <input
                    value={row.idea}
                    onChange={(e) => setRow(i, e.target.value)}
                    placeholder="Ý tưởng bài viết..."
                    style={{ flex: 1 }}
                  />
                  <button type="button" className="btn btn-ghost" onClick={() => removeRow(i)} title="Xoá dòng">✕</button>
                </div>
              ))}
            </div>

            <div className="bulk-toolbar">
              <div className="bulk-toolbar__meta">
                Sẽ tạo <strong style={{ color: 'var(--color-text)' }}>{totalPosts}</strong> bài
                {' '}({validRows.length} ý tưởng × {channelIds.length} kênh)
                {totalPosts > 50 && <span style={{ color: 'var(--color-warning-text)' }}> — lô lớn, sinh dần ở nền</span>}
              </div>
              <button
                type="button"
                className="btn btn-primary"
                onClick={handleSubmit}
                disabled={busy || totalPosts === 0 || (categoryRequired && !promptTemplateId)}
              >
                {createMutation.isPending ? 'Đang tạo...' : `Tạo ${totalPosts} bài → AI sinh nền`}
              </button>
            </div>
          </div>
        </>
      )}
    </section>
  )
}
