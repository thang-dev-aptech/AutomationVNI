import React from 'react'
import { Routes, Route, Navigate, useLocation } from 'react-router-dom'
import { useAuth } from './auth/useAuth'
import CrmLayout from './layouts/CrmLayout'
import LoginPage from './modules/auth/LoginPage'
import InboxPage from './modules/inbox/InboxPage'
import CustomersPage from './modules/customers/CustomersPage'
import TasksPage from './modules/tasks/TasksPage'
import SettingsPage from './modules/settings/SettingsPage'

const ProtectedRoute = () => {
  const { isAuthenticated } = useAuth()
  const location = useLocation()

  if (!isAuthenticated) {
    return <Navigate to="/login" state={{ from: location }} replace />
  }

  return <CrmLayout />
}

export const App = () => {
  return (
    <Routes>
      <Route path="/login" element={<LoginPage />} />

      <Route element={<ProtectedRoute />}>
        <Route index element={<Navigate to="/inbox" replace />} />
        <Route path="/inbox" element={<InboxPage />} />
        <Route path="/customers" element={<CustomersPage />} />
        <Route path="/customers/:id" element={<CustomersPage />} />
        <Route path="/tasks" element={<TasksPage />} />
        <Route path="/settings" element={<SettingsPage />} />
      </Route>

      <Route path="*" element={<Navigate to="/inbox" replace />} />
    </Routes>
  )
}

export default App
