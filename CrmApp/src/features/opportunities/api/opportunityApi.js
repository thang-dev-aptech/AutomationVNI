import crmApi from '../../../api/crmApi'

export const opportunityApi = {
  filter: async (request = {}) => {
    const res = await crmApi.post('/CrmOpportunity/filter', request)
    return res.data?.data || res.data
  },

  stats: async (request = {}) => {
    const res = await crmApi.post('/CrmOpportunity/stats', request)
    return res.data?.data || res.data
  },

  pipeline: async (request = {}) => {
    const res = await crmApi.post('/CrmOpportunity/pipeline', request)
    return res.data?.data || res.data
  },

  pipelineStage: async (stageId, request = {}) => {
    const res = await crmApi.post(`/CrmOpportunity/pipeline/${stageId}`, request)
    return res.data?.data || res.data
  },

  get: async (id) => {
    const res = await crmApi.get(`/CrmOpportunity/${id}`)
    return res.data?.data || res.data
  },

  create: async (data) => {
    const res = await crmApi.post('/CrmOpportunity', data)
    return res.data?.data || res.data
  },

  update: async (id, data) => {
    const res = await crmApi.put(`/CrmOpportunity/${id}`, data)
    return res.data?.data || res.data
  },

  moveStage: async (id, stageIdOrData, lostReason) => {
    const payload =
      typeof stageIdOrData === 'object' && stageIdOrData !== null
        ? stageIdOrData
        : { stageId: stageIdOrData, lostReason: lostReason || null }
    const res = await crmApi.post(`/CrmOpportunity/${id}/move-stage`, payload)
    return res.data?.data || res.data
  },

  assign: async (id, data) => {
    const res = await crmApi.post(`/CrmOpportunity/${id}/assign`, data)
    return res.data?.data || res.data
  },

  addWatcher: async (id, userId) => {
    const res = await crmApi.post(`/CrmOpportunity/${id}/watchers/${userId}`)
    return res.data?.data || res.data
  },

  removeWatcher: async (id, userId) => {
    const res = await crmApi.delete(`/CrmOpportunity/${id}/watchers/${userId}`)
    return res.data?.data || res.data
  },

  archive: async (id) => {
    const res = await crmApi.post(`/CrmOpportunity/${id}/archive`)
    return res.data?.data || res.data
  },

  unarchive: async (id) => {
    const res = await crmApi.post(`/CrmOpportunity/${id}/unarchive`)
    return res.data?.data || res.data
  },

  delete: async (id) => {
    const res = await crmApi.delete(`/CrmOpportunity/${id}`)
    return res.data?.data || res.data
  },

  fromConversation: async (data) => {
    const res = await crmApi.post('/CrmOpportunity/from-conversation', data)
    return res.data?.data || res.data
  },

  // Backend trả {success:true, data:null} khi hội thoại chưa có cơ hội: null là giá trị hợp lệ,
  // KHÔNG được rơi về cả envelope (`|| res.data` làm UI luôn tưởng đã có cơ hội).
  byConversation: async (kind, id) => {
    const res = await crmApi.get(`/CrmOpportunity/by-conversation/${kind}/${id}`)
    const body = res.data
    if (body && typeof body === 'object' && 'data' in body) return body.data ?? null
    return body ?? null
  },

  listStages: async () => {
    const res = await crmApi.get('/CrmOpportunityStage')
    return res.data?.data || res.data || []
  },

  listUsers: async () => {
    const res = await crmApi.get('/Users')
    return res.data?.data || res.data || []
  },
}

export default opportunityApi
