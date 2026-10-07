import axios from 'axios'

export const CRM_TOKEN_KEY = 'crm_access_token'
export const CRM_USER_KEY = 'crm_user'

export const getStoredToken = () => {
  try {
    return localStorage.getItem(CRM_TOKEN_KEY)
  } catch {
    return null
  }
}

export const setStoredToken = (token) => {
  try {
    if (token) {
      localStorage.setItem(CRM_TOKEN_KEY, token)
    } else {
      localStorage.removeItem(CRM_TOKEN_KEY)
    }
  } catch (err) {
    console.error('Failed to save token to localStorage', err)
  }
}

export const getStoredUser = () => {
  try {
    const raw = localStorage.getItem(CRM_USER_KEY)
    return raw ? JSON.parse(raw) : null
  } catch {
    return null
  }
}

export const setStoredUser = (user) => {
  try {
    if (user) {
      localStorage.setItem(CRM_USER_KEY, JSON.stringify(user))
    } else {
      localStorage.removeItem(CRM_USER_KEY)
    }
  } catch (err) {
    console.error('Failed to save user to localStorage', err)
  }
}

export const clearStoredAuth = () => {
  try {
    localStorage.removeItem(CRM_TOKEN_KEY)
    localStorage.removeItem(CRM_USER_KEY)
  } catch (err) {
    console.error('Failed to clear auth from localStorage', err)
  }
}

export const crmApi = axios.create({
  baseURL: '/api',
  headers: {
    'Content-Type': 'application/json',
  },
})

// Request Interceptor: Attach token if available
crmApi.interceptors.request.use(
  (config) => {
    const token = getStoredToken()
    if (token) {
      config.headers = config.headers || {}
      config.headers.Authorization = `Bearer ${token}`
    }
    return config
  },
  (error) => Promise.reject(error),
)

// Response Interceptor: Catch 401 and redirect to /login
crmApi.interceptors.response.use(
  (response) => response,
  (error) => {
    if (error?.response?.status === 401) {
      clearStoredAuth()
      if (typeof window !== 'undefined') {
        window.dispatchEvent(new CustomEvent('crm:unauthorized'))
        if (window.location.pathname !== '/login') {
          window.location.href = '/login'
        }
      }
    }
    return Promise.reject(error)
  },
)

export default crmApi
