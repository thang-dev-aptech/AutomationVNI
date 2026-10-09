import crmApi from '../../../api/crmApi'

export const tagApi = {
  list: async () => {
    const res = await crmApi.get('/CrmTag')
    return res.data?.data ?? res.data ?? []
  },

  create: async (data) => {
    const res = await crmApi.post('/CrmTag', data)
    return res.data?.data ?? res.data
  },

  update: async (id, data) => {
    const res = await crmApi.put(`/CrmTag/${id}`, data)
    return res.data?.data ?? res.data
  },

  delete: async (id) => {
    const res = await crmApi.delete(`/CrmTag/${id}`)
    return res.data?.data ?? res.data
  },
}

export const autoAssignApi = {
  get: async () => {
    const res = await crmApi.get('/CrmAutoAssign')
    return res.data?.data ?? res.data ?? { isEnabled: false, assigneeUserIds: [], nextIndex: 0 }
  },

  update: async (data) => {
    const res = await crmApi.put('/CrmAutoAssign', data)
    return res.data?.data ?? res.data
  },
}

export const usersApi = {
  list: async () => {
    const res = await crmApi.get('/Users')
    return res.data?.data ?? res.data ?? []
  },
}

export const settingsApi = {
  tag: tagApi,
  autoAssign: autoAssignApi,
  users: usersApi,
}

export default settingsApi
