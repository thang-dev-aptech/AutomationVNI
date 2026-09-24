import { useRef, useState } from 'react'
import PageHeader from '@/shared/components/PageHeader'
import LoadingState from '@/shared/components/LoadingState'
import EmptyState from '@/shared/components/EmptyState'
import { usePermissions } from '@/shared/hooks/usePermissions'
import { getErrorMessage } from '@/shared/utils/apiHelpers'
import { toast } from '@/shared/stores/toastStore'
import MusicTrackCard from '../components/MusicTrackCard'
import { useDeleteMusicTrack, useMusicTrackAll, useUploadMusicTrack } from '../hooks/useMusicTracks'

const ACCEPTED_MIME = 'audio/mpeg,audio/wav,audio/mp4,audio/x-m4a,.mp3,.wav,.m4a'

export default function MusicLibraryPage() {
  const { canManageMedia } = usePermissions()
  const fileInputRef = useRef(null)
  const [displayName, setDisplayName] = useState('')

  const { data: tracks = [], isLoading, isError, error, refetch } = useMusicTrackAll()
  const uploadMutation = useUploadMusicTrack()
  const deleteMutation = useDeleteMusicTrack()

  const handleUpload = async (event) => {
    event.preventDefault()
    const file = fileInputRef.current?.files?.[0]
    if (!file) {
      toast.error('Chọn file nhạc trước')
      return
    }

    const formData = new FormData()
    formData.append('file', file)
    if (displayName.trim()) formData.append('displayName', displayName.trim())

    try {
      await uploadMutation.mutateAsync(formData)
      toast.success('Đã thêm nhạc vào thư viện')
      setDisplayName('')
      if (fileInputRef.current) fileInputRef.current.value = ''
    } catch (uploadError) {
      toast.error(getErrorMessage(uploadError))
    }
  }

  const handleDelete = async (track) => {
    if (!window.confirm(`Xóa bài nhạc "${track.displayName}"? Hành động này không thể hoàn tác.`)) return
    try {
      await deleteMutation.mutateAsync(track.id)
      toast.success('Đã xóa bài nhạc')
    } catch (deleteError) {
      toast.error(getErrorMessage(deleteError))
    }
  }

  return (
    <section className="music-library-page">
      <PageHeader
        title="Thư viện nhạc"
        description="Nhạc nền dùng khi ghép video Reels — chọn ở bước 'Đăng dạng Reels' của từng bài"
      />

      {canManageMedia && (
        <form
          onSubmit={handleUpload}
          className="card card-body"
          style={{ display: 'flex', gap: 12, alignItems: 'flex-end', flexWrap: 'wrap', marginBottom: 16 }}
        >
          <div className="form-group" style={{ marginBottom: 0 }}>
            <label htmlFor="music-file">File nhạc (mp3/wav/m4a)</label>
            <input id="music-file" ref={fileInputRef} type="file" accept={ACCEPTED_MIME} />
          </div>
          <div className="form-group" style={{ marginBottom: 0 }}>
            <label htmlFor="music-name">Tên hiển thị (tuỳ chọn)</label>
            <input
              id="music-name"
              value={displayName}
              onChange={(event) => setDisplayName(event.target.value)}
              placeholder="Để trống = dùng tên file"
            />
          </div>
          <button type="submit" className="btn btn-primary" disabled={uploadMutation.isPending}>
            {uploadMutation.isPending ? 'Đang tải lên...' : 'Thêm nhạc'}
          </button>
        </form>
      )}

      {isLoading && <LoadingState message="Đang tải..." />}

      {isError && (
        <EmptyState
          message={getErrorMessage(error, 'Không tải được thư viện nhạc')}
          action={<button type="button" className="btn btn-secondary" onClick={refetch}>Thử lại</button>}
        />
      )}

      {!isLoading && !isError && tracks.length === 0 && (
        <EmptyState message="Chưa có bài nhạc nào. Thêm nhạc để dùng khi ghép video Reels." />
      )}

      {!isLoading && !isError && tracks.length > 0 && (
        <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fill, minmax(320px, 1fr))', gap: 12 }}>
          {tracks.map((track) => (
            <MusicTrackCard
              key={track.id}
              track={track}
              canManage={canManageMedia}
              onDelete={handleDelete}
            />
          ))}
        </div>
      )}
    </section>
  )
}
