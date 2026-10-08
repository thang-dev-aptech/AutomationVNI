export const toast = {
  error: (msg) => {
    if (typeof window !== 'undefined') {
      window.dispatchEvent(
        new CustomEvent('crm:toast', { detail: { type: 'error', message: msg } }),
      )
    }
  },
  success: (msg) => {
    if (typeof window !== 'undefined') {
      window.dispatchEvent(
        new CustomEvent('crm:toast', { detail: { type: 'success', message: msg } }),
      )
    }
  },
  info: (msg) => {
    if (typeof window !== 'undefined') {
      window.dispatchEvent(
        new CustomEvent('crm:toast', { detail: { type: 'info', message: msg } }),
      )
    }
  },
}

export default toast
