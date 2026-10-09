import React from 'react'
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import { MemoryRouter, Routes, Route } from 'react-router-dom'
import fs from 'fs'
import path from 'path'

import { InboxDetail } from '../features/inbox/components/InboxDetail'
import { OpportunityTable } from '../features/opportunities/components/OpportunityTable'
import OpportunitiesFeature from '../features/opportunities/OpportunitiesFeature'
import OpportunityDrawer from '../features/opportunities/components/OpportunityDrawer'
import CustomerProfile from '../features/customers/components/CustomerProfile'
import MergeModal from '../features/customers/components/MergeModal'
import SettingsFeature from '../features/settings/SettingsFeature'

import { opportunityApi } from '../features/opportunities/api/opportunityApi'
import { customerApi } from '../features/customers/api/customerApi'
import { inboxApi } from '../features/inbox/api/inboxApi'
import { useAuthStore } from '../auth/authStore'

describe('AC 3f6105db (a)(b)(c) — Gỡ toàn bộ UI nhắc việc khỏi CrmApp', () => {
  beforeEach(() => {
    vi.restoreAllMocks()
    localStorage.clear()
    useAuthStore.getState().setAuth('mock-token', {
      id: 'u-reviewer',
      email: 'reviewer@vni.local',
      userName: 'Reviewer Care',
      roles: ['Reviewer', 'Admin'],
    })
  })

  afterEach(() => {
    vi.restoreAllMocks()
  })

  // =========================================================================
  // (a) Test quét tĩnh: không file nào trong CrmApp/src (trừ test) import reminderApi
  // hoặc gọi '/CrmReminder'. Thư mục features/tasks đã bị xoá.
  // =========================================================================
  describe('(a) Kiểm tra quét tĩnh mã nguồn CrmApp/src', () => {
    const srcDir = path.resolve(__dirname, '..')

    function getAllSourceFiles(dir) {
      let results = []
      const list = fs.readdirSync(dir)
      for (const file of list) {
        const fullPath = path.join(dir, file)
        const stat = fs.statSync(fullPath)
        if (stat.isDirectory()) {
          // Bỏ qua thư mục test
          if (file === 'test' || file === '__tests__') continue
          results = results.concat(getAllSourceFiles(fullPath))
        } else if (/\.(jsx?|tsx?|css)$/.test(file) && !/\.test\.|\.spec\./.test(file)) {
          results.push(fullPath)
        }
      }
      return results
    }

    it('thư mục features/tasks đã bị xoá hoặc không tồn tại', () => {
      const tasksDir = path.join(srcDir, 'features', 'tasks')
      expect(fs.existsSync(tasksDir)).toBe(false)
    })

    it('không file source nào import reminderApi hoặc gọi endpoint /CrmReminder', () => {
      const sourceFiles = getAllSourceFiles(srcDir)
      expect(sourceFiles.length).toBeGreaterThan(10)

      for (const filePath of sourceFiles) {
        const content = fs.readFileSync(filePath, 'utf8')
        const relPath = path.relative(srcDir, filePath)

        expect(
          content.includes('reminderApi'),
          `File ${relPath} không được chứa hoặc import reminderApi`
        ).toBe(false)

        expect(
          content.includes('/CrmReminder'),
          `File ${relPath} không được gọi endpoint /CrmReminder`
        ).toBe(false)

        expect(
          content.includes('api/CrmReminder'),
          `File ${relPath} không được gọi api/CrmReminder`
        ).toBe(false)
      }
    })

    it('quét tĩnh toàn bộ src (trừ test và comment): không còn bất kỳ chuỗi hiển thị nào chứa "nhắc việc" (kể cả window.confirm/alert/toast)', () => {
      const sourceFiles = getAllSourceFiles(srcDir)
      expect(sourceFiles.length).toBeGreaterThan(10)

      for (const filePath of sourceFiles) {
        const rawContent = fs.readFileSync(filePath, 'utf8')
        // Loại bỏ multi-line comments /* ... */
        let stripped = rawContent.replace(/\/\*[\s\S]*?\*\//g, '')
        // Loại bỏ single-line comments // ...
        stripped = stripped
          .split('\n')
          .map((line) => {
            const commentIdx = line.search(/(?<!https?:)\/\//)
            return commentIdx !== -1 ? line.slice(0, commentIdx) : line
          })
          .join('\n')

        const relPath = path.relative(srcDir, filePath)
        const match = stripped.match(/nhắc việc/i)
        expect(
          match,
          `File ${relPath} vẫn còn chứa chuỗi hiển thị "nhắc việc" (ở: "${match ? stripped.slice(Math.max(0, match.index - 20), match.index + 40).trim() : ''}")`
        ).toBeNull()
      }
    })

    it('CustomerProfile.jsx, MergeModal.jsx, SettingsFeature.jsx, useAuth.js không còn chứa chữ "nhắc việc"', () => {
      const customerProfileContent = fs.readFileSync(
        path.join(srcDir, 'features/customers/components/CustomerProfile.jsx'),
        'utf8'
      )
      expect(customerProfileContent).toContain('dữ liệu chăm sóc liên quan')
      expect(customerProfileContent.toLowerCase()).not.toContain('nhắc việc')

      const mergeModalContent = fs.readFileSync(
        path.join(srcDir, 'features/customers/components/MergeModal.jsx'),
        'utf8'
      )
      expect(mergeModalContent.toLowerCase()).not.toContain('nhắc việc')

      const settingsContent = fs.readFileSync(
        path.join(srcDir, 'features/settings/SettingsFeature.jsx'),
        'utf8'
      )
      expect(settingsContent.toLowerCase()).not.toContain('nhắc việc')

      const useAuthContent = fs.readFileSync(
        path.join(srcDir, 'auth/useAuth.js'),
        'utf8'
      )
      expect(useAuthContent.toLowerCase()).not.toContain('nhắc việc')
    })
  })

  // =========================================================================
  // (b) Render kiểm tra:
  // - InboxDetail không còn btn-create-reminder
  // - OpportunityTable / Việc của tôi không còn tab-activities
  // - URL cũ ?tab=activities rơi về tab Tất cả, không crash
  // - OpportunityDrawer không còn drawer-reminders-card
  // - CustomerProfile không còn tab nhắc việc
  // - Ở mọi màn đó, queryByText(/nhắc việc/i) đều null
  // =========================================================================
  describe('(b) Render kiểm tra UI không còn nhắc việc', () => {
    it('InboxDetail: không còn nút btn-create-reminder, không có text "nhắc việc"', () => {
      const mockItem = {
        id: 'msg-1',
        kind: 1,
        displayName: 'Khách Test',
        canReply: true,
        status: 1,
      }
      const mockDetail = {
        kind: 1,
        conversation: { id: 'msg-1', messages: [] },
        tags: [],
      }

      render(
        <MemoryRouter>
          <InboxDetail
            item={mockItem}
            detail={mockDetail}
            canCare={true}
            users={[]}
            tags={[]}
          />
        </MemoryRouter>
      )

      expect(screen.queryByTestId('btn-create-reminder')).not.toBeInTheDocument()
      expect(screen.queryByText(/nhắc việc/i)).not.toBeInTheDocument()
    })

    it('OpportunityTable: không còn tab-activities và queryByText(/nhắc việc/i) null', () => {
      render(
        <MemoryRouter>
          <OpportunityTable
            items={[]}
            columns={{}}
            loading={false}
            activeTab="all"
            onTabChange={vi.fn()}
            stages={[]}
            users={[]}
          />
        </MemoryRouter>
      )

      expect(screen.queryByTestId('tab-activities')).not.toBeInTheDocument()
      expect(screen.queryByText(/nhắc việc/i)).not.toBeInTheDocument()
    })

    it('OpportunitiesFeature: URL cũ ?tab=activities rơi về tab "all", không crash, không render TasksBoard hay tab-activities', async () => {
      vi.spyOn(opportunityApi, 'listStages').mockResolvedValue([
        { id: 'stg-1', name: 'Mới', color: '#3b82f6', kind: 1, sortOrder: 1 },
      ])
      vi.spyOn(opportunityApi, 'listUsers').mockResolvedValue([])
      vi.spyOn(opportunityApi, 'stats').mockResolvedValue({
        total: 1, open: 1, won: 0, lost: 0, activity: 0, rev: 0,
      })
      vi.spyOn(opportunityApi, 'filter').mockResolvedValue({ items: [], total: 0 })

      render(
        <MemoryRouter initialEntries={['/tasks?tab=activities']}>
          <OpportunitiesFeature />
        </MemoryRouter>
      )

      expect(await screen.findByTestId('opportunities-feature')).toBeInTheDocument()
      expect(screen.queryByTestId('tab-activities')).not.toBeInTheDocument()
      expect(screen.queryByTestId('opportunity-activities')).not.toBeInTheDocument()
      expect(screen.queryByTestId('tasks-board-page')).not.toBeInTheDocument()
      expect(screen.queryByText(/nhắc việc/i)).not.toBeInTheDocument()
    })

    it('OpportunityDrawer: không còn drawer-reminders-card, không có nút thêm nhắc việc, queryByText(/nhắc việc/i) null', async () => {
      vi.spyOn(opportunityApi, 'get').mockResolvedValue({
        id: 'opp-100',
        title: 'Cơ hội khoá học Pro',
        crmCustomerId: 'cust-100',
        customerName: 'Trần Văn Pro',
        expectedValue: 20000000,
        stageId: 'stg-1',
        status: 1,
        watcherUserIds: [],
      })

      render(
        <MemoryRouter>
          <OpportunityDrawer
            opportunityId="opp-100"
            isOpen={true}
            onClose={vi.fn()}
          />
        </MemoryRouter>
      )

      expect(await screen.findByTestId('opportunity-drawer')).toBeInTheDocument()
      expect(screen.queryByTestId('drawer-reminders-card')).not.toBeInTheDocument()
      expect(screen.queryByTestId('btn-add-opp-reminder-toggle')).not.toBeInTheDocument()
      expect(screen.queryByText(/nhắc việc/i)).not.toBeInTheDocument()
    })

    it('CustomerProfile: không còn tab-reminders, không có reminders-pane, queryByText(/nhắc việc/i) null', async () => {
      vi.spyOn(customerApi, 'get').mockResolvedValue({
        id: 'cust-200',
        displayName: 'Nguyễn Văn Test',
        phoneE164: '+84988888888',
        identities: [],
      })
      vi.spyOn(customerApi, 'getTimeline').mockResolvedValue([])
      vi.spyOn(customerApi, 'listNotes').mockResolvedValue([])

      render(
        <MemoryRouter>
          <CustomerProfile customerId="cust-200" onBack={vi.fn()} />
        </MemoryRouter>
      )

      await waitFor(() => {
        expect(screen.getByTestId('customer-profile-page')).toBeInTheDocument()
      })

      expect(screen.queryByTestId('tab-reminders')).not.toBeInTheDocument()
      expect(screen.queryByTestId('reminders-pane')).not.toBeInTheDocument()
      expect(screen.queryByTestId('add-reminder-form')).not.toBeInTheDocument()
      expect(screen.queryByText(/nhắc việc/i)).not.toBeInTheDocument()
    })

    it('MergeModal: queryByText(/nhắc việc/i) null', () => {
      render(
        <MergeModal
          isOpen={true}
          onClose={vi.fn()}
          customerId="cust-200"
        />
      )

      expect(screen.queryByText(/nhắc việc/i)).not.toBeInTheDocument()
    })

    it('SettingsFeature: queryByText(/nhắc việc/i) null', () => {
      render(
        <MemoryRouter>
          <SettingsFeature />
        </MemoryRouter>
      )

      expect(screen.queryByText(/nhắc việc/i)).not.toBeInTheDocument()
    })
  })
})
