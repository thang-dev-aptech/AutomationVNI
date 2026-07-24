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
import { useBulkCreate, useSuggestIdeas } from '../hooks/useBulk'
import { downloadBulkIdeasSampleCsv, parseBulkIdeasFile } from '../utils/bulkIdeasImport'

const emptyRow = () => ({ idea: '' })

export default function BulkCreatePage() {
  const navigate = useNavigate()
  const csvInputRef = useRef(null)

  const [rows, setRows] = useState([emptyRow(), emptyRow(), emptyRow()])
  const [channelIds, setChannelIds] = useState([])
  const [promptTemplateId, setPromptTemplateId] = useState('')
  const [topic, setTopic] = useState('')
  const [ideaCount, setIdeaCount] = useState(5)
  const [useMedia, setUseMedia] = useState(false)
  const [postTypeId, setPostTypeId] = useState('')

  const { data: channels = [], isLoading: channelsLoading } = useSocialChannelAll()
  const { data: tplData } = usePromptTemplateList({ isActive: true, index: 1, size: 100 })
  const categoryTemplates = tplData?.items ?? []
  const { data: categoryData } = useCategoryList({ index: 1, size: 200 })
  const categories = categoryData?.items ?? []
  const { data: pageContextData } = usePageContextList({ index: 1, size: 200 })
  const pageContexts = pageContextData?.items ?? []

  const createMutation = useBulkCreate()
  const suggestMutation = useSuggestIdeas()

  const validRows = useMemo(() => rows.filter((r) => r.idea.trim()), [rows])
  const totalPosts = validRows.length * channelIds.length
  const selectedCategoryName = categoryTemplates.find((t) => t.id === promptTemplateId)?.name

  // Page đã có PageContext (template mặc định) thì không cần chọn danh mục — giống tạo bài đơn.
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

  const setRow = (i, value) => setRows((prev) => prev.map((r, idx) => (idx === i ? { ...r, idea: value } : r)))
  const addRow = () => setRows((prev) => [...prev, emptyRow()])
  const removeRow = (i) => setRows((prev) => (prev.length <= 1 ? prev : prev.filter((_, idx) => idx !== i)))

  const appendIdeas = (ideas) => {
    const cleaned = ideas.map((s) => String(s).trim()).filter(Boolean)
    if (cleaned.length === 0) return
    setRows((prev) => {
      const existing = prev.filter((r) => r.idea.trim())
      return [...existing, ...cleaned.map((idea) => ({ idea }))]
    })
  }

  const handleSuggest = async () => {
    if (!topic.trim()) {
      toast.error('Nhập chủ đề để AI đề xuất')
      return
    }
    try {
      const res = await suggestMutation.mutateAsync({
        topic: topic.trim(),
        count: Number(ideaCount) || 5,
        category: selectedCategoryName || null,
      })
      appendIdeas(res?.ideas ?? [])
      toast.success(`AI đề xuất ${res?.ideas?.length ?? 0} ý tưởng (${res?.source})`)
    } catch (error) {
      toast.error(getErrorMessage(error))
    }
  }

  const handleCsv = (event) => {
    const file = event.target.files?.[0]
    if (!file) return
    const reader = new FileReader()
    reader.onload = () => {
      try {
        const ideas = parseBulkIdeasFile(String(reader.result || ''))
        if (ideas.length === 0) {
          toast.error('File không có ý tưởng hợp lệ. Tải file mẫu để xem định dạng.')
          return
        }
        appendIdeas(ideas)
        toast.success(`Import ${ideas.length} ý tưởng từ file`)
      } catch (err) {
        toast.error(err?.message || 'Không đọc được file')
      }
    }
    reader.readAsText(file)
    event.target.value = ''
  }

  const handleDownloadSample = () => {
    downloadBulkIdeasSampleCsv()
    toast.success('Đã tải bulk-ideas-sample.csv')
  }

  const handleSubmit = async () => {
    if (validRows.length === 0) {
      toast.error('Nhập ít nhất 1 ý tưởng')
      return
    }
    if (channelIds.length === 0) {
      toast.error('Chọn ít nhất 1 kênh')
      return
    }
    if (categoryRequired && !promptTemplateId) {
      toast.error('Có page chưa có PageContext — hãy chọn danh mục')
      return
    }
    try {
      const result = await createMutation.mutateAsync({
        items: validRows.map((r) => ({ idea: r.idea.trim() })),
        channelIds,
        generationFlow: useMedia ? 2 : 1,
        categoryId: useMedia ? (postTypeId || null) : null,
        // Chuỗi rỗng không parse được thành Guid? → backend trả 400 ngay ở bước bind model.
        // Không chọn danh mục thì phải gửi null để mỗi page dùng template trong PageContext của nó.
        promptTemplateId: promptTemplateId || null,
      })
      toast.success(result?.message || `Đã tạo ${result?.created} bài`)
      if (result?.batchId) navigate(`/bulk/${result.batchId}`)
    } catch (error) {
      toast.error(getErrorMessage(error))
    }
  }

  if (channelsLoading) return <LoadingState message="Đang tải..." />

  return (
    <section>
      <PageHeader
        title="Tạo bài hàng loạt"
        description="Nhiều ý tưởng × nhiều kênh → 1 danh mục template → AI sinh text & ảnh nền, xong là Đã duyệt"
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
          <div className="card card-body" style={{ marginBottom: 16 }}>
            <h3 style={{ marginTop: 0 }}>AI đề xuất ý tưởng (tuỳ chọn)</h3>
            <div style={{ display: 'flex', gap: 12, flexWrap: 'wrap', alignItems: 'flex-end' }}>
              <div className="form-group" style={{ marginBottom: 0, flex: 1, minWidth: 240 }}>
                <label htmlFor="bulk-topic">Chủ đề</label>
                <input id="bulk-topic" value={topic} onChange={(e) => setTopic(e.target.value)}
                  placeholder="VD: khuyến mãi thời trang mùa đông" />
              </div>
              <div className="form-group" style={{ marginBottom: 0, width: 90 }}>
                <label htmlFor="bulk-count">Số lượng</label>
                <input id="bulk-count" type="number" min={1} max={30} value={ideaCount}
                  onChange={(e) => setIdeaCount(e.target.value)} />
              </div>
              <button type="button" className="btn btn-primary" onClick={handleSuggest} disabled={suggestMutation.isPending}>
                {suggestMutation.isPending ? 'Đang nghĩ...' : 'Đề xuất → thêm vào danh sách'}
              </button>
            </div>
          </div>

          <div className="card card-body" style={{ marginBottom: 16 }}>
            <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', flexWrap: 'wrap', gap: 8 }}>
              <h3 style={{ margin: 0 }}>Ý tưởng ({validRows.length})</h3>
              <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap' }}>
                <button type="button" className="btn btn-ghost" onClick={handleDownloadSample}>
                  Tải file mẫu
                </button>
                <button type="button" className="btn btn-ghost" onClick={() => csvInputRef.current?.click()}>
                  Import CSV
                </button>
                <input ref={csvInputRef} type="file" accept=".csv,.txt" onChange={handleCsv} style={{ display: 'none' }} />
                <button type="button" className="btn btn-ghost" onClick={addRow}>+ Thêm dòng</button>
              </div>
            </div>
            <p style={{ margin: '8px 0 0', fontSize: '0.85rem', color: 'var(--text-muted, #888)' }}>
              File mẫu CSV: cột <code>idea</code> (mỗi dòng một ý tưởng). Tải mẫu → điền → Import CSV.
            </p>
            <div style={{ marginTop: 12 }}>
              {rows.map((row, i) => (
                <div key={i} style={{ display: 'flex', gap: 8, marginBottom: 8 }}>
                  <span style={{ width: 24, textAlign: 'right', color: 'var(--text-muted,#888)', paddingTop: 8 }}>{i + 1}.</span>
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
          </div>

          <div className="card card-body" style={{ marginBottom: 16 }}>
            <ChannelMultiSelect
              label="Kênh đăng (fan-out)"
              placeholder="Chọn page"
              channels={channels}
              value={channelIds}
              onChange={setChannelIds}
              maxHeight={280}
            />

            <div style={{ display: 'flex', gap: 16, flexWrap: 'wrap', marginTop: 16 }}>
              <div className="form-group" style={{ marginBottom: 0, minWidth: 220 }}>
                <label htmlFor="bulk-category">
                  Danh mục {categoryRequired ? '*' : '(tuỳ chọn — ghi đè PageContext)'}
                </label>
                <select
                  id="bulk-category"
                  value={promptTemplateId}
                  onChange={(e) => setPromptTemplateId(e.target.value)}
                  required={categoryRequired}
                >
                  <option value="">
                    {categoryRequired ? 'Chọn danh mục' : 'Dùng mặc định PageContext'}
                  </option>
                  {categoryTemplates.map((t) => (
                    <option key={t.id} value={t.id}>
                      {t.name}{t.isDefault ? ' ⭐' : ''}
                    </option>
                  ))}
                </select>
                {channelIds.length > 0 && (
                  <p style={{ margin: '6px 0 0', fontSize: '0.85rem', color: 'var(--text-muted, #888)' }}>
                    {categoryRequired
                      ? `${needCategoryCount} page chưa có PageContext — bắt buộc chọn danh mục.`
                      : 'Tất cả page đã có PageContext — không cần chọn danh mục.'}
                  </p>
                )}
              </div>
            </div>

            <div
              style={{ border: '1px solid var(--color-border, #e5e7eb)', borderRadius: 8, padding: 12, marginTop: 16 }}
            >
              <label style={{ display: 'flex', alignItems: 'center', gap: 8, marginBottom: 0, cursor: 'pointer' }}>
                <input type="checkbox" checked={useMedia} onChange={(e) => setUseMedia(e.target.checked)} />
                <span>Dùng ảnh từ kho media (AI tự chọn 2–3 ảnh phù hợp cho mỗi bài)</span>
              </label>
              {useMedia && (
                <div className="form-group" style={{ marginTop: 10, marginBottom: 0, maxWidth: 320 }}>
                  <label htmlFor="bulk-post-type">Loại bài viết — lọc ảnh đúng chủ đề (tuỳ chọn)</label>
                  <select id="bulk-post-type" value={postTypeId} onChange={(e) => setPostTypeId(e.target.value)}>
                    <option value="">Tất cả loại (không lọc)</option>
                    {categories.map((c) => (
                      <option key={c.id} value={c.id}>{c.name}</option>
                    ))}
                  </select>
                </div>
              )}
            </div>
          </div>

          <div className="card card-body" style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
            <div>
              Sẽ tạo <strong>{totalPosts}</strong> bài
              <span style={{ color: 'var(--text-muted,#888)' }}> ({validRows.length} ý tưởng × {channelIds.length} kênh)</span>
              {totalPosts > 50 && <span style={{ color: 'var(--color-warning,#d97706)' }}> — lô lớn, sẽ sinh dần ở nền</span>}
            </div>
            <button type="button" className="btn btn-primary" onClick={handleSubmit}
              disabled={createMutation.isPending || totalPosts === 0 || (categoryRequired && !promptTemplateId)}>
              {createMutation.isPending ? 'Đang tạo...' : `Tạo ${totalPosts} bài → AI sinh nền`}
            </button>
          </div>
        </>
      )}
    </section>
  )
}
