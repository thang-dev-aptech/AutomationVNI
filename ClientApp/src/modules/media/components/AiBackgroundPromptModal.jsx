import { useEffect, useMemo, useState } from 'react'
import Modal from '@/shared/components/Modal'
import { toast } from '@/shared/stores/toastStore'
import { usePageContextList } from '@/modules/page-contexts/hooks/usePageContexts'
import { usePromptTemplateList } from '@/modules/prompt-templates/hooks/usePromptTemplates'
import { useSocialChannelAll } from '@/modules/social-channels/hooks/useSocialChannels'
import { renderBackgroundPrompt } from '../constants/aiBackgroundPrompt'

/**
 * Tạo prompt ẢNH NỀN (không chữ/logo) cho nhiều Page cùng lúc — chỉ thay biến phía client
 * (renderBackgroundPrompt), KHÔNG gọi AI/API mới nào (tái dùng GET /api/PageContext,
 * GET /api/SocialChannel và filter /api/PromptTemplate đã có sẵn). Hiển thị theo TÊN KÊNH
 * (SocialChannel.pageName) chứ không phải BrandName — mỗi PageContext gắn đúng 1 kênh qua
 * SocialChannelId. Danh mục không cho gõ tay — tự lấy theo PageContext.defaultImageTemplateId,
 * hoặc danh mục mặc định (isDefault) trong Danh mục template nếu Page chưa cấu hình riêng.
 */
