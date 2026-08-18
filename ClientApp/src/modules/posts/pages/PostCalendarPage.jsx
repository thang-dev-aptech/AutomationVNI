import { useMemo, useState } from 'react'
import PageHeader from '@/shared/components/PageHeader'
import LoadingState from '@/shared/components/LoadingState'
import ErrorState from '@/shared/components/ErrorState'
import ChannelMultiSelect from '@/shared/components/ChannelMultiSelect'
import { getErrorMessage } from '@/shared/utils/apiHelpers'
import { toast } from '@/shared/stores/toastStore'
import { useSocialChannelAll } from '@/modules/social-channels/hooks/useSocialChannels'
import { getSocialPlatformLabel } from '@/modules/social-channels/constants/socialPlatform'
import PostCalendar from '../components/PostCalendar'
import { monthRangeUtc, withVnDate } from '../utils/calendarGrid'
import { usePostCalendar, useSchedulePost } from '../hooks/usePosts'

/** Nhóm trạng thái bật/tắt được trên lịch. Khớp PostStatus của backend. */
const STATUS_FILTERS = [
  { key: 'scheduled', label: 'Đã lên lịch', statuses: [5] },
  { key: 'published', label: 'Đã đăng', statuses: [7] },
  { key: 'failed', label: 'Thất bại', statuses: [8] },
]

function currentVnMonth() {
  const now = new Date()
  const parts = new Intl.DateTimeFormat('en-CA', {
    timeZone: 'Asia/Ho_Chi_Minh',
    year: 'numeric',
    month: '2-digit',
  }).formatToParts(now)
  const get = (type) => Number(parts.find((p) => p.type === type)?.value)
  return { year: get('year'), month: get('month') }
}

export default function PostCalendarPage() {
  const [{ year, month }, setCursor] = useState(currentVnMonth)
  const [activeFilters, setActiveFilters] = useState(() => STATUS_FILTERS.map((f) => f.key))
  const [channelIds, setChannelIds] = useState([])

  const { data: channels = [] } = useSocialChannelAll()
  const channelMap = useMemo(
    () => Object.fromEntries(channels.map((c) => [c.id, c.pageName])),
    [channels],
  )

  const params = useMemo(() => {
    const { fromUtc, toUtc } = monthRangeUtc(year, month)
    const statuses = STATUS_FILTERS
      .filter((f) => activeFilters.includes(f.key))
      .flatMap((f) => f.statuses)
    return {
      fromUtc,
      toUtc,
      statuses,
      socialChannelIds: channelIds.length > 0 ? channelIds : undefined,
    }
  }, [year, month, activeFilters, channelIds])

  const { data: posts = [], isLoading, isError, error, refetch } = usePostCalendar(params)
  const scheduleMutation = useSchedulePost()

  function shiftMonth(delta) {
    setCursor((prev) => {
      const next = new Date(Date.UTC(prev.year, prev.month - 1 + delta, 1))
      return { year: next.getUTCFullYear(), month: next.getUTCMonth() + 1 }
    })
  }

  function toggleFilter(key) {
    setActiveFilters((prev) =>
      prev.includes(key) ? prev.filter((k) => k !== key) : [...prev, key])
  }

  /** Kéo-thả sang ngày mới: giữ nguyên giờ-phút cũ, chỉ đổi phần ngày. */
  function handleReschedule(post, targetYmd) {
    const newDate = withVnDate(post.scheduledPublishAt, targetYmd)

    // Chốt chặn cuối: ô quá khứ đã bị chặn ở component, nhưng nếu kéo đúng lúc giao ngày
    // thì vẫn có thể lọt — backend sẽ từ chối, nên báo trước cho gọn.
    if (newDate.getTime() <= Date.now()) {
      toast.error('Không thể đặt lịch vào thời điểm đã qua')
      return
    }

    scheduleMutation.mutate(
      {
        id: post.id,
        scheduledAt: newDate.toISOString(),
        timezone: Intl.DateTimeFormat().resolvedOptions().timeZone,
      },
      {
        onSuccess: () => toast.success('Đã đổi lịch đăng'),
        // Không tự sửa state cục bộ — invalidate của mutation sẽ kéo lại dữ liệu thật,
        // nên bài tự nhảy về chỗ cũ khi lỗi.
        onError: (err) => toast.error(getErrorMessage(err)),
      },
    )
  }

  return (
    <div>
      <PageHeader
        title="Lịch đăng bài"
        description="Xem toàn cảnh bài đã lên lịch và đã đăng theo tháng. Kéo-thả bài đã lên lịch sang ngày khác để đổi lịch."
      />

      <div className="card" style={{ marginBottom: 16 }}>
        <div
          className="card-body"
          style={{ display: 'flex', flexWrap: 'wrap', gap: 16, alignItems: 'center' }}
        >
          <div style={{ display: 'flex', gap: 14, flexWrap: 'wrap' }}>
            {STATUS_FILTERS.map((filter) => (
              <label
                key={filter.key}
                style={{ display: 'inline-flex', alignItems: 'center', gap: 6, cursor: 'pointer' }}
              >
                <input
                  type="checkbox"
                  checked={activeFilters.includes(filter.key)}
                  onChange={() => toggleFilter(filter.key)}
                />
                <span>{filter.label}</span>
              </label>
            ))}
          </div>

          <div style={{ marginLeft: 'auto', minWidth: 240 }}>
            <ChannelMultiSelect
              label=""
              placeholder="Tất cả kênh"
              channels={channels}
              value={channelIds}
              onChange={setChannelIds}
              getBadge={(channel) => ({
                label: getSocialPlatformLabel(channel.platform),
                title: getSocialPlatformLabel(channel.platform),
                tone: 'ok',
              })}
              maxHeight={280}
            />
          </div>
        </div>
      </div>

      {isLoading && <LoadingState />}
      {isError && <ErrorState message={getErrorMessage(error)} onRetry={refetch} />}

      {!isLoading && !isError && (
        <PostCalendar
          year={year}
          month={month}
          posts={posts}
          channelMap={channelMap}
          onPrevMonth={() => shiftMonth(-1)}
          onNextMonth={() => shiftMonth(1)}
          onToday={() => setCursor(currentVnMonth())}
          onReschedule={handleReschedule}
          isRescheduling={scheduleMutation.isPending}
        />
      )}
    </div>
  )
}
