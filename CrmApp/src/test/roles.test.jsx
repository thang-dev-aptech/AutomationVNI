import React from 'react'
import { describe, it, expect, beforeEach, vi } from 'vitest'
import { render, screen, fireEvent } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { useAuthStore } from '../auth/authStore'
import InboxPage from '../modules/inbox/InboxPage'
import CustomersPage from '../modules/customers/CustomersPage'
import TasksBoard from '../features/tasks/components/TasksBoard'
import SettingsPage from '../modules/settings/SettingsPage'
import { reminderApi } from '../features/tasks/api/reminderApi'

describe('AC crm-auth-roles-test (42a67d24) - Role-based button visibility', () => {
  beforeEach(() => {
    useAuthStore.getState().clearAuth()
  })

  describe('Viewer (Chỉ đọc) role restrictions', () => {
    beforeEach(() => {
      useAuthStore.getState().setAuth('mock-token', {
        email: 'viewer@vni.local',
        userName: 'ViewerUser',
        roles: ['Viewer'],
      })
    })

    it('hides reply, notes and care action buttons in Inbox for Viewer', () => {
      render(
        <MemoryRouter>
          <InboxPage />
        </MemoryRouter>,
      )

      expect(screen.queryByTestId('reply-form')).not.toBeInTheDocument()
      expect(screen.queryByTestId('btn-send-reply')).not.toBeInTheDocument()
      expect(screen.queryByTestId('btn-add-note')).not.toBeInTheDocument()
      expect(screen.queryByTestId('btn-create-reminder')).not.toBeInTheDocument()
      expect(screen.getByTestId('viewer-readonly-notice')).toBeInTheDocument()
    })

    it('hides all create, merge, import, export, delete buttons in Customers for Viewer', () => {
      render(
        <MemoryRouter>
          <CustomersPage />
        </MemoryRouter>,
      )

      expect(screen.queryByTestId('btn-create-customer')).not.toBeInTheDocument()
      expect(screen.queryByTestId('btn-merge-customer')).not.toBeInTheDocument()
      expect(screen.queryByTestId('btn-import-csv')).not.toBeInTheDocument()
      expect(screen.queryByTestId('btn-export-customers')).not.toBeInTheDocument()
      expect(screen.queryByTestId('btn-delete-cust-1')).not.toBeInTheDocument()
    })

    it('hides create task and complete buttons in Tasks for Viewer', () => {
      render(
        <MemoryRouter>
          <TasksBoard />
        </MemoryRouter>,
      )

      expect(screen.queryByTestId('btn-create-task')).not.toBeInTheDocument()
      expect(screen.queryByTestId('btn-toggle-task-t-1')).not.toBeInTheDocument()
    })

    it('shows permission denied message in Settings tabs for Viewer', () => {
      render(
        <MemoryRouter>
          <SettingsPage />
        </MemoryRouter>,
      )

      fireEvent.click(screen.getByTestId('tab-tags'))
      expect(screen.getByTestId('tags-permission-denied')).toBeInTheDocument()
      expect(screen.queryByTestId('btn-add-tag')).not.toBeInTheDocument()

      fireEvent.click(screen.getByTestId('tab-routing'))
      expect(screen.getByTestId('routing-permission-denied')).toBeInTheDocument()
      expect(screen.queryByTestId('btn-save-routing')).not.toBeInTheDocument()
    })
  })

  describe('Reviewer (Chăm sóc khách hàng) role permissions', () => {
    beforeEach(() => {
      useAuthStore.getState().setAuth('mock-token', {
        email: 'reviewer@vni.local',
        userName: 'ReviewerUser',
        roles: ['Reviewer'],
      })
    })

    it('shows reply, note and reminder buttons in Inbox for Reviewer', () => {
      render(
        <MemoryRouter>
          <InboxPage />
        </MemoryRouter>,
      )

      expect(screen.getByTestId('reply-form')).toBeInTheDocument()
      expect(screen.getByTestId('btn-send-reply')).toBeInTheDocument()
      expect(screen.getByTestId('btn-add-note')).toBeInTheDocument()
      expect(screen.getByTestId('btn-create-reminder')).toBeInTheDocument()
      expect(screen.queryByTestId('viewer-readonly-notice')).not.toBeInTheDocument()
    })

    it('shows care buttons (create, merge) but hides management buttons (import CSV, export, delete) in Customers for Reviewer', () => {
      render(
        <MemoryRouter>
          <CustomersPage />
        </MemoryRouter>,
      )

      expect(screen.getByTestId('btn-create-customer')).toBeInTheDocument()
      expect(screen.getByTestId('btn-merge-customer')).toBeInTheDocument()

      // Management buttons must be hidden for Reviewer
      expect(screen.queryByTestId('btn-import-csv')).not.toBeInTheDocument()
      expect(screen.queryByTestId('btn-export-customers')).not.toBeInTheDocument()
      expect(screen.queryByTestId('btn-delete-cust-1')).not.toBeInTheDocument()
    })

    it('shows task creation and task completion toggle for Reviewer', async () => {
      vi.spyOn(reminderApi, 'getBuckets').mockResolvedValue({
        today: [
          {
            id: 't-1',
            title: 'Việc cần làm hôm nay',
            dueAtUtc: new Date().toISOString(),
            isCompleted: false,
          },
        ],
        overdue: [],
        upcoming: [],
      })

      render(
        <MemoryRouter>
          <TasksBoard />
        </MemoryRouter>,
      )

      expect(screen.getByTestId('btn-create-task')).toBeInTheDocument()
      expect(await screen.findByTestId('btn-toggle-task-t-1')).toBeInTheDocument()
    })

    it('shows permission denied for management settings tabs for Reviewer', () => {
      render(
        <MemoryRouter>
          <SettingsPage />
        </MemoryRouter>,
      )

      fireEvent.click(screen.getByTestId('tab-tags'))
      expect(screen.getByTestId('tags-permission-denied')).toBeInTheDocument()
      expect(screen.queryByTestId('btn-add-tag')).not.toBeInTheDocument()
    })
  })

  describe('Admin and ContentManager (Quản lý) role permissions', () => {
    beforeEach(() => {
      useAuthStore.getState().setAuth('mock-token', {
        email: 'admin@vni.local',
        userName: 'AdminUser',
        roles: ['Admin'],
      })
    })

    it('shows all care and management buttons in Customers for Admin', () => {
      render(
        <MemoryRouter>
          <CustomersPage />
        </MemoryRouter>,
      )

      expect(screen.getByTestId('btn-create-customer')).toBeInTheDocument()
      expect(screen.getByTestId('btn-merge-customer')).toBeInTheDocument()
      expect(screen.getByTestId('btn-import-csv')).toBeInTheDocument()
      expect(screen.getByTestId('btn-export-customers')).toBeInTheDocument()
      expect(screen.getByTestId('btn-delete-cust-1')).toBeInTheDocument()
    })

    it('enables tag management in Settings for Admin', () => {
      render(
        <MemoryRouter>
          <SettingsPage />
        </MemoryRouter>,
      )

      // Switch to tags tab
      fireEvent.click(screen.getByTestId('tab-tags'))
      expect(screen.getByTestId('btn-add-tag')).toBeInTheDocument()
      expect(screen.queryByTestId('tags-permission-denied')).not.toBeInTheDocument()

      // Switch to routing tab
      fireEvent.click(screen.getByTestId('tab-routing'))
      expect(screen.getByTestId('btn-save-routing')).toBeInTheDocument()
      expect(screen.queryByTestId('routing-permission-denied')).not.toBeInTheDocument()
    })
  })
})
