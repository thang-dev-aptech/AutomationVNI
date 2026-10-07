import crmApi from '../../../api/crmApi'

export const reminderApi = {
  getBuckets: async ({ all = false, assigneeUserId = null } = {}) => {
    const params = new URLSearchParams()
    if (all) params.append('all', 'true')
    if (assigneeUserId) params.append('assigneeUserId', assigneeUserId)
    const queryString = params.toString() ? `?${params.toString()}` : ''
    const res = await crmApi.get(`/CrmReminder/buckets${queryString}`)
    return res.data?.data || res.data
  },

  listForCustomer: async (customerId) => {
    const res = await crmApi.get(`/CrmReminder/by-customer/${customerId}`)
    return res.data?.data || res.data
  },

  create: async (data) => {
    const res = await crmApi.post('/CrmReminder', data)
    return res.data?.data || res.data
  },

  update: async (id, data) => {
    const res = await crmApi.put(`/CrmReminder/${id}`, data)
    return res.data?.data || res.data
  },

  complete: async (id) => {
    const res = await crmApi.post(`/CrmReminder/${id}/complete`)
    return res.data?.data || res.data
  },

  delete: async (id) => {
    const res = await crmApi.delete(`/CrmReminder/${id}`)
    return res.data?.data || res.data
  },
}

export default reminderApi
