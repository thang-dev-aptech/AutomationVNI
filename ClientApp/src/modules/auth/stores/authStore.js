import { create } from 'zustand'
import { STORAGE_KEYS } from '@/shared/constants/storageKeys'

function readStoredUser() {
  try {
    const raw = localStorage.getItem(STORAGE_KEYS.CURRENT_USER)
    return raw ? JSON.parse(raw) : null
  } catch {
    return null
  }
}

const storedUser = readStoredUser()

export const useAuthStore = create((set) => ({
  accessToken: localStorage.getItem(STORAGE_KEYS.ACCESS_TOKEN),
  refreshToken: localStorage.getItem(STORAGE_KEYS.REFRESH_TOKEN),
  currentUser: storedUser,
  roles: storedUser?.roles ?? [],
  isAuthenticated: Boolean(localStorage.getItem(STORAGE_KEYS.ACCESS_TOKEN)),

  setAuth: ({ accessToken, refreshToken, user }) => {
    if (accessToken) {
      localStorage.setItem(STORAGE_KEYS.ACCESS_TOKEN, accessToken)
    }
    if (refreshToken) {
      localStorage.setItem(STORAGE_KEYS.REFRESH_TOKEN, refreshToken)
    } else {
      localStorage.removeItem(STORAGE_KEYS.REFRESH_TOKEN)
    }
    if (user) {
      localStorage.setItem(STORAGE_KEYS.CURRENT_USER, JSON.stringify(user))
    }
    set({
      accessToken: accessToken ?? null,
      refreshToken: refreshToken ?? null,
      currentUser: user ?? null,
      roles: user?.roles ?? [],
      isAuthenticated: Boolean(accessToken),
    })
  },

  clearAuth: () => {
    localStorage.removeItem(STORAGE_KEYS.ACCESS_TOKEN)
    localStorage.removeItem(STORAGE_KEYS.REFRESH_TOKEN)
    localStorage.removeItem(STORAGE_KEYS.CURRENT_USER)
    set({
      accessToken: null,
      refreshToken: null,
      currentUser: null,
      roles: [],
      isAuthenticated: false,
    })
  },
}))
