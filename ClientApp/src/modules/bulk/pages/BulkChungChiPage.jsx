import { useState } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { useMutation } from '@tanstack/react-query'
import PageHeader from '@/shared/components/PageHeader'
import LoadingState from '@/shared/components/LoadingState'
import EmptyState from '@/shared/components/EmptyState'
import ErrorState from '@/shared/components/ErrorState'
import ChannelMultiSelect from '@/shared/components/ChannelMultiSelect'
import { unwrapApiData, getErrorMessage } from '@/shared/utils/apiHelpers'
import { toast } from '@/shared/stores/toastStore'
import { useChungChiEligiblePages } from '@/modules/media/hooks/useMediaFolders'
import { bulkApi } from '../services/bulkApi'
import './BulkCreatePage.css'

const DEFAULT_CHUNG_CHI_IDEA = 'Chứng chỉ'

export const CHUNG_CHI_MODE = {
  Random: 1,
  All: 2,
}

export default function BulkChungChiPage() {
  const navigate = useNavigate()
  const [channelIds, setChannelIds] = useState([])
  const [mode, setMode] = useState(CHUNG_CHI_MODE.Random)
  const [randomCount, setRandomCount] = useState('1')

  const {
    data: channelData,
    isLoading: channelsLoading,
    isError: channelsError,
    error: channelsErrorDetail,
    refetch: refetchChannels,
  } = useChungChiEligiblePages()
  const channels = Array.isArray(channelData) ? channelData : []

  const createMutation = useMutation({
    mutationFn: async (payload) => unwrapApiData(await bulkApi.createChungChi(payload)),
  })

  const totalPosts = channelIds.length
  const isRandom = mode === CHUNG_CHI_MODE.Random

  const buildPayload = () => {
    const count = Number(randomCount)
    const payload = {
      items: [{
        idea: DEFAULT_CHUNG_CHI_IDEA,
      }],
      channelIds,
      mode,
    }
    if (isRandom) payload.randomCount = Number.isFinite(count) && count >= 1 ? count : 1
    return payload
  }

  const handleSubmit = async () => {
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
  if (channelsError) {
    return <ErrorState message={getErrorMessage(channelsErrorDetail)} onRetry={refetchChannels} />
  }

  return (
    <section className="bulk-create">
      <PageHeader
        title="Tạo hàng loạt từ chứng chỉ"
        description="Mỗi kênh một bài; ảnh lấy nguyên trạng từ thư mục chung_chi của từng Page — không overlay"
        actions={<Link to="/bulk" className="btn btn-secondary">Tạo hàng loạt thường</Link>}
      />

      {channels.length === 0 && (
        <EmptyState
          message="Chưa có Page nào có ảnh trong thư mục chung_chi."
          action={<Link to="/media" className="btn btn-primary">Đến Thư mục Media</Link>}
        />
      )}

      {channels.length > 0 && (
        <div className="bulk-panel">
          <div className="bulk-panel__head">
            <h3 className="bulk-panel__title">Ảnh chứng chỉ theo kênh</h3>
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

          <fieldset className="form-group" style={{ marginTop: 16, border: 0, padding: 0 }}>
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

          <div className="bulk-toolbar">
            <div className="bulk-toolbar__meta">
              Sẽ tạo <strong style={{ color: 'var(--color-text)' }}>{totalPosts}</strong> bài
              {' '}(mỗi kênh 1 bài)
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
