import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { unwrapApiData } from '@/shared/utils/apiHelpers'
import { useAuthStore } from '../stores/authStore'
import { authApi } from '../services/authApi'

export const authQueryKeys = {
  me: ['auth', 'me'],
}

export function useCurrentUser() {
  const accessToken = useAuthStore((state) => state.accessToken)
  const setAuth = useAuthStore((state) => state.setAuth)

  return useQuery({
    queryKey: authQueryKeys.me,
    queryFn: async () => {
      const user = unwrapApiData(await authApi.me())
      setAuth({ accessToken, user })
      return user
    },
    enabled: Boolean(accessToken),
    retry: false,
  })
}

export function useLogin() {
  const setAuth = useAuthStore((state) => state.setAuth)
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: async (payload) => unwrapApiData(await authApi.login(payload)),
    onSuccess: (data) => {
      setAuth({
        accessToken: data.accessToken,
        user: data.user,
      })
      queryClient.setQueryData(authQueryKeys.me, data.user)
    },
  })
}

export function useLogout() {
  const clearAuth = useAuthStore((state) => state.clearAuth)
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: async () => {
      try {
        await authApi.logout()
      } catch {
        // JWT stateless — vẫn clear client dù API lỗi
      }
    },
    onSettled: () => {
      clearAuth()
      queryClient.clear()
    },
  })
}
