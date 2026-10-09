import crmApi from './crmApi'

export const crmAuthApi = {
  login: async ({ email, password }) => {
    const response = await crmApi.post('/Auth/login', { email, password })
    return response.data
  },

  getMe: async () => {
    const response = await crmApi.get('/Auth/me')
    return response.data
  },

  logout: async () => {
    try {
      const response = await crmApi.post('/Auth/logout')
      return response.data
    } catch {
      return { success: true }
    }
  },
}

export default crmAuthApi
