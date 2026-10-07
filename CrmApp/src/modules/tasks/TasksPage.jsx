import React, { useState } from 'react'
import { useAuth } from '../../auth/useAuth'
import Button from '../../shared/components/Button'
import Badge from '../../shared/components/Badge'
import Icon from '../../shared/components/Icon'
import './TasksPage.css'

const initialTasks = [
  {
    id: 't-1',
    title: 'Gọi điện tư vấn lộ trình học cho bạn An',
    customer: 'Nguyễn Văn An',
    dueDate: 'Hôm nay, 14:00',
    priority: 'Cao',
    completed: false,
  },
  {
    id: 't-2',
    title: 'Xác nhận lại địa chỉ nhận tài liệu học',
    customer: 'Trần Thị Bích',
    dueDate: 'Ngày mai, 10:00',
    priority: 'Trung bình',
    completed: false,
  },
  {
    id: 't-3',
    title: 'Gửi email báo giá doanh nghiệp',
    customer: 'Lê Hoàng Long',
    dueDate: '08/10/2026',
    priority: 'Thấp',
    completed: true,
  },
]

export const TasksPage = () => {
  const { canCare } = useAuth()
  const [tasks, setTasks] = useState(initialTasks)
  const [filter, setFilter] = useState('all')

  const toggleTask = (id) => {
    if (!canCare) return
    setTasks(tasks.map((t) => (t.id === id ? { ...t, completed: !t.completed } : t)))
  }

  const handleAddTask = () => {
    const title = window.prompt('Nhập tiêu đề việc cần nhắc:')
    if (!title || !title.trim()) return

    const newTask = {
      id: `t-${Date.now()}`,
      title: title.trim(),
      customer: 'Khách hàng mới',
      dueDate: 'Hôm nay',
      priority: 'Trung bình',
      completed: false,
    }
    setTasks([newTask, ...tasks])
  }

  const filteredTasks = tasks.filter((t) => {
    if (filter === 'pending') return !t.completed
    if (filter === 'done') return t.completed
    return true
  })

  return (
    <div>
      <div className="crm-page-header">
        <div>
          <h2 style={{ margin: 0, fontSize: '20px', fontWeight: '800' }}>Việc của tôi</h2>
          <p style={{ margin: '4px 0 0', fontSize: '14px', color: 'var(--crm-text-muted)' }}>
            Danh sách công việc và nhắc nhở chăm sóc khách hàng được giao
          </p>
        </div>

        {canCare && (
          <Button
            variant="primary"
            icon={<Icon name="plus" size={16} />}
            onClick={handleAddTask}
            data-testid="btn-create-task"
          >
            Tạo nhắc việc
          </Button>
        )}
      </div>

      <div style={{ marginBottom: '16px', display: 'flex', gap: '8px' }}>
        <button
          type="button"
          className={`crm-filter-chip ${filter === 'all' ? 'active' : ''}`}
          onClick={() => setFilter('all')}
        >
          Tất cả ({tasks.length})
        </button>
        <button
          type="button"
          className={`crm-filter-chip ${filter === 'pending' ? 'active' : ''}`}
          onClick={() => setFilter('pending')}
        >
          Cần làm ({tasks.filter((t) => !t.completed).length})
        </button>
        <button
          type="button"
          className={`crm-filter-chip ${filter === 'done' ? 'active' : ''}`}
          onClick={() => setFilter('done')}
        >
          Đã xong ({tasks.filter((t) => t.completed).length})
        </button>
      </div>

      <div className="crm-tasks-list" data-testid="tasks-list">
        {filteredTasks.map((t) => (
          <div
            key={t.id}
            className={`crm-task-card ${t.completed ? 'completed' : ''}`}
            data-testid={`task-item-${t.id}`}
          >
            <div className="crm-task-left">
              <div>
                <h4 className="crm-task-title" style={{ textDecoration: t.completed ? 'line-through' : 'none' }}>
                  {t.title}
                </h4>
                <div className="crm-task-meta">
                  <span>Khách: <strong>{t.customer}</strong></span>
                  <span>•</span>
                  <span>Hạn: {t.dueDate}</span>
                  <span>•</span>
                  <Badge variant={t.priority === 'Cao' ? 'danger' : 'default'} size="sm">
                    {t.priority}
                  </Badge>
                </div>
              </div>
            </div>

            {canCare && (
              <div>
                <Button
                  variant={t.completed ? 'secondary' : 'primary'}
                  size="sm"
                  onClick={() => toggleTask(t.id)}
                  data-testid={`btn-toggle-task-${t.id}`}
                >
                  {t.completed ? 'Mở lại' : 'Hoàn thành'}
                </Button>
              </div>
            )}
          </div>
        ))}
      </div>
    </div>
  )
}

export default TasksPage
