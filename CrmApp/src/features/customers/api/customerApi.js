import crmApi from '../../../api/crmApi'

export const customerApi = {
  filter: async (request = {}) => {
    const res = await crmApi.post('/CrmCustomer/filter', request)
    return res.data?.data || res.data
  },

  get: async (id) => {
    const res = await crmApi.get(`/CrmCustomer/${id}`)
    return res.data?.data || res.data
  },

  create: async (data) => {
    const res = await crmApi.post('/CrmCustomer', data)
    return res.data?.data || res.data
  },

  update: async (id, data) => {
    const res = await crmApi.put(`/CrmCustomer/${id}`, data)
    return res.data?.data || res.data
  },

  softDelete: async (id) => {
    const res = await crmApi.delete(`/CrmCustomer/${id}`)
    return res.data?.data || res.data
  },

  hardDelete: async (id) => {
    const res = await crmApi.delete(`/CrmCustomer/${id}/hard`)
    return res.data?.data || res.data
  },

  getTimeline: async (id) => {
    const res = await crmApi.get(`/CrmCustomer/${id}/timeline`)
    return res.data?.data || res.data
  },

  listNotes: async (id) => {
    const res = await crmApi.get(`/CrmCustomer/${id}/notes`)
    return res.data?.data || res.data
  },

  addNote: async (id, data) => {
    const res = await crmApi.post(`/CrmCustomer/${id}/notes`, data)
    return res.data?.data || res.data
  },

  updateNote: async (id, noteId, data) => {
    const res = await crmApi.put(`/CrmCustomer/${id}/notes/${noteId}`, data)
    return res.data?.data || res.data
  },

  deleteNote: async (id, noteId) => {
    const res = await crmApi.delete(`/CrmCustomer/${id}/notes/${noteId}`)
    return res.data?.data || res.data
  },

  confirmPhone: async (id, data) => {
    const res = await crmApi.post(`/CrmCustomer/${id}/phone/confirm`, data)
    return res.data?.data || res.data
  },

  listMergeSuggestions: async () => {
    const res = await crmApi.get('/CrmCustomer/merge-suggestions')
    return res.data?.data || res.data
  },

  merge: async (keptId, sourceCustomerId) => {
    const res = await crmApi.post(`/CrmCustomer/${keptId}/merge`, { sourceCustomerId })
    return res.data?.data || res.data
  },

  split: async (mergeRecordId) => {
    const res = await crmApi.post(`/CrmCustomer/merge/${mergeRecordId}/split`)
    return res.data?.data || res.data
  },

  importPreview: async (file) => {
    const formData = new FormData()
    formData.append('file', file)
    const res = await crmApi.post('/CrmCustomer/import/preview', formData, {
      headers: { 'Content-Type': 'multipart/form-data' },
    })
    return res.data?.data || res.data
  },

  importCommit: async (file) => {
    const formData = new FormData()
    formData.append('file', file)
    const res = await crmApi.post('/CrmCustomer/import/commit', formData, {
      headers: { 'Content-Type': 'multipart/form-data' },
    })
    return res.data?.data || res.data
  },

  exportCsv: async () => {
    const res = await crmApi.get('/CrmCustomer/export', { responseType: 'blob' })
    return res.data
  },
}

export default customerApi
