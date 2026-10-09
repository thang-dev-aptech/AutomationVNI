import { create } from 'zustand'
import { crmAuthApi } from '../api/crmAuthApi'
import {
  getStoredToken,
  setStoredToken,
  getStoredUser,
  setStoredUser,
  clearStoredAuth,
} from '../api/crmApi'

export const useAuthStore = create((set) => ({
  token: getStoredToken(),
  user: getStoredUser(),
  isAuthenticated: Boolean(getStoredToken()),
  isLoading: false,
  error: null,

  login: async ({ email, password }) => {
    set({ isLoading: true, error: null })
    try {
      const res = await crmAuthApi.login({ email, password })
      const authData = res?.data || res
      const token = authData.accessToken || authData.token
      const user = authData.user || null

      if (!token) {
        throw new Error('Không nhận được token từ hệ thống xác thực')
      }

      setStoredToken(token)
      setStoredUser(user)

      set({
        token,
        user,
        isAuthenticated: true,
        isLoading: false,
        error: null,
      })
      return authData
    } catch (err) {
      const message =
        err?.response?.data?.message ||
        err?.message ||
        'Đăng nhập thất bại. Vui lòng kiểm tra lại thông tin.'
      set({ isLoading: false, error: message })
      throw err
    }
  },

  logout: async () => {
    try {
      await crmAuthApi.logout()
    } finally {
      clearStoredAuth()
      set({
        token: null,
        user: null,
        isAuthenticated: false,
        isLoading: false,
        error: null,
      })
    }
  },

  setAuth: (token, user) => {
    setStoredToken(token)
    setStoredUser(user)
    set({
      token,
      user,
      isAuthenticated: Boolean(token),
      error: null,
    })
  },

  clearAuth: () => {
    clearStoredAuth()
    set({
      token: null,
      user: null,
      isAuthenticated: false,
      error: null,
    })
  },
}))

// Synchronize when a 401 occurs in crmApi
if (typeof window !== 'undefined') {
  window.addEventListener('crm:unauthorized', () => {
    useAuthStore.getState().clearAuth()
  })
}
