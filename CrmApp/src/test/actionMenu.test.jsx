import React from 'react'
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { render, screen, fireEvent, waitFor } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import fs from 'node:fs'
import path from 'node:path'
import { fileURLToPath } from 'node:url'
import { ActionMenu } from '../shared/components/ActionMenu'
import { OpportunityTable } from '../features/opportunities/components/OpportunityTable'
import { OpportunityCard } from '../features/opportunities/components/OpportunityCard'

const __filename = fileURLToPath(import.meta.url)
const __dirname = path.dirname(__filename)

const mockOpp = {
  id: 'opp-1',
  title: 'Cơ hội VIP Alpha',
  customerName: 'Nguyễn Văn A',
  customerPhoneE164: '+84901234567',
  stageId: 'stg-1',
  stageName: 'Mới liên hệ',
  value: 50000000,
  expectedCloseDateUtc: '2026-11-01T00:00:00Z',
  createdAt: '2026-10-01T00:00:00Z',
  isArchived: false,
}

const mockStages = [
  { id: 'stg-1', name: 'Mới liên hệ', color: '#3b82f6', kind: 1 },
  { id: 'stg-2', name: 'Đang tư vấn', color: '#f59e0b', kind: 1 },
  { id: 'stg-3', name: 'Chốt thành công', color: '#10b981', kind: 2 },
]

