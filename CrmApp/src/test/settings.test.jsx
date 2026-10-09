import React from 'react'
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { render, screen, fireEvent, waitFor } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import SettingsPage from '../modules/settings/SettingsPage'
import { tagApi, autoAssignApi, usersApi } from '../features/settings/api/settingsApi'
import { useAuthStore } from '../auth/authStore'

describe('AC Settings Vitest (ẩn với Reviewer/Viewer, lưu đúng payload)', () => {
  const mockTags = [
    {
      id: 'tag-1',
      name: 'Khóa K45',
      color: '#4F46E5',
      createdAt: '2026-10-06T10:00:00Z',
    },
    {
      id: 'tag-2',
      name: 'Hà Nội',
      color: '#0D9488',
      createdAt: '2026-10-06T11:00:00Z',
    },
  ]

  const mockUsers = [
    {
      id: 'user-sale-1',
      displayName: 'Nguyễn Văn A',
      userName: 'sale_a',
      roles: ['Reviewer'],
      isActive: true,
    },
    {
      id: 'user-sale-2',
      displayName: 'Trần Thị B',
      userName: 'sale_b',
      roles: ['Reviewer'],
      isActive: true,
    },
    {
      id: 'user-admin-3',
      displayName: 'Lê Hoàng Admin',
      userName: 'admin_c',
      roles: ['Admin'],
      isActive: true,
    },
  ]

  const mockAutoAssign = {
    isEnabled: false,
    assigneeUserIds: ['user-sale-1'],
    nextIndex: 0,
  }

  beforeEach(() => {
    vi.restoreAllMocks()
    useAuthStore.getState().clearAuth()

    vi.spyOn(tagApi, 'list').mockResolvedValue(mockTags)
    vi.spyOn(autoAssignApi, 'get').mockResolvedValue(mockAutoAssign)
    vi.spyOn(usersApi, 'list').mockResolvedValue(mockUsers)
    vi.spyOn(tagApi, 'create').mockImplementation(async (payload) => ({
      id: 'tag-new-99',
      name: payload.name,
      color: payload.color,
      createdAt: new Date().toISOString(),
    }))
    vi.spyOn(tagApi, 'update').mockImplementation(async (id, payload) => ({
      id,
      name: payload.name,
      color: payload.color,
      updatedAt: new Date().toISOString(),
    }))
    vi.spyOn(tagApi, 'delete').mockResolvedValue({ success: true })
    vi.spyOn(autoAssignApi, 'update').mockImplementation(async (payload) => ({
      isEnabled: payload.isEnabled,
      assigneeUserIds: payload.assigneeUserIds,
      nextIndex: 0,
    }))
  })

  afterEach(() => {
    vi.restoreAllMocks()
  })

  // 1. Phân quyền vai trò: Ẩn với Viewer và Reviewer
  describe('1. Role-based access control (Ẩn với Reviewer/Viewer, cho phép Admin/ContentManager)', () => {
    it('Viewer: ẩn form/nút quản lý tag và cấu hình tự chia, hiển thị thông báo từ chối quyền', () => {
      useAuthStore.getState().setAuth('mock-token', {
        email: 'viewer@vni.local',
        userName: 'ViewerUser',
        roles: ['Viewer'],
      })

      render(
        <MemoryRouter>
          <SettingsPage />
        </MemoryRouter>,
      )

      // Tab Profile hoạt động bình thường
      expect(screen.getByTestId('settings-profile-pane')).toBeInTheDocument()

      // Tab Tags: bị từ chối quyền
      fireEvent.click(screen.getByTestId('tab-tags'))
      expect(screen.getByTestId('tags-permission-denied')).toBeInTheDocument()
      expect(screen.queryByTestId('btn-add-tag')).not.toBeInTheDocument()
      expect(screen.queryByTestId('input-new-tag')).not.toBeInTheDocument()

      // Tab Routing: bị từ chối quyền
      fireEvent.click(screen.getByTestId('tab-routing'))
      expect(screen.getByTestId('routing-permission-denied')).toBeInTheDocument()
      expect(screen.queryByTestId('btn-save-routing')).not.toBeInTheDocument()
      expect(screen.queryByTestId('toggle-auto-assign')).not.toBeInTheDocument()
    })

    it('Reviewer: ẩn chức năng quản trị cài đặt (tag & routing) và hiển thị thông báo từ chối', () => {
      useAuthStore.getState().setAuth('mock-token', {
        email: 'reviewer@vni.local',
        userName: 'ReviewerUser',
        roles: ['Reviewer'],
      })

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

    it('ContentManager: có toàn quyền cấu hình thẻ và tự chia hội thoại', async () => {
      useAuthStore.getState().setAuth('mock-token', {
        email: 'cm@vni.local',
        userName: 'ContentManagerUser',
        roles: ['ContentManager'],
      })

      render(
        <MemoryRouter>
          <SettingsPage />
        </MemoryRouter>,
      )

      fireEvent.click(screen.getByTestId('tab-tags'))
      await waitFor(() => {
        expect(screen.queryByTestId('tags-permission-denied')).not.toBeInTheDocument()
        expect(screen.getByTestId('btn-add-tag')).toBeInTheDocument()
      })

      fireEvent.click(screen.getByTestId('tab-routing'))
      await waitFor(() => {
        expect(screen.queryByTestId('routing-permission-denied')).not.toBeInTheDocument()
        expect(screen.getByTestId('btn-save-routing')).toBeInTheDocument()
      })
    })

    it('Admin: có toàn quyền quản trị', async () => {
      useAuthStore.getState().setAuth('mock-token', {
        email: 'admin@vni.local',
        userName: 'AdminUser',
        roles: ['Admin'],
      })

      render(
        <MemoryRouter>
          <SettingsPage />
        </MemoryRouter>,
      )

      fireEvent.click(screen.getByTestId('tab-tags'))
      await waitFor(() => {
        expect(screen.getByTestId('btn-add-tag')).toBeInTheDocument()
      })

      fireEvent.click(screen.getByTestId('tab-routing'))
      await waitFor(() => {
        expect(screen.getByTestId('btn-save-routing')).toBeInTheDocument()
      })
    })
  })

  // 2. Quản lý Tag: Hiển thị danh sách, thêm, sửa, xóa và gửi đúng payload
  describe('2. Tag Management (Lưu đúng payload: tạo, sửa, xóa)', () => {
    beforeEach(() => {
      useAuthStore.getState().setAuth('mock-token', {
        email: 'admin@vni.local',
        userName: 'AdminUser',
        roles: ['Admin'],
      })
    })

    it('hiển thị danh sách tag tải từ API', async () => {
      render(
        <MemoryRouter>
          <SettingsPage />
        </MemoryRouter>,
      )

      fireEvent.click(screen.getByTestId('tab-tags'))

      await waitFor(() => {
        expect(screen.getByText('Khóa K45')).toBeInTheDocument()
        expect(screen.getByText('Hà Nội')).toBeInTheDocument()
      })
      expect(tagApi.list).toHaveBeenCalledTimes(1)
    })

    it('tạo tag mới gửi đúng payload { name, color }', async () => {
      render(
        <MemoryRouter>
          <SettingsPage />
        </MemoryRouter>,
      )

      fireEvent.click(screen.getByTestId('tab-tags'))
      await waitFor(() => expect(screen.getByText('Khóa K45')).toBeInTheDocument())

      const nameInput = screen.getByTestId('input-new-tag')
      const colorInput = screen.getByTestId('input-new-tag-color')
      const submitBtn = screen.getByTestId('btn-add-tag')

      fireEvent.change(nameInput, { target: { value: 'VIP Kế Toán' } })
      fireEvent.change(colorInput, { target: { value: '#E91E63' } })
      fireEvent.click(submitBtn)

      await waitFor(() => {
        expect(tagApi.create).toHaveBeenCalledWith({
          name: 'VIP Kế Toán',
          color: '#E91E63',
        })
      })

      // Thẻ mới xuất hiện trên UI
      await waitFor(() => {
        expect(screen.getByText('VIP Kế Toán')).toBeInTheDocument()
      })
    })

    it('chỉnh sửa tag gửi đúng payload { name, color } và id', async () => {
      render(
        <MemoryRouter>
          <SettingsPage />
        </MemoryRouter>,
      )

      fireEvent.click(screen.getByTestId('tab-tags'))
      await waitFor(() => expect(screen.getByText('Khóa K45')).toBeInTheDocument())

      // Bấm nút sửa của tag-1
      const editBtn = screen.getByTestId('btn-edit-tag-tag-1')
      fireEvent.click(editBtn)

      const editNameInput = screen.getByTestId('input-edit-tag-name')
      const editColorInput = screen.getByTestId('input-edit-tag-color')
      const saveBtn = screen.getByTestId('btn-save-tag')

      fireEvent.change(editNameInput, { target: { value: 'Khóa K45 Nâng Cao' } })
      fireEvent.change(editColorInput, { target: { value: '#9333EA' } })
      fireEvent.click(saveBtn)

      await waitFor(() => {
        expect(tagApi.update).toHaveBeenCalledWith('tag-1', {
          name: 'Khóa K45 Nâng Cao',
          color: '#9333EA',
        })
      })

      await waitFor(() => {
        expect(screen.getByText('Khóa K45 Nâng Cao')).toBeInTheDocument()
      })
    })

    it('xóa tag gọi API delete với đúng tag id', async () => {
      render(
        <MemoryRouter>
          <SettingsPage />
        </MemoryRouter>,
      )

      fireEvent.click(screen.getByTestId('tab-tags'))
      await waitFor(() => expect(screen.getByText('Hà Nội')).toBeInTheDocument())

      const removeBtn = screen.getByTestId('btn-remove-tag-tag-2')
      fireEvent.click(removeBtn)

      await waitFor(() => {
        expect(tagApi.delete).toHaveBeenCalledWith('tag-2')
      })

      // Tag đã bị gỡ khỏi UI
      await waitFor(() => {
        expect(screen.queryByText('Hà Nội')).not.toBeInTheDocument()
      })
    })
  })

  // 3. Tự chia hội thoại: Bật/tắt, chọn nhân sự và lưu đúng payload
  describe('3. Auto-assign Configuration (Lưu đúng payload: isEnabled, assigneeUserIds)', () => {
    beforeEach(() => {
      useAuthStore.getState().setAuth('mock-token', {
        email: 'admin@vni.local',
        userName: 'AdminUser',
        roles: ['Admin'],
      })
    })

    it('tải cấu hình hiện tại và danh sách nhân sự từ API', async () => {
      render(
        <MemoryRouter>
          <SettingsPage />
        </MemoryRouter>,
      )

      fireEvent.click(screen.getByTestId('tab-routing'))

      await waitFor(() => {
        expect(screen.getByText('Nguyễn Văn A')).toBeInTheDocument()
        expect(screen.getByText('Trần Thị B')).toBeInTheDocument()
        expect(screen.getByText('Lê Hoàng Admin')).toBeInTheDocument()
      })

      expect(autoAssignApi.get).toHaveBeenCalledTimes(1)
      expect(usersApi.list).toHaveBeenCalledTimes(1)

      // Kiểm tra checkbox ban đầu
      const toggle = screen.getByTestId('toggle-auto-assign')
      expect(toggle).not.toBeChecked()

      const user1Checkbox = screen.getByTestId('user-checkbox-user-sale-1')
      const user2Checkbox = screen.getByTestId('user-checkbox-user-sale-2')
      expect(user1Checkbox).toBeChecked()
      expect(user2Checkbox).not.toBeChecked()
    })

    it('bật tự chia và thêm nhân viên nhận hội thoại, lưu đúng payload', async () => {
      render(
        <MemoryRouter>
          <SettingsPage />
        </MemoryRouter>,
      )

      fireEvent.click(screen.getByTestId('tab-routing'))
      await waitFor(() => expect(screen.getByText('Nguyễn Văn A')).toBeInTheDocument())

      // Bật tự chia
      const toggle = screen.getByTestId('toggle-auto-assign')
      fireEvent.click(toggle)
      expect(toggle).toBeChecked()

      // Chọn thêm user-sale-2
      const user2Checkbox = screen.getByTestId('user-checkbox-user-sale-2')
      fireEvent.click(user2Checkbox)
      expect(user2Checkbox).toBeChecked()

      // Nhấn Lưu cấu hình
      const saveBtn = screen.getByTestId('btn-save-routing')
      fireEvent.click(saveBtn)

      await waitFor(() => {
        expect(autoAssignApi.update).toHaveBeenCalledWith({
          isEnabled: true,
          assigneeUserIds: ['user-sale-1', 'user-sale-2'],
        })
      })

      // Thông báo thành công
      await waitFor(() => {
        expect(screen.getByTestId('auto-assign-success')).toBeInTheDocument()
        expect(screen.getByText(/Đã lưu cấu hình tự chia thành công/)).toBeInTheDocument()
      })
    })

    it('nút Chọn tất cả và Bỏ chọn cập nhật đúng danh sách nhân sự và lưu đúng payload', async () => {
      render(
        <MemoryRouter>
          <SettingsPage />
        </MemoryRouter>,
      )

      fireEvent.click(screen.getByTestId('tab-routing'))
      await waitFor(() => expect(screen.getByText('Nguyễn Văn A')).toBeInTheDocument())

      // Bấm "Chọn tất cả"
      const selectAllBtn = screen.getByTestId('btn-select-all-users')
      fireEvent.click(selectAllBtn)

      const saveBtn = screen.getByTestId('btn-save-routing')
      fireEvent.click(saveBtn)

      await waitFor(() => {
        expect(autoAssignApi.update).toHaveBeenCalledWith({
          isEnabled: false,
          assigneeUserIds: ['user-sale-1', 'user-sale-2', 'user-admin-3'],
        })
      })

      // Bấm "Bỏ chọn"
      const deselectAllBtn = screen.getByTestId('btn-deselect-all-users')
      fireEvent.click(deselectAllBtn)

      fireEvent.click(saveBtn)

      await waitFor(() => {
        expect(autoAssignApi.update).toHaveBeenCalledWith({
          isEnabled: false,
          assigneeUserIds: [],
        })
      })
    })
  })
})
