import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { BrowserRouter } from 'react-router-dom'
import ToastContainer from '@/shared/components/ToastContainer'
import AuthBootstrap from './AuthBootstrap'

const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      retry: 1,
      refetchOnWindowFocus: false,
      staleTime: 30_000,
    },
  },
})

export default function AppProviders({ children }) {
  return (
    <QueryClientProvider client={queryClient}>
      <BrowserRouter>
        <AuthBootstrap>
          {children}
          <ToastContainer />
        </AuthBootstrap>
      </BrowserRouter>
    </QueryClientProvider>
  )
}
