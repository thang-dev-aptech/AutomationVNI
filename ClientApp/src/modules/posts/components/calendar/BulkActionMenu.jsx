import { useEffect, useRef, useState } from 'react'
import { useMutation } from '@tanstack/react-query'
import { confirmAction } from '@/shared/utils/confirmAction'
import { getErrorMessage, unwrapApiData } from '@/shared/utils/apiHelpers'
import { toast } from '@/shared/stores/toastStore'
import { postApi } from '../../services/postApi'

export const BULK_ACTIONS = [
  {
    key: 'cancelSchedule',
    label: 'Huỷ lịch',
    confirm: (n) => `Huỷ lịch đăng của ${n} bài đã chọn?`,
  },
  {
    key: 'publishNow',
    label: 'Đăng ngay',
    confirm: (n) => `Đăng ngay ${n} bài đã chọn?`,
  },
  {
    key: 'delete',
    label: 'Xoá',
    confirm: (n) => `Xoá mềm ${n} bài đã chọn? Thao tác không hoàn tác từ đây.`,
  },
]

/**
 * Nút "Hành động hàng loạt" + menu 3 action + panel kết quả từng bài.
 * Gọi POST /api/Post/bulk-action; không tắt toàn bộ khi một bài lỗi.
 */
export default function BulkActionMenu({
  selectedIds = [],
  postsById = {},
  onDone,
  disabled = false,
}) {
  const [open, setOpen] = useState(false)
  const [results, setResults] = useState(null)
  const rootRef = useRef(null)

  const mutation = useMutation({
    mutationFn: async ({ action, postIds }) =>
      unwrapApiData(await postApi.bulkAction({ action, postIds })),
  })

  useEffect(() => {
    if (!open) return undefined
    function onDocClick(e) {
      if (rootRef.current && !rootRef.current.contains(e.target)) setOpen(false)
    }
    document.addEventListener('mousedown', onDocClick)
    return () => document.removeEventListener('mousedown', onDocClick)
  }, [open])

  const count = selectedIds.length
  const isDisabled = disabled || count === 0 || mutation.isPending

  async function runAction(actionDef) {
    setOpen(false)
    if (count === 0) return
    if (count > 100) {
      toast.error('Tối đa 100 bài mỗi lượt')
      return
    }
    const ok = await confirmAction(actionDef.confirm(count))
    if (!ok) return

    try {
      const data = await mutation.mutateAsync({
        action: actionDef.key,
        postIds: selectedIds,
      })
      const list = data?.results ?? data?.Results ?? []
      setResults(list)
      onDone?.(list)
    } catch (err) {
      toast.error(getErrorMessage(err))
    }
  }

  return (
    <div className="bulk-action-menu" ref={rootRef} data-testid="bulk-action-menu">
      <button
        type="button"
        className="btn btn-secondary btn-sm"
        disabled={isDisabled}
        aria-haspopup="menu"
        aria-expanded={open}
        onClick={() => setOpen((v) => !v)}
        data-testid="bulk-action-trigger"
      >
        Hành động hàng loạt{count > 0 ? ` (${count})` : ''}
      </button>

      {open ? (
        <ul className="bulk-action-dropdown" role="menu">
          {BULK_ACTIONS.map((action) => (
            <li key={action.key} role="none">
              <button
                type="button"
                role="menuitem"
                className="bulk-action-item"
                onClick={() => runAction(action)}
                data-testid={`bulk-action-${action.key}`}
              >
                {action.label}
              </button>
            </li>
          ))}
        </ul>
      ) : null}

      {results ? (
        <div className="bulk-action-results" data-testid="bulk-action-results" role="status">
          <div className="bulk-action-results-head">
            <strong>Kết quả hàng loạt</strong>
            <button
              type="button"
              className="btn btn-ghost btn-sm"
              onClick={() => setResults(null)}
            >
              Đóng
            </button>
          </div>
          <ul>
            {results.map((row) => {
              const id = row.postId ?? row.PostId
              const success = row.success ?? row.Success
              const message = row.message ?? row.Message ?? (success ? 'Thành công' : 'Lỗi')
              const title = postsById[id]?.title || id
              return (
                <li
                  key={String(id)}
                  className={success ? 'is-ok' : 'is-fail'}
                  data-testid={`bulk-result-${id}`}
                >
                  <span className="bulk-result-title">{title}</span>
                  <span className="bulk-result-msg">{message}</span>
                </li>
              )
            })}
          </ul>
        </div>
      ) : null}
    </div>
  )
}
