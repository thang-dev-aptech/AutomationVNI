import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { unwrapApiData } from '@/shared/utils/apiHelpers'
import { promptTemplateApi, promptTemplateQueryKeys } from '../services/promptTemplateApi'

export function usePromptTemplateList(params = { index: 1, size: 100 }) {
  return useQuery({
    queryKey: promptTemplateQueryKeys.list(params),
    queryFn: async () => unwrapApiData(await promptTemplateApi.filter(params)),
  })
}

export function useCreatePromptTemplate() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async (payload) => unwrapApiData(await promptTemplateApi.create(payload)),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: promptTemplateQueryKeys.all }),
  })
}

export function useUpdatePromptTemplate() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async ({ id, payload }) =>
      unwrapApiData(await promptTemplateApi.update(id, payload)),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: promptTemplateQueryKeys.all }),
  })
}

export function useDeletePromptTemplate() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (id) => promptTemplateApi.softDelete(id),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: promptTemplateQueryKeys.all }),
  })
}
