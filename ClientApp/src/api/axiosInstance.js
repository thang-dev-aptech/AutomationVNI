import axios from 'axios'
import { appConfig } from '@/shared/config/appConfig'
import { STORAGE_KEYS } from '@/shared/constants/storageKeys'
import { useAuthStore } from '@/modules/auth/stores/authStore'

const axiosInstance = axios.create({
  baseURL: appConfig.apiBaseUrl,
  headers: {
    'Content-Type': 'application/json',
  },
})

axiosInstance.interceptors.request.use((config) => {
  const token = localStorage.getItem(STORAGE_KEYS.ACCESS_TOKEN)
  if (token) {
    config.headers.Authorization = `Bearer ${token}`
  }
  return config
})

axiosInstance.interceptors.response.use(
  (response) => response,
  (error) => {
    if (error.response?.status === 401) {
      useAuthStore.getState().clearAuth()
      const isLoginPage = window.location.pathname.startsWith('/login')
      if (!isLoginPage) {
        window.location.assign('/login')
      }
    }
    return Promise.reject(error)
  },
)

export default axiosInstance
