import { useToastStore } from '@/shared/stores/toastStore'
import './Toast.css'

export default function ToastContainer() {
  const toasts = useToastStore((state) => state.toasts)
  const remove = useToastStore((state) => state.remove)

  if (toasts.length === 0) return null

  return (
    <div className="toast-container" aria-live="polite">
      {toasts.map((item) => (
        <div
          key={item.id}
          className={`toast toast--${item.type}`}
          role="status"
        >
          <span>{item.message}</span>
          <button
            type="button"
            className="toast-close"
            aria-label="Đóng"
            onClick={() => remove(item.id)}
          >
            ×
          </button>
        </div>
      ))}
    </div>
  )
}