export default function AiBackgroundPromptModal({ open, onClose }) {
  const [selectedIds, setSelectedIds] = useState([])
  const [results, setResults] = useState([])
  const [search, setSearch] = useState('')

  const { data: pageData } = usePageContextList({ index: 1, size: 100 })
  const pages = pageData?.items ?? []

  const { data: channels = [] } = useSocialChannelAll()
  const channelNameById = useMemo(
    () => Object.fromEntries(channels.map((c) => [c.id, c.pageName])),
    [channels],
  )
  const labelForPage = (page) => channelNameById[page.socialChannelId] ?? page.brandName

  const { data: templateData } = usePromptTemplateList({ index: 1, size: 100, isActive: true })
  const templates = templateData?.items ?? []

  useEffect(() => {
    if (!open) return
    setSelectedIds([])
    setResults([])
    setSearch('')
  }, [open])

  const filteredPages = useMemo(() => {
    const keyword = search.trim().toLowerCase()
    if (!keyword) return pages
    return pages.filter((p) => labelForPage(p).toLowerCase().includes(keyword))
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [pages, search, channelNameById])

  const togglePage = (id) => {
    setSelectedIds((prev) => (prev.includes(id) ? prev.filter((x) => x !== id) : [...prev, id]))
  }

  // "Chọn tất cả" áp dụng theo danh sách ĐANG LỌC (search) — bỏ tick thì bỏ đúng các dòng đang
  // hiển thị, không đụng tới lựa chọn của các kênh đang bị ẩn bởi ô tìm kiếm.
  const allFilteredSelected =
    filteredPages.length > 0 && filteredPages.every((p) => selectedIds.includes(p.id))
  const toggleAll = () => {
    const filteredIds = filteredPages.map((p) => p.id)
    setSelectedIds((prev) =>
      allFilteredSelected
        ? prev.filter((id) => !filteredIds.includes(id))
        : [...new Set([...prev, ...filteredIds])],
    )
  }

  const resolveCategory = (page) => {
    const byDefault = templates.find((t) => t.id === page.defaultImageTemplateId)
    if (byDefault) return byDefault
    return templates.find((t) => t.isDefault) ?? null
  }

  const handleGenerate = (event) => {
    event.preventDefault()
    if (selectedIds.length === 0) {
      toast.error('Chọn ít nhất 1 Page')
      return
    }
    const generated = selectedIds.map((id) => {
      const page = pages.find((p) => p.id === id)
      const category = resolveCategory(page)
      const prompt = renderBackgroundPrompt({
        title: category?.description || category?.name || '',
        category: category?.name || '',
        brand: page.brandName ?? '',
        brandColors: page.brandColors ?? '',
        tone: page.toneOfVoice ?? '',
      })
      return {
        pageId: page.id,
        channelLabel: labelForPage(page),
        categoryName: category?.name || '(chưa có danh mục mặc định)',
        prompt,
      }
    })
    setResults(generated)
  }

  const handleCopy = async (prompt) => {
    try {
      await navigator.clipboard.writeText(prompt)
      toast.success('Đã copy prompt')
    } catch {
      toast.error('Không copy được clipboard')
    }
  }

  const handleCopyAll = async () => {
    const combined = results
      .map((r) => `=== ${r.channelLabel} (danh mục: ${r.categoryName}) ===\n${r.prompt}`)
      .join('\n\n')
    try {
      await navigator.clipboard.writeText(combined)
      toast.success(`Đã copy ${results.length} prompt`)
    } catch {
      toast.error('Không copy được clipboard')
    }
  }

  return (
    <Modal
      open={open}
      title="Tạo prompt ảnh nền AI"
      onClose={onClose}
      footer={(
        <>
          <button type="button" className="btn btn-secondary" onClick={onClose}>Đóng</button>
          <button type="submit" form="ai-bg-prompt-form" className="btn btn-primary">
            Tạo prompt
          </button>
        </>
      )}
    >
      <form id="ai-bg-prompt-form" onSubmit={handleGenerate}>
        <div className="card card-body" style={{ margin: '0 0 16px', background: 'var(--color-surface-muted)' }}>
          <small>
            Prompt sinh ra dành cho ẢNH NỀN sạch (không chữ/logo) — dán vào Claude/ChatGPT để lấy
            prompt ảnh cuối cùng, dùng ở công cụ tạo ảnh AI bạn đang có. Danh mục lấy tự động theo
            cấu hình của từng Page (hoặc danh mục mặc định nếu Page chưa cấu hình riêng).
          </small>
        </div>

        <div className="form-group">
          <label htmlFor="ai-bg-search">Chọn kênh (được chọn nhiều)</label>
          <input
            id="ai-bg-search"
            value={search}
            onChange={(event) => setSearch(event.target.value)}
            placeholder="Tìm theo tên kênh..."
            style={{ marginBottom: 8 }}
          />
          <div
            style={{
              maxHeight: 200,
              overflowY: 'auto',
              border: '1px solid var(--color-border)',
              borderRadius: 6,
              padding: 8,
            }}
          >
            {filteredPages.length === 0 && (
              <div style={{ padding: 8, color: 'var(--color-text-muted)' }}>
                {pages.length === 0 ? 'Chưa có Page nào' : 'Không tìm thấy kênh nào khớp'}
              </div>
            )}
            {filteredPages.length > 0 && (
              <label
                style={{
                  display: 'flex',
                  alignItems: 'center',
                  gap: 8,
                  padding: '4px 0 8px',
                  marginBottom: 4,
                  borderBottom: '1px solid var(--color-border)',
                  fontWeight: 600,
                }}
              >
                <input type="checkbox" checked={allFilteredSelected} onChange={toggleAll} />
                Chọn tất cả ({filteredPages.length})
              </label>
            )}
            {filteredPages.map((p) => (
              <label
                key={p.id}
                style={{ display: 'flex', alignItems: 'center', gap: 8, padding: '4px 0' }}
              >
                <input
                  type="checkbox"
                  checked={selectedIds.includes(p.id)}
                  onChange={() => togglePage(p.id)}
                />
                {labelForPage(p)}
              </label>
            ))}
          </div>
        </div>
      </form>

      {results.length > 0 && (
        <div style={{ display: 'flex', flexDirection: 'column', gap: 16, marginTop: 16 }}>
          <div style={{ display: 'flex', justifyContent: 'flex-end' }}>
            <button type="button" className="btn btn-primary" onClick={handleCopyAll}>
              📋 Copy tất cả ({results.length})
            </button>
          </div>
          {results.map((r) => (
            <div key={r.pageId} className="form-group" style={{ marginBottom: 0 }}>
              <label>
                {r.channelLabel} <span style={{ color: 'var(--color-text-muted)', fontWeight: 400 }}>— danh mục: {r.categoryName}</span>
              </label>
              <textarea readOnly rows={10} value={r.prompt} />
              <button
                type="button"
                className="btn btn-secondary"
                style={{ marginTop: 8 }}
                onClick={() => handleCopy(r.prompt)}
              >
                📋 Copy
              </button>
            </div>
          ))}
        </div>
      )}
    </Modal>
  )
}
