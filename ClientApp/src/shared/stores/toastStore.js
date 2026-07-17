import { create } from 'zustand'

const TOAST_DURATION_MS = 4000

export const useToastStore = create((set, get) => ({
  toasts: [],

  add(toast) {
    const id = crypto.randomUUID()
    set({ toasts: [...get().toasts, { id, ...toast }] })

    window.setTimeout(() => {
      set({ toasts: get().toasts.filter((item) => item.id !== id) })
    }, TOAST_DURATION_MS)
  },

  remove(id) {
    set({ toasts: get().toasts.filter((item) => item.id !== id) })
  },
}))

export const toast = {
  success(message) {
    useToastStore.getState().add({ type: 'success', message })
  },
  error(message) {
    useToastStore.getState().add({ type: 'error', message })
  },
}
