import React, { useState, useEffect, useMemo } from 'react'
import Button from '../../../shared/components/Button'
import Icon from '../../../shared/components/Icon'

// Format Date / ISO to Vietnam local string YYYY-MM-DDTHH:mm for datetime-local input
export function toVietnamDateTimeLocalString(date) {
  if (!date) return ''
  const d = new Date(date)
  if (isNaN(d.getTime())) return ''
  // Vietnam time is UTC+7
  const utcMs = d.getTime() + d.getTimezoneOffset() * 60000
  const vnDate = new Date(utcMs + 7 * 3600000)
  const pad = (n) => String(n).padStart(2, '0')
  const YYYY = vnDate.getFullYear()
  const MM = pad(vnDate.getMonth() + 1)
  const DD = pad(vnDate.getDate())
  const HH = pad(vnDate.getHours())
  const mm = pad(vnDate.getMinutes())
  return `${YYYY}-${MM}-${DD}T${HH}:${mm}`
}

// Format Vietnam datetime string to UTC Date
export function fromVietnamDateTimeLocalString(str) {
  if (!str) return null
  const [datePart, timePart] = str.split('T')
  if (!datePart || !timePart) return null
  const [y, m, d] = datePart.split('-').map(Number)
  const [h, min] = timePart.split(':').map(Number)
  return new Date(Date.UTC(y, m - 1, d, h - 7, min, 0))
}

// Format "Cửa sổ 24h đóng lúc HH:mm dd/MM" in Vietnam time (UTC+7)
export function formatWindowClosesAt(closesAtStr) {
  if (!closesAtStr) return ''
  const d = new Date(closesAtStr)
  if (isNaN(d.getTime())) return ''
  const utcMs = d.getTime() + d.getTimezoneOffset() * 60000
  const vnDate = new Date(utcMs + 7 * 3600000)
  const pad = (n) => String(n).padStart(2, '0')
  const HH = pad(vnDate.getHours())
  const mm = pad(vnDate.getMinutes())
  const dd = pad(vnDate.getDate())
  const MM = pad(vnDate.getMonth() + 1)
  return `Cửa sổ 24h đóng lúc ${HH}:${mm} ${dd}/${MM}`
}

