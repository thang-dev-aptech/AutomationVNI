import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { unwrapApiData } from '@/shared/utils/apiHelpers'
import { pageContextApi, pageContextQueryKeys } from '../services/pageContextApi'

export function usePageContextList(params = { index: 1, size: 50 }) {
  return useQuery({
    queryKey: pageContextQueryKeys.list(params),
    queryFn: async () => unwrapApiData(await pageContextApi.filter(params)),
  })
}

export function useCreatePageContext() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async (payload) => unwrapApiData(await pageContextApi.create(payload)),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: pageContextQueryKeys.all }),
  })
}

export function useUpdatePageContext() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async ({ id, payload }) =>
      unwrapApiData(await pageContextApi.update(id, payload)),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: pageContextQueryKeys.all }),
  })
}

export function useDeletePageContext() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (id) => pageContextApi.softDelete(id),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: pageContextQueryKeys.all }),
  })
}

/** Import nhiều Page Context (JSON). Trả { created, skipped, errors }. */
export function useImportPageContexts() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async (payload) => unwrapApiData(await pageContextApi.import(payload)),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: pageContextQueryKeys.all }),
  })
}
