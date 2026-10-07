import React from 'react'
import { describe, it, expect, beforeEach } from 'vitest'
import { render, screen, fireEvent } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import CrmLayout from '../layouts/CrmLayout'
import { useAuthStore } from '../auth/authStore'

describe('CRM Mobile Responsive (375px)', () => {
  beforeEach(() => {
    useAuthStore.getState().setAuth('mock-token', {
      email: 'admin@vni.local',
      userName: 'AdminUser',
      roles: ['Admin'],
    })
  })

  it('renders mobile menu button and toggles sidebar open/closed state', () => {
    render(
      <MemoryRouter>
        <CrmLayout />
      </MemoryRouter>,
    )

    const toggleBtn = screen.getByTestId('mobile-menu-btn')
    const sidebar = screen.getByTestId('crm-sidebar')

    expect(sidebar).not.toHaveClass('crm-sidebar-open')

    // Click hamburger button to open
    fireEvent.click(toggleBtn)
    expect(sidebar).toHaveClass('crm-sidebar-open')

    // Click again to close
    fireEvent.click(toggleBtn)
    expect(sidebar).not.toHaveClass('crm-sidebar-open')
  })
})
