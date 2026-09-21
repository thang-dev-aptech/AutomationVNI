import { useMemo, useState } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { useMutation } from '@tanstack/react-query'
import PageHeader from '@/shared/components/PageHeader'
import LoadingState from '@/shared/components/LoadingState'
import EmptyState from '@/shared/components/EmptyState'
import ChannelMultiSelect from '@/shared/components/ChannelMultiSelect'
import { unwrapApiData, getErrorMessage } from '@/shared/utils/apiHelpers'
import { toast } from '@/shared/stores/toastStore'
import { useSocialChannelAll } from '@/modules/social-channels/hooks/useSocialChannels'
import { useCategoryList } from '@/modules/categories/hooks/useCategories'
import { bulkApi } from '../services/bulkApi'
import './BulkCreatePage.css'

const emptyRow = () => ({ idea: '' })

export const CHUNG_CHI_MODE = {
  Random: 1,
  All: 2,
}

export default function BulkChungChiPage() {
  const navigate = useNavigate()
  const [rows, setRows] = useState([emptyRow(), emptyRow(), emptyRow()])
  const [channelIds, setChannelIds] = useState([])
  const [categoryId, setCategoryId] = useState('')
  const [mode, setMode] = useState(CHUNG_CHI_MODE.Random)
  const [randomCount, setRandomCount] = useState('1')

  const { data: channels = [], isLoading: channelsLoading } = useSocialChannelAll()
  const { data: categoryData } = useCategoryList({ index: 1, size: 200 })
  const categories = categoryData?.items ?? []

  const createMutation = useMutation({
    mutationFn: async (payload) => unwrapApiData(await bulkApi.createChungChi(payload)),
  })

  const validRows = useMemo(() => rows.filter((r) => r.idea.trim()), [rows])
  const totalPosts = validRows.length * channelIds.length
  const isRandom = mode === CHUNG_CHI_MODE.Random

  const setRow = (i, value) => setRows((prev) => prev.map((r, idx) => (idx === i ? { ...r, idea: value } : r)))
  const addRow = () => setRows((prev) => [...prev, emptyRow()])
  const removeRow = (i) => setRows((prev) => (prev.length <= 1 ? prev : prev.filter((_, idx) => idx !== i)))

  const buildPayload = () => {
    const count = Number(randomCount)
    const payload = {
      items: validRows.map((r) => ({
        idea: r.idea.trim(),
        ...(categoryId ? { categoryId } : {}),
      })),
      channelIds,
      mode,
    }
    if (isRandom) payload.randomCount = Number.isFinite(count) && count >= 1 ? count : 1
    return payload
  }

  const handleSubmit = async () => {
    if (validRows.length === 0) {
      toast.error('Nhập ít nhất 1 ý tưởng')
      return
    }
    if (channelIds.length === 0) {
      toast.error('Chọn ít nhất 1 kênh trước khi tạo hàng loạt')
      return
    }
    try {
      const result = await createMutation.mutateAsync(buildPayload())
      toast.success(result?.message || `Đã tạo ${result?.created} bài`)
      if (result?.batchId) navigate(`/bulk/${result.batchId}`)
    } catch (error) {
      toast.error(getErrorMessage(error))
    }
  }

  if (channelsLoading) return <LoadingState message="Đang tải..." />

  return (
    <section className="bulk-create">
      <PageHeader
        title="Tạo hàng loạt từ chứng chỉ"
        description="Ý tưởng × kênh; ảnh lấy nguyên trạng từ thư mục chung_chi của từng Page — không overlay"
        actions={<Link to="/bulk" className="btn btn-secondary">Tạo hàng loạt thường</Link>}
      />

      {channels.length === 0 && (
        <EmptyState
          message="Chưa có kênh nào. Kết nối kênh trước khi tạo bài."
          action={<Link to="/platforms" className="btn btn-primary">Đến Platforms</Link>}
        />
      )}

      {channels.length > 0 && (
        <div className="bulk-panel">
          <div className="bulk-panel__head">
            <h3 className="bulk-panel__title">Ý tưởng × kênh (ảnh chứng chỉ)</h3>
            <p className="bulk-panel__desc">
              Chọn Random N ảnh hoặc tất cả ảnh trong thư mục chung_chi của từng Page đã chọn.
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

          <div className="form-group" style={{ marginTop: 16, maxWidth: 360 }}>
            <label htmlFor="chung-chi-category">Loại bài (tuỳ chọn)</label>
            <select
              id="chung-chi-category"
              value={categoryId}
              onChange={(e) => setCategoryId(e.target.value)}
            >
              <option value="">Không gắn loại bài</option>
              {categories.map((c) => (
                <option key={c.id} value={c.id}>{c.name}</option>
              ))}
            </select>
          </div>

          <fieldset className="form-group" style={{ marginTop: 8, border: 0, padding: 0 }}>
            <legend style={{ fontWeight: 500, marginBottom: 8 }}>Chọn ảnh từ chung_chi</legend>
            <div style={{ display: 'flex', gap: 16, flexWrap: 'wrap', alignItems: 'center' }}>
              <label style={{ display: 'flex', alignItems: 'center', gap: 8, cursor: 'pointer' }}>
                <input
                  type="radio"
                  name="chung-chi-mode"
                  value={CHUNG_CHI_MODE.Random}
                  checked={isRandom}
                  onChange={() => setMode(CHUNG_CHI_MODE.Random)}
                />
                Ngẫu nhiên
              </label>
              <label style={{ display: 'flex', alignItems: 'center', gap: 8, cursor: 'pointer' }}>
                <input
                  type="radio"
                  name="chung-chi-mode"
                  value={CHUNG_CHI_MODE.All}
                  checked={!isRandom}
                  onChange={() => setMode(CHUNG_CHI_MODE.All)}
                />
                Tất cả
              </label>
            </div>
          </fieldset>

          {isRandom && (
            <div className="form-group" style={{ maxWidth: 160 }}>
              <label htmlFor="chung-chi-random-count">Số ảnh mỗi bài</label>
              <input
                id="chung-chi-random-count"
                type="number"
                min={1}
                value={randomCount}
                onChange={(e) => setRandomCount(e.target.value)}
              />
            </div>
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
                  aria-label={`Ý tưởng ${i + 1}`}
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
            </div>
            <button
              type="button"
              className="btn btn-primary"
              onClick={handleSubmit}
              disabled={createMutation.isPending || totalPosts === 0}
            >
              {createMutation.isPending ? 'Đang tạo...' : `Tạo ${totalPosts} bài → ảnh chứng chỉ`}
            </button>
          </div>
        </div>
      )}
    </section>
  )
}
