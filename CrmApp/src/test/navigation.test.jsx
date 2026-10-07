import React from 'react'
import { describe, it, expect, beforeEach } from 'vitest'
import { render, screen, fireEvent } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import App from '../App'
import { useAuthStore } from '../auth/authStore'

describe('CRM Navigation & Menu', () => {
  beforeEach(() => {
    useAuthStore.getState().setAuth('mock-token', {
      email: 'admin@vni.local',
      userName: 'AdminUser',
      roles: ['Admin'],
    })
  })

  it('renders all 4 main CRM navigation items and navigates between them', () => {
    render(
      <MemoryRouter initialEntries={['/inbox']}>
        <App />
      </MemoryRouter>,
    )

    // Check presence of all 4 links in sidebar
    const inboxNav = screen.getByTestId('nav-inbox')
    const customersNav = screen.getByTestId('nav-customers')
    const tasksNav = screen.getByTestId('nav-tasks')
    const settingsNav = screen.getByTestId('nav-settings')

    expect(inboxNav).toHaveTextContent('Hộp thư')
    expect(customersNav).toHaveTextContent('Khách hàng')
    expect(tasksNav).toHaveTextContent('Việc của tôi')
    expect(settingsNav).toHaveTextContent('Cài đặt')

    // Navigate to Khách hàng
    fireEvent.click(customersNav)
    expect(screen.getByRole('heading', { level: 2, name: 'Hồ sơ Khách hàng' })).toBeInTheDocument()

    // Navigate to Việc của tôi
    fireEvent.click(tasksNav)
    expect(screen.getByRole('heading', { level: 2, name: 'Việc của tôi' })).toBeInTheDocument()

    // Navigate to Cài đặt
    fireEvent.click(settingsNav)
    expect(screen.getByRole('heading', { level: 2, name: 'Cài đặt hệ thống' })).toBeInTheDocument()
  })
})
