import { useMemo, useRef, useState } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import PageHeader from '@/shared/components/PageHeader'
import LoadingState from '@/shared/components/LoadingState'
import EmptyState from '@/shared/components/EmptyState'
import { getErrorMessage } from '@/shared/utils/apiHelpers'
import { toast } from '@/shared/stores/toastStore'
import { useSocialChannelAll } from '@/modules/social-channels/hooks/useSocialChannels'
import { useCategoryList } from '@/modules/categories/hooks/useCategories'
import { usePromptTemplateList } from '@/modules/prompt-templates/hooks/usePromptTemplates'
import { TEMPLATE_TYPE } from '@/modules/prompt-templates/constants/promptTemplateType'
import { GENERATION_FLOW_OPTIONS } from '@/modules/posts/constants/postStatus'
import { useBulkCreate, useSuggestIdeas } from '../hooks/useBulk'

const emptyRow = () => ({ idea: '' })

export default function BulkCreatePage() {
  const navigate = useNavigate()
  const csvInputRef = useRef(null)

  const [rows, setRows] = useState([emptyRow(), emptyRow(), emptyRow()])
  const [channelIds, setChannelIds] = useState([])
  const [generationFlow, setGenerationFlow] = useState('1')
  const [categoryId, setCategoryId] = useState('')
  const [textTemplateId, setTextTemplateId] = useState('')
  const [imageTemplateId, setImageTemplateId] = useState('')
  const [topic, setTopic] = useState('')
  const [ideaCount, setIdeaCount] = useState(5)

  const { data: channels = [], isLoading: channelsLoading } = useSocialChannelAll()
  const { data: categoryData } = useCategoryList({ index: 1, size: 100 })
  const { data: textTplData } = usePromptTemplateList({ templateType: TEMPLATE_TYPE.TEXT, isActive: true, index: 1, size: 100 })
  const { data: imageTplData } = usePromptTemplateList({ templateType: TEMPLATE_TYPE.IMAGE, isActive: true, index: 1, size: 100 })

  const categories = categoryData?.items ?? []
  const textTemplates = textTplData?.items ?? []
  const imageTemplates = imageTplData?.items ?? []

  const createMutation = useBulkCreate()
  const suggestMutation = useSuggestIdeas()

  const validRows = useMemo(() => rows.filter((r) => r.idea.trim()), [rows])
  const totalPosts = validRows.length * channelIds.length

  const setRow = (i, value) => setRows((prev) => prev.map((r, idx) => (idx === i ? { ...r, idea: value } : r)))
  const addRow = () => setRows((prev) => [...prev, emptyRow()])
  const removeRow = (i) => setRows((prev) => (prev.length <= 1 ? prev : prev.filter((_, idx) => idx !== i)))

  const toggleChannel = (id) =>
    setChannelIds((prev) => (prev.includes(id) ? prev.filter((c) => c !== id) : [...prev, id]))

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
        category: categories.find((c) => c.id === categoryId)?.name || null,
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
      const text = String(reader.result || '')
      const ideas = text
        .split(/\r?\n/)
        .map((line) => line.split(',')[0].trim()) // cột đầu = ý tưởng
        .filter((s) => s && s.toLowerCase() !== 'idea' && s.toLowerCase() !== 'title')
      appendIdeas(ideas)
      toast.success(`Import ${ideas.length} ý tưởng từ CSV`)
    }
    reader.readAsText(file)
    event.target.value = ''
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
    try {
      const result = await createMutation.mutateAsync({
        items: validRows.map((r) => ({ idea: r.idea.trim() })),
        channelIds,
        generationFlow: Number(generationFlow),
        categoryId: categoryId || null,
        textTemplateId: textTemplateId || null,
        imageTemplateId: imageTemplateId || null,
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
        description="Nhập nhiều ý tưởng, chọn nhiều kênh (fan-out) → AI sinh nội dung nền → duyệt & rải lịch"
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
          {/* AI đề xuất ý tưởng */}
          <div className="card card-body" style={{ marginBottom: 16 }}>
            <h3 style={{ marginTop: 0 }}>🤖 AI đề xuất ý tưởng (tuỳ chọn)</h3>
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

          {/* Danh sách ý tưởng */}
          <div className="card card-body" style={{ marginBottom: 16 }}>
            <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
              <h3 style={{ margin: 0 }}>Ý tưởng ({validRows.length})</h3>
              <div style={{ display: 'flex', gap: 8 }}>
                <button type="button" className="btn btn-ghost" onClick={() => csvInputRef.current?.click()}>
                  Import CSV
                </button>
                <input ref={csvInputRef} type="file" accept=".csv,.txt" onChange={handleCsv} style={{ display: 'none' }} />
                <button type="button" className="btn btn-ghost" onClick={addRow}>+ Thêm dòng</button>
              </div>
            </div>
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

          {/* Kênh (fan-out) + cấu hình */}
          <div className="card card-body" style={{ marginBottom: 16 }}>
            <h3 style={{ marginTop: 0 }}>Kênh đăng (fan-out) — chọn nhiều</h3>
            <div style={{ display: 'flex', flexWrap: 'wrap', gap: 10 }}>
              {channels.map((ch) => (
                <label key={ch.id} className="btn btn-ghost" style={{
                  display: 'flex', alignItems: 'center', gap: 6,
                  borderColor: channelIds.includes(ch.id) ? 'var(--color-primary,#4f46e5)' : undefined,
                }}>
                  <input type="checkbox" checked={channelIds.includes(ch.id)} onChange={() => toggleChannel(ch.id)} />
                  {ch.pageName}
                </label>
              ))}
            </div>

            <div style={{ display: 'flex', gap: 16, flexWrap: 'wrap', marginTop: 16 }}>
              <div className="form-group" style={{ marginBottom: 0, minWidth: 180 }}>
                <label htmlFor="bulk-flow">Luồng sinh</label>
                <select id="bulk-flow" value={generationFlow} onChange={(e) => setGenerationFlow(e.target.value)}>
                  {GENERATION_FLOW_OPTIONS.map((o) => <option key={o.value} value={o.value}>{o.label}</option>)}
                </select>
              </div>
              {categories.length > 0 && (
                <div className="form-group" style={{ marginBottom: 0, minWidth: 180 }}>
                  <label htmlFor="bulk-cat">Danh mục (chung)</label>
                  <select id="bulk-cat" value={categoryId} onChange={(e) => setCategoryId(e.target.value)}>
                    <option value="">Không chọn</option>
                    {categories.map((c) => <option key={c.id} value={c.id}>{c.name}</option>)}
                  </select>
                </div>
              )}
              {textTemplates.length > 0 && (
                <div className="form-group" style={{ marginBottom: 0, minWidth: 180 }}>
                  <label htmlFor="bulk-ttpl">Template nội dung</label>
                  <select id="bulk-ttpl" value={textTemplateId} onChange={(e) => setTextTemplateId(e.target.value)}>
                    <option value="">Mặc định</option>
                    {textTemplates.map((t) => <option key={t.id} value={t.id}>{t.name}</option>)}
                  </select>
                </div>
              )}
              {imageTemplates.length > 0 && (
                <div className="form-group" style={{ marginBottom: 0, minWidth: 180 }}>
                  <label htmlFor="bulk-itpl">Template ảnh</label>
                  <select id="bulk-itpl" value={imageTemplateId} onChange={(e) => setImageTemplateId(e.target.value)}>
                    <option value="">Mặc định</option>
                    {imageTemplates.map((t) => <option key={t.id} value={t.id}>{t.name}</option>)}
                  </select>
                </div>
              )}
            </div>
          </div>

          {/* Submit */}
          <div className="card card-body" style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
            <div>
              Sẽ tạo <strong>{totalPosts}</strong> bài
              <span style={{ color: 'var(--text-muted,#888)' }}> ({validRows.length} ý tưởng × {channelIds.length} kênh)</span>
              {totalPosts > 50 && <span style={{ color: 'var(--color-warning,#d97706)' }}> — lô lớn, sẽ sinh dần ở nền</span>}
            </div>
            <button type="button" className="btn btn-primary" onClick={handleSubmit}
              disabled={createMutation.isPending || totalPosts === 0}>
              {createMutation.isPending ? 'Đang tạo...' : `Tạo ${totalPosts} bài → sinh nền`}
            </button>
          </div>
        </>
      )}
    </section>
  )
}
