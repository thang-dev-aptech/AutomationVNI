import crmApi from '../../../api/crmApi'

export const scheduledMessageApi = {
  listByConversation: async (conversationId) => {
    const res = await crmApi.get(`/CrmScheduledMessage/by-conversation/${conversationId}`)
    return res.data?.data || res.data || []
  },

  create: async ({ pageConversationId, text, scheduledAtUtc }) => {
    const res = await crmApi.post('/CrmScheduledMessage', {
      pageConversationId,
      text,
      scheduledAtUtc,
    })
    return res.data?.data || res.data
  },

  update: async (id, { text, scheduledAtUtc }) => {
    const res = await crmApi.put(`/CrmScheduledMessage/${id}`, {
      text,
      scheduledAtUtc,
    })
    return res.data?.data || res.data
  },

  cancel: async (id) => {
    const res = await crmApi.post(`/CrmScheduledMessage/${id}/cancel`)
    return res.data?.data || res.data
  },
}

export default scheduledMessageApi