export const ScheduleMessagePopover = ({
  isOpen,
  onClose,
  onConfirm,
  replyWindowClosesAt,
  initialText = '',
  initialScheduledAt = null,
  isEdit = false,
  isLoading = false,
}) => {
  const [text, setText] = useState(initialText)
  const [datetimeLocal, setDatetimeLocal] = useState('')

  useEffect(() => {
    if (isOpen) {
      setText(initialText)
      if (initialScheduledAt) {
        setDatetimeLocal(toVietnamDateTimeLocalString(initialScheduledAt))
      } else {
        // Default to now + 1 hour
        const defaultDate = new Date(Date.now() + 60 * 60 * 1000)
        setDatetimeLocal(toVietnamDateTimeLocalString(defaultDate))
      }
    }
  }, [isOpen, initialText, initialScheduledAt])

  const selectedDateUtc = useMemo(() => {
    return fromVietnamDateTimeLocalString(datetimeLocal)
  }, [datetimeLocal])

  const windowClosesDate = useMemo(() => {
    if (!replyWindowClosesAt) return null
    const d = new Date(replyWindowClosesAt)
    return isNaN(d.getTime()) ? null : d
  }, [replyWindowClosesAt])

  const windowClosesText = useMemo(() => {
    return formatWindowClosesAt(replyWindowClosesAt)
  }, [replyWindowClosesAt])

  // Check if selected time is beyond 24h reply window
  const isOverWindow = useMemo(() => {
    if (!selectedDateUtc || !windowClosesDate) return false
    return selectedDateUtc.getTime() > windowClosesDate.getTime()
  }, [selectedDateUtc, windowClosesDate])

  // Check if selected time is too soon (< now + 1m)
  const isTooSoon = useMemo(() => {
    if (!selectedDateUtc) return true
    return selectedDateUtc.getTime() < Date.now() + 50 * 1000
  }, [selectedDateUtc])

  const canConfirm = Boolean(
    text.trim() &&
      selectedDateUtc &&
      !isOverWindow &&
      !isTooSoon &&
      !isLoading,
  )

  // Quick suggestions handlers
  const handleQuickAddMinutes = (minutes) => {
    const nextDate = new Date(Date.now() + minutes * 60 * 1000)
    setDatetimeLocal(toVietnamDateTimeLocalString(nextDate))
  }

  const handleQuickBeforeClose = (minutesBefore = 30) => {
    if (!windowClosesDate) return
    const nextDate = new Date(windowClosesDate.getTime() - minutesBefore * 60 * 1000)
    setDatetimeLocal(toVietnamDateTimeLocalString(nextDate))
  }

  const handleFormSubmit = (e) => {
    e.preventDefault()
    if (!canConfirm || !selectedDateUtc) return
    onConfirm({
      text: text.trim(),
      scheduledAtUtc: selectedDateUtc.toISOString(),
    })
  }

  if (!isOpen) return null

  return (
    <div
      className="crm-schedule-modal-backdrop"
      style={{
        position: 'fixed',
        top: 0,
        left: 0,
        right: 0,
        bottom: 0,
        backgroundColor: 'rgba(0, 0, 0, 0.45)',
        zIndex: 1000,
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'center',
      }}
      onClick={onClose}
    >
      <div
        className="crm-schedule-modal"
        data-testid="schedule-message-modal"
        style={{
          background: 'var(--crm-surface)',
          border: '1px solid var(--crm-border)',
          borderRadius: 'var(--crm-radius-lg, 12px)',
          width: '460px',
          maxWidth: '92vw',
          boxShadow: '0 20px 25px -5px rgba(0, 0, 0, 0.1), 0 10px 10px -5px rgba(0, 0, 0, 0.04)',
          overflow: 'hidden',
          display: 'flex',
          flexDirection: 'column',
        }}
        onClick={(e) => e.stopPropagation()}
      >
        {/* Header */}
        <div
          style={{
            padding: '16px 20px',
            borderBottom: '1px solid var(--crm-border)',
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'space-between',
          }}
        >
          <div style={{ display: 'flex', alignItems: 'center', gap: '8px' }}>
            <Icon name="clock" size={18} />
            <h3 style={{ margin: 0, fontSize: '16px', fontWeight: 700, color: 'var(--crm-text)' }}>
              {isEdit ? 'Chỉnh sửa tin hẹn giờ' : 'Hẹn giờ gửi tin nhắn Messenger'}
            </h3>
          </div>
          <button
            type="button"
            onClick={onClose}
            aria-label="Đóng"
            style={{
              background: 'none',
              border: 'none',
              fontSize: '20px',
              cursor: 'pointer',
              color: 'var(--crm-text-muted)',
              lineHeight: 1,
            }}
          >
            ×
          </button>
        </div>

        {/* Body */}
        <form onSubmit={handleFormSubmit} style={{ padding: '20px' }}>
          {/* Window info & warnings */}
          {windowClosesText && (
            <div
              style={{
                marginBottom: '16px',
                padding: '10px 14px',
                borderRadius: 'var(--crm-radius-md, 8px)',
                backgroundColor: isOverWindow ? '#FEF2F2' : '#EFF6FF',
                border: `1px solid ${isOverWindow ? '#FCA5A5' : '#BFDBFE'}`,
                color: isOverWindow ? '#991B1B' : '#1E40AF',
                fontSize: '13px',
                display: 'flex',
                alignItems: 'center',
                gap: '8px',
              }}
              data-testid="schedule-window-info"
            >
              <Icon name={isOverWindow ? 'alert' : 'info'} size={16} />
              <span>
                {isOverWindow ? `Thời gian hẹn vượt quá: ${windowClosesText}` : windowClosesText}
              </span>
            </div>
          )}

          {/* Quick suggestions */}
          <div style={{ marginBottom: '16px' }}>
            <label
              style={{
                display: 'block',
                fontSize: '12px',
                fontWeight: 600,
                color: 'var(--crm-text-muted)',
                marginBottom: '8px',
              }}
            >
              Gợi ý thời gian gửi nhanh:
            </label>
            <div style={{ display: 'flex', flexWrap: 'wrap', gap: '6px' }}>
              <button
                type="button"
                className="crm-btn crm-btn-secondary"
                data-testid="quick-schedule-30m"
                onClick={() => handleQuickAddMinutes(30)}
                style={{ fontSize: '12px', padding: '4px 10px', borderRadius: '14px' }}
              >
                +30 phút
              </button>
              <button
                type="button"
                className="crm-btn crm-btn-secondary"
                data-testid="quick-schedule-1h"
                onClick={() => handleQuickAddMinutes(60)}
                style={{ fontSize: '12px', padding: '4px 10px', borderRadius: '14px' }}
              >
                +1 giờ
              </button>
              <button
                type="button"
                className="crm-btn crm-btn-secondary"
                data-testid="quick-schedule-3h"
                onClick={() => handleQuickAddMinutes(180)}
                style={{ fontSize: '12px', padding: '4px 10px', borderRadius: '14px' }}
              >
                +3 giờ
              </button>
              {windowClosesDate && (
                <button
                  type="button"
                  className="crm-btn crm-btn-secondary"
                  data-testid="quick-schedule-before-close"
                  onClick={() => handleQuickBeforeClose(30)}
                  style={{ fontSize: '12px', padding: '4px 10px', borderRadius: '14px' }}
                >
                  Trước khi hết 24h 30 phút
                </button>
              )}
            </div>
          </div>

          {/* Datetime input */}
          <div style={{ marginBottom: '16px' }}>
            <label
              htmlFor="schedule-datetime-input"
              style={{
                display: 'block',
                fontSize: '13px',
                fontWeight: 600,
                color: 'var(--crm-text)',
                marginBottom: '6px',
              }}
            >
              Thời gian gửi (Giờ Việt Nam):
            </label>
            <input
              id="schedule-datetime-input"
              type="datetime-local"
              className="crm-form-input"
              data-testid="schedule-datetime-input"
              value={datetimeLocal}
              onChange={(e) => setDatetimeLocal(e.target.value)}
              style={{
                width: '100%',
                padding: '8px 12px',
                fontSize: '14px',
                borderColor: isOverWindow ? 'var(--crm-danger, #EF4444)' : undefined,
              }}
            />
          </div>

          {/* Message content */}
          <div style={{ marginBottom: '20px' }}>
            <label
              htmlFor="schedule-text-input"
              style={{
                display: 'block',
                fontSize: '13px',
                fontWeight: 600,
                color: 'var(--crm-text)',
                marginBottom: '6px',
              }}
            >
              Nội dung tin nhắn:
            </label>
            <textarea
              id="schedule-text-input"
              className="crm-form-input"
              data-testid="schedule-text-input"
              rows={4}
              placeholder="Nhập nội dung tin nhắn cần gửi..."
              value={text}
              onChange={(e) => setText(e.target.value)}
              style={{ width: '100%', padding: '8px 12px', fontSize: '13px', resize: 'vertical' }}
            />
          </div>

          {/* Actions */}
          <div style={{ display: 'flex', justifyContent: 'flex-end', gap: '8px' }}>
            <Button
              type="button"
              variant="secondary"
              onClick={onClose}
              data-testid="btn-cancel-schedule-modal"
              disabled={isLoading}
            >
              Huỷ
            </Button>
            <Button
              type="submit"
              variant="primary"
              disabled={!canConfirm}
              isLoading={isLoading}
              data-testid="btn-confirm-schedule"
            >
              {isEdit ? 'Lưu thay đổi' : 'Xác nhận hẹn giờ'}
            </Button>
          </div>
        </form>
      </div>
    </div>
  )
}

export default ScheduleMessagePopover
