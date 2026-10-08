import crmApi from '../../../api/crmApi'

export const inboxApi = {
  filter: async (request = {}) => {
    const res = await crmApi.post('/CrmInbox/filter', request)
    return res.data?.data || res.data
  },

  getMessage: async (id) => {
    const res = await crmApi.get(`/CrmInbox/message/${id}`)
    return res.data?.data || res.data
  },

  getComment: async (id) => {
    const res = await crmApi.get(`/CrmInbox/comment/${id}`)
    return res.data?.data || res.data
  },

  sendMessage: async (id, text) => {
    const res = await crmApi.post(`/PageMessage/${id}/send`, { message: text })
    return res.data?.data || res.data
  },

  replyComment: async (id, text) => {
    const res = await crmApi.post(`/SocialComment/${id}/reply`, { message: text })
    return res.data?.data || res.data
  },

  setMessageStatus: async (id, status) => {
    const res = await crmApi.post(`/PageMessage/${id}/status`, { status })
    return res.data?.data || res.data
  },

  setCommentStatus: async (id, status) => {
    const res = await crmApi.post(`/SocialComment/${id}/status`, { status })
    return res.data?.data || res.data
  },

  assignMessage: async (id, { assignedUserId, assignedTo }) => {
    const res = await crmApi.post(`/PageMessage/${id}/assign`, { assignedUserId, assignedTo })
    return res.data?.data || res.data
  },

  assignComment: async (id, { assignedUserId, assignedTo }) => {
    const res = await crmApi.post(`/SocialComment/${id}/assign`, { assignedUserId, assignedTo })
    return res.data?.data || res.data
  },

  addMessageNote: async (id, note) => {
    const res = await crmApi.post(`/PageMessage/${id}/note`, { note })
    return res.data?.data || res.data
  },

  addCommentNote: async (id, note) => {
    const res = await crmApi.post(`/SocialComment/${id}/note`, { note })
    return res.data?.data || res.data
  },

  listTags: async () => {
    const res = await crmApi.get('/CrmTag')
    return res.data?.data || res.data
  },

  attachTag: async (tagId, targetType, targetId) => {
    const res = await crmApi.post(`/CrmTag/${tagId}/attach`, { targetType, targetId })
    return res.data?.data || res.data
  },

  detachTag: async (tagId, targetType, targetId) => {
    const res = await crmApi.post(`/CrmTag/${tagId}/detach`, { targetType, targetId })
    return res.data?.data || res.data
  },

  listUsers: async () => {
    const res = await crmApi.get('/Users')
    return res.data?.data || res.data
  },

  listChannels: async () => {
    const res = await crmApi.post('/SocialChannel/filter', { pageSize: 100 })
    return res.data?.data?.items || res.data?.items || res.data?.data || res.data || []
  },

  suggestReply: async (kind, id) => {
    const normalizedKind =
      String(kind).toLowerCase() === 'comment' || String(kind) === '2'
        ? 'comment'
        : 'message'
    const res = await crmApi.post(`/Inbox/${normalizedKind}/${id}/suggest-reply`)
    return res.data?.data || res.data
  },

  getCustomer: async (kind, id) => {
    const normalizedKind =
      String(kind).toLowerCase() === 'comment' || String(kind) === '2'
        ? 'comment'
        : 'message'
    const res = await crmApi.get(`/CrmInbox/${normalizedKind}/${id}/customer`)
    return res.data?.data || res.data
  },
}