describe('Requirement d39fd882, AC 0eeab67e — ActionMenu dùng chung (portal + position:fixed)', () => {
  beforeEach(() => {
    vi.restoreAllMocks()
  })

  afterEach(() => {
    vi.restoreAllMocks()
  })

  describe('AC 0eeab67e (a) — Bấm ⋮ ở bảng: menu render qua portal trong document.body, KHÔNG nằm trong .crm-opp-table-wrap', () => {
    it('bấm ⋮ → menu render trong document.body, có role="menu", nút ⋮ có aria-expanded="true" và KHÔNG nằm trong table-wrap', async () => {
      const { container } = render(
        <MemoryRouter>
          <OpportunityTable
            items={[mockOpp]}
            total={1}
            pageIndex={1}
            pageSize={20}
            stages={mockStages}
            columns={{ actions: true }}
          />
        </MemoryRouter>
      )

      const tableWrap = container.querySelector('.crm-opp-table-wrap')
      expect(tableWrap).toBeInTheDocument()

      const btnTrigger = screen.getByTestId('btn-actions-opp-1')
      expect(btnTrigger).toHaveAttribute('aria-expanded', 'false')
      expect(btnTrigger).toHaveAttribute('aria-haspopup', 'menu')

      // Click nút ⋮ để mở menu
      fireEvent.click(btnTrigger)

      expect(btnTrigger).toHaveAttribute('aria-expanded', 'true')

      const menu = await screen.findByRole('menu')
      expect(menu).toBeInTheDocument()
      expect(menu).toHaveAttribute('data-testid', 'menu-actions-opp-1')

      // Điểm mấu chốt: Menu render qua portal vào document.body, KHÔNG nằm trong container có overflow
      expect(tableWrap.contains(menu)).toBe(false)
      expect(document.body.contains(menu)).toBe(true)

      // Kiểm tra các item hành động cơ hội
      expect(screen.getByTestId('action-edit-opp-1')).toBeInTheDocument()
      expect(screen.getByTestId('action-move-stage-opp-1')).toBeInTheDocument()
      expect(screen.getByTestId('action-archive-opp-1')).toBeInTheDocument()
      expect(screen.getByTestId('action-delete-opp-1')).toBeInTheDocument()
    })

    it('revert-to-prove: nếu ActionMenu render tại chỗ (không portal) thì menu nằm trong table container (bị đỏ tiêu chí portal)', () => {
      // Giả lập component menu không dùng portal (render tại chỗ)
      const { container } = render(
        <div className="crm-opp-table-wrap">
          <ActionMenu
            portal={false}
            triggerTestId="test-no-portal-trigger"
            menuTestId="test-no-portal-menu"
            items={[{ key: 'edit', label: 'Sửa' }]}
          />
        </div>
      )

      const tableWrap = container.querySelector('.crm-opp-table-wrap')
      fireEvent.click(screen.getByTestId('test-no-portal-trigger'))

      const menu = screen.getByTestId('test-no-portal-menu')
      // Nếu không dùng portal, menu bị nhốt trong tableWrap
      expect(tableWrap.contains(menu)).toBe(true)
    })
  })

  describe('AC 0eeab67e (b) — Phím tắt và tương tác: Esc, click ngoài, cuộn, mũi tên lên/xuống', () => {
    it('Esc đóng menu và trả focus về nút trigger ⋮', async () => {
      render(
        <ActionMenu
          triggerTestId="btn-trigger"
          menuTestId="menu-dropdown"
          items={[
            { key: 'item-1', label: 'Mục 1', testId: 'action-item-1' },
            { key: 'item-2', label: 'Mục 2', testId: 'action-item-2' },
          ]}
        />
      )

      const trigger = screen.getByTestId('btn-trigger')
      fireEvent.click(trigger)
      expect(await screen.findByRole('menu')).toBeInTheDocument()

      // Nhấn Escape
      fireEvent.keyDown(document, { key: 'Escape' })

      await waitFor(() => {
        expect(screen.queryByRole('menu')).not.toBeInTheDocument()
      })
      expect(trigger).toHaveAttribute('aria-expanded', 'false')
      expect(document.activeElement).toBe(trigger)
    })

    it('bấm ra ngoài document đóng menu', async () => {
      render(
        <div>
          <button data-testid="outside-button">Outside</button>
          <ActionMenu
            triggerTestId="btn-trigger"
            menuTestId="menu-dropdown"
            items={[{ key: 'item-1', label: 'Mục 1' }]}
          />
        </div>
      )

      const trigger = screen.getByTestId('btn-trigger')
      fireEvent.click(trigger)
      expect(await screen.findByRole('menu')).toBeInTheDocument()

      // Click ra ngoài
      fireEvent.pointerDown(screen.getByTestId('outside-button'))

      await waitFor(() => {
        expect(screen.queryByRole('menu')).not.toBeInTheDocument()
      })
    })

    it('mũi tên xuống / lên chuyển focus giữa các menuitem', async () => {
      render(
        <ActionMenu
          triggerTestId="btn-trigger"
          items={[
            { key: 'item-1', label: 'Mục 1', testId: 'item-1' },
            { key: 'item-2', label: 'Mục 2', testId: 'item-2' },
            { key: 'item-3', label: 'Mục 3', testId: 'item-3' },
          ]}
        />
      )

      const trigger = screen.getByTestId('btn-trigger')
      fireEvent.click(trigger)
      expect(await screen.findByRole('menu')).toBeInTheDocument()

      const item1 = screen.getByTestId('item-1')
      const item2 = screen.getByTestId('item-2')
      const item3 = screen.getByTestId('item-3')

      // Mũi tên xuống lần 1 -> item 1 nhận focus
      fireEvent.keyDown(document, { key: 'ArrowDown' })
      expect(document.activeElement).toBe(item1)

      // Mũi tên xuống lần 2 -> item 2 nhận focus
      fireEvent.keyDown(document, { key: 'ArrowDown' })
      expect(document.activeElement).toBe(item2)

      // Mũi tên xuống lần 3 -> item 3 nhận focus
      fireEvent.keyDown(document, { key: 'ArrowDown' })
      expect(document.activeElement).toBe(item3)

      // Mũi tên lên -> quay lại item 2
      fireEvent.keyDown(document, { key: 'ArrowUp' })
      expect(document.activeElement).toBe(item2)
    })

    it('cuộn window / container đóng menu', async () => {
      render(
        <ActionMenu
          triggerTestId="btn-trigger"
          items={[{ key: 'item-1', label: 'Mục 1' }]}
        />
      )

      fireEvent.click(screen.getByTestId('btn-trigger'))
      expect(await screen.findByRole('menu')).toBeInTheDocument()

      // Bắn sự kiện scroll trên window
      fireEvent.scroll(window)

      await waitFor(() => {
        expect(screen.queryByRole('menu')).not.toBeInTheDocument()
      })
    })
  })

  describe('AC 0eeab67e (c) — Thẻ pipeline dùng cùng ActionMenu module; các mục gọi đúng API; Viewer không thấy ⋮', () => {
    it('OpportunityCard và OpportunityTable import cùng module ActionMenu', () => {
      const tableContent = fs.readFileSync(
        path.resolve(__dirname, '../features/opportunities/components/OpportunityTable.jsx'),
        'utf8'
      )
      const cardContent = fs.readFileSync(
        path.resolve(__dirname, '../features/opportunities/components/OpportunityCard.jsx'),
        'utf8'
      )

      const importRegex = /from\s+['"][^'"]*shared\/components\/ActionMenu['"]/
      expect(tableContent).toMatch(importRegex)
      expect(cardContent).toMatch(importRegex)
    })

    it('thẻ pipeline mở menu, chọn mục gọi đúng callback onMoveStage / onEdit', async () => {
      const onMoveStageSpy = vi.fn()
      const onEditSpy = vi.fn()

      render(
        <MemoryRouter>
          <OpportunityCard
            opp={mockOpp}
            stages={mockStages}
            isReadOnly={false}
            onMoveStage={onMoveStageSpy}
            onEdit={onEditSpy}
          />
        </MemoryRouter>
      )

      const btnCardMenu = screen.getByTestId('btn-card-menu-opp-1')
      expect(btnCardMenu).toBeInTheDocument()

      // Mở menu thẻ pipeline
      fireEvent.click(btnCardMenu)

      const menu = await screen.findByRole('menu')
      expect(menu).toHaveAttribute('data-testid', 'card-menu-opp-1')
      expect(document.body.contains(menu)).toBe(true)

      // Bấm chuyển sang stg-2
      const btnMoveToStg2 = screen.getByTestId('action-card-move-to-stg-2')
      fireEvent.click(btnMoveToStg2)

      expect(onMoveStageSpy).toHaveBeenCalledWith(mockOpp, 'stg-2')
      await waitFor(() => {
        expect(screen.queryByRole('menu')).not.toBeInTheDocument()
      })
    })

    it('Viewer role (isReadOnly = true): không hiển thị nút trigger ⋮ ở cả bảng và thẻ', () => {
      const { unmount } = render(
        <MemoryRouter>
          <OpportunityTable
            items={[mockOpp]}
            total={1}
            pageIndex={1}
            pageSize={20}
            stages={mockStages}
            isReadOnly={true}
            columns={{ actions: true }}
          />
        </MemoryRouter>
      )

      expect(screen.queryByTestId('btn-actions-opp-1')).not.toBeInTheDocument()
      unmount()

      render(
        <MemoryRouter>
          <OpportunityCard
            opp={mockOpp}
            stages={mockStages}
            isReadOnly={true}
          />
        </MemoryRouter>
      )

      expect(screen.queryByTestId('btn-card-menu-opp-1')).not.toBeInTheDocument()
    })
  })

  describe('AC 0eeab67e (d)(e) — Tính toán vị trí fixed và lật / clamp trong viewport', () => {
    it('lật lên trên khi trigger nằm gần đáy viewport', () => {
      const originalInnerHeight = window.innerHeight
      window.innerHeight = 600

      render(
        <ActionMenu
          triggerTestId="trigger-bottom"
          menuTestId="menu-bottom"
          items={[{ key: '1', label: 'Item 1' }]}
        />
      )

      const trigger = screen.getByTestId('trigger-bottom')
      // Giả lập toạ độ nút nằm sát đáy viewport (top: 550, bottom: 580 trên màn cao 600)
      vi.spyOn(trigger, 'getBoundingClientRect').mockReturnValue({
        top: 550,
        bottom: 580,
        left: 500,
        right: 530,
        width: 30,
        height: 30,
      })

      fireEvent.click(trigger)

      const menu = screen.getByTestId('menu-bottom')
      expect(menu).toHaveStyle({ position: 'fixed' })
      // Toạ độ top phải lật lên phía trên trigger (< 550)
      const topStyle = parseInt(menu.style.top, 10)
      expect(topStyle).toBeLessThan(550)

      window.innerHeight = originalInnerHeight
    })

    it('bảng 1 dòng: menu mở ra ngoài document.body không làm phình kích thước table-wrap', async () => {
      const { container } = render(
        <MemoryRouter>
          <OpportunityTable
            items={[mockOpp]}
            total={1}
            pageIndex={1}
            pageSize={20}
            stages={mockStages}
            columns={{ actions: true }}
          />
        </MemoryRouter>
      )

      const tableWrap = container.querySelector('.crm-opp-table-wrap')
      const initialScrollHeight = tableWrap.scrollHeight

      fireEvent.click(screen.getByTestId('btn-actions-opp-1'))
      const menu = await screen.findByRole('menu')
      expect(menu).toBeInTheDocument()

      // tableWrap không chứa menu nên không bị phình scrollHeight
      expect(tableWrap.contains(menu)).toBe(false)
      expect(tableWrap.scrollHeight).toBe(initialScrollHeight)
    })
  })
})
