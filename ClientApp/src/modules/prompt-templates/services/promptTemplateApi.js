import axiosInstance from '@/api/axiosInstance'

export const promptTemplateApi = {
  getAll: () => axiosInstance.get('/api/PromptTemplate'),
  getById: (id) => axiosInstance.get(`/api/PromptTemplate/${id}`),
  filter: (params) => axiosInstance.post('/api/PromptTemplate/filter', params),
  create: (payload) => axiosInstance.post('/api/PromptTemplate', payload),
  update: (id, payload) => axiosInstance.put(`/api/PromptTemplate/${id}`, payload),
  softDelete: (id) => axiosInstance.delete(`/api/PromptTemplate/${id}`),
  getVariables: () => axiosInstance.get('/api/PromptTemplate/variables'),
}

export const promptTemplateQueryKeys = {
  all: ['prompt-templates'],
  list: (params) => ['prompt-templates', 'list', params],
  detail: (id) => ['prompt-templates', 'detail', id],
  variables: ['prompt-templates', 'variables'],
}
