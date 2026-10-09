import React from 'react'
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { render, screen, fireEvent, waitFor } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import LoginPage from '../modules/auth/LoginPage'
import { useAuthStore } from '../auth/authStore'
import { crmApi, CRM_TOKEN_KEY, CRM_USER_KEY, getStoredToken, getStoredUser } from '../api/crmApi'

describe('AC crm-auth-roles-test (42a67d24) - Auth & 401 flow', () => {
  beforeEach(() => {
    localStorage.clear()
    useAuthStore.getState().clearAuth()
    vi.restoreAllMocks()
  })

  afterEach(() => {
    localStorage.clear()
    vi.restoreAllMocks()
  })

  it('calls /api/Auth/login with credentials and stores token on origin', async () => {
    const postSpy = vi.spyOn(crmApi, 'post').mockResolvedValueOnce({
      data: {
        success: true,
        data: {
          accessToken: 'mock-crm-jwt-token-xyz',
          expiresAt: '2026-10-08T00:00:00Z',
          user: {
            id: 'user-1',
            email: 'admin@vni.local',
            userName: 'AdminUser',
            roles: ['Admin'],
          },
        },
        message: 'Đăng nhập thành công',
      },
    })

    render(
      <MemoryRouter>
        <LoginPage />
      </MemoryRouter>,
    )

    const emailInput = screen.getByTestId('email-input')
    const passwordInput = screen.getByTestId('password-input')
    const submitBtn = screen.getByTestId('login-submit-btn')

    fireEvent.change(emailInput, { target: { value: 'admin@vni.local' } })
    fireEvent.change(passwordInput, { target: { value: 'Admin@123' } })
    fireEvent.click(submitBtn)

    await waitFor(() => {
      expect(postSpy).toHaveBeenCalledWith('/Auth/login', {
        email: 'admin@vni.local',
        password: 'Admin@123',
      })
    })

    await waitFor(() => {
      expect(getStoredToken()).toBe('mock-crm-jwt-token-xyz')
      expect(getStoredUser()).toEqual({
        id: 'user-1',
        email: 'admin@vni.local',
        userName: 'AdminUser',
        roles: ['Admin'],
      })
      expect(useAuthStore.getState().isAuthenticated).toBe(true)
    })
  })

  it('displays error alert when login fails', async () => {
    vi.spyOn(crmApi, 'post').mockRejectedValueOnce({
      response: {
        status: 401,
        data: {
          success: false,
          message: 'Email hoặc mật khẩu không đúng',
        },
      },
    })

    render(
      <MemoryRouter>
        <LoginPage />
      </MemoryRouter>,
    )

    fireEvent.change(screen.getByTestId('email-input'), { target: { value: 'wrong@vni.local' } })
    fireEvent.change(screen.getByTestId('password-input'), { target: { value: 'wrongpass' } })
    fireEvent.click(screen.getByTestId('login-submit-btn'))

    await waitFor(() => {
      expect(screen.getByTestId('login-error-alert')).toHaveTextContent('Email hoặc mật khẩu không đúng')
    })
    expect(useAuthStore.getState().isAuthenticated).toBe(false)
  })

  it('clears stored token and resets auth session on 401 response', async () => {
    // Seed authenticated session
    localStorage.setItem(CRM_TOKEN_KEY, 'stale-token')
    localStorage.setItem(CRM_USER_KEY, JSON.stringify({ email: 'user@vni.local', roles: ['Reviewer'] }))
    useAuthStore.getState().setAuth('stale-token', { email: 'user@vni.local', roles: ['Reviewer'] })

    expect(getStoredToken()).toBe('stale-token')
    expect(useAuthStore.getState().isAuthenticated).toBe(true)

    // Trigger unauthorized event directly simulating 401 interceptor
    window.dispatchEvent(new CustomEvent('crm:unauthorized'))

    await waitFor(() => {
      expect(getStoredToken()).toBeNull()
      expect(useAuthStore.getState().isAuthenticated).toBe(false)
      expect(useAuthStore.getState().user).toBeNull()
    })
  })
})
