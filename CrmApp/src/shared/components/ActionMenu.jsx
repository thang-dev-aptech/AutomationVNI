import React, { useState, useRef, useEffect, useLayoutEffect, useCallback } from 'react'
import { createPortal } from 'react-dom'
import './ActionMenu.css'

const useIsomorphicLayoutEffect = typeof window !== 'undefined' ? useLayoutEffect : useEffect

/**
 * ActionMenu — Menu hành động dùng chung, render qua portal vào document.body
 * với position: fixed tính từ toạ độ nút trigger.
 *
 * Hỗ trợ:
 * - createPortal vào document.body (hoặc portalContainer tùy chỉnh)
 * - Tự động lật lên khi gần đáy viewport
 * - Clamp toạ độ không tràn ra mép trái / phải viewport
 * - Đóng khi click ngoài, Esc, cuộn container (capture), resize
 * - Bàn phím: Mũi tên lên/xuống chuyển focus giữa các item, Home/End, Enter chọn, Esc đóng
 * - Trả focus về trigger sau khi đóng
 * - a11y: role="menu", role="menuitem", aria-haspopup="menu", aria-expanded
 */
export const ActionMenu = ({
  items = [],
  trigger = '⋮',
  triggerLabel = 'Thao tác',
  triggerTestId,
  triggerClassName = '',
  menuTestId,
  menuClassName = '',
  align = 'right',
  portal = true,
  portalContainer = typeof document !== 'undefined' ? document.body : null,
  disabled = false,
  onOpen,
  onClose,
}) => {
  const [isOpen, setIsOpen] = useState(false)
  const [position, setPosition] = useState({ top: 0, left: 0 })
  const [focusedIndex, setFocusedIndex] = useState(-1)

  const triggerRef = useRef(null)
  const menuRef = useRef(null)
  const itemRefs = useRef([])

  // Danh sách các item tương tác (loại bỏ header và divider)
  const interactiveItems = items.filter(
    (item) => item.type !== 'header' && item.type !== 'divider' && !item.disabled
  )

  const updatePosition = useCallback(() => {
    if (!triggerRef.current) return
    const triggerRect = triggerRef.current.getBoundingClientRect()
    const menuEl = menuRef.current

    const menuWidth = menuEl?.offsetWidth || 180
    const menuHeight = menuEl?.offsetHeight || 160
    const viewportWidth = typeof window !== 'undefined' ? window.innerWidth : 1440
    const viewportHeight = typeof window !== 'undefined' ? window.innerHeight : 900
    const gap = 4
    const margin = 8

    // Kiểm tra không gian phía dưới và phía trên
    const spaceBelow = viewportHeight - triggerRect.bottom
    const spaceAbove = triggerRect.top
    const placeUp = spaceBelow < menuHeight + gap && spaceAbove > spaceBelow

    let top = placeUp ? triggerRect.top - menuHeight - gap : triggerRect.bottom + gap

    // Canh lề ngang: mặc định canh phải theo trigger button
    let left = align === 'left' ? triggerRect.left : triggerRect.right - menuWidth

    // Giới hạn trong viewport ngang
    if (left + menuWidth > viewportWidth - margin) {
      left = viewportWidth - menuWidth - margin
    }
    if (left < margin) {
      left = margin
    }

    // Giới hạn trong viewport dọc
    if (top < margin) {
      top = margin
    }
    if (top + menuHeight > viewportHeight - margin) {
      top = Math.max(margin, viewportHeight - menuHeight - margin)
    }

    setPosition({ top, left })
  }, [align])

  const closeMenu = useCallback(
    (restoreFocus = true) => {
      setIsOpen(false)
      setFocusedIndex(-1)
      onClose?.()
      if (restoreFocus) {
        triggerRef.current?.focus()
      }
    },
    [onClose]
  )

  const openMenu = useCallback(() => {
    if (disabled) return
    setIsOpen(true)
    setFocusedIndex(-1)
    onOpen?.()
  }, [disabled, onOpen])

  const toggleMenu = (e) => {
    e.stopPropagation()
    if (isOpen) {
      closeMenu(false)
    } else {
      openMenu()
    }
  }

  // Cập nhật vị trí khi mở menu
  useIsomorphicLayoutEffect(() => {
    if (isOpen) {
      updatePosition()
    }
  }, [isOpen, updatePosition])

  // Lắng nghe click ngoài, cuộn container, resize, phím ESC
  useEffect(() => {
    if (!isOpen) return

    const handlePointerDown = (e) => {
      const target = e.target
      if (
        target instanceof Node &&
        menuRef.current &&
        !menuRef.current.contains(target) &&
        triggerRef.current &&
        !triggerRef.current.contains(target)
      ) {
        closeMenu(false)
      }
    }

    const handleScroll = (e) => {
      // Nếu sự kiện cuộn nằm bên trong menu thì không đóng
      if (
        e.target instanceof Node &&
        menuRef.current &&
        menuRef.current.contains(e.target)
      ) {
        return
      }
      closeMenu(false)
    }

    const handleResize = () => {
      closeMenu(false)
    }

    const handleGlobalKeyDown = (e) => {
      if (e.key === 'Escape') {
        e.preventDefault()
        e.stopPropagation()
        closeMenu(true)
        return
      }

      if (e.key === 'ArrowDown') {
        e.preventDefault()
        e.stopPropagation()
        setFocusedIndex((prev) => {
          const next = prev < interactiveItems.length - 1 ? prev + 1 : 0
          itemRefs.current[next]?.focus()
          return next
        })
        return
      }

      if (e.key === 'ArrowUp') {
        e.preventDefault()
        e.stopPropagation()
        setFocusedIndex((prev) => {
          const next = prev > 0 ? prev - 1 : interactiveItems.length - 1
          itemRefs.current[next]?.focus()
          return next
        })
        return
      }

      if (e.key === 'Home') {
        e.preventDefault()
        e.stopPropagation()
        setFocusedIndex(0)
        itemRefs.current[0]?.focus()
        return
      }

      if (e.key === 'End') {
        e.preventDefault()
        e.stopPropagation()
        const last = interactiveItems.length - 1
        setFocusedIndex(last)
        itemRefs.current[last]?.focus()
        return
      }

      if (e.key === 'Tab') {
        closeMenu(false)
      }
    }

    document.addEventListener('pointerdown', handlePointerDown)
    document.addEventListener('mousedown', handlePointerDown)
    document.addEventListener('keydown', handleGlobalKeyDown)
    window.addEventListener('scroll', handleScroll, true)
    window.addEventListener('resize', handleResize)

    return () => {
      document.removeEventListener('pointerdown', handlePointerDown)
      document.removeEventListener('mousedown', handlePointerDown)
      document.removeEventListener('keydown', handleGlobalKeyDown)
      window.removeEventListener('scroll', handleScroll, true)
      window.removeEventListener('resize', handleResize)
    }
  }, [isOpen, closeMenu, interactiveItems.length])

  // Xử lý phím trên nút trigger khi đóng
  const handleTriggerKeyDown = (e) => {
    if (e.key === 'ArrowDown' || e.key === 'Enter' || e.key === ' ') {
      if (!isOpen) {
        e.preventDefault()
        openMenu()
      }
    }
  }

  let interactiveCounter = 0

  const menuContent = (
    <div
      ref={menuRef}
      className={`crm-action-menu-dropdown crm-opp-dropdown-menu ${menuClassName}`}
      data-testid={menuTestId}
      role="menu"
      aria-label={triggerLabel}
      tabIndex={-1}
      style={
        portal
          ? {
              position: 'fixed',
              top: `${position.top}px`,
              left: `${position.left}px`,
            }
          : undefined
      }
      onClick={(e) => e.stopPropagation()}
    >
      {items.map((item, index) => {
        if (item.type === 'header') {
          return (
            <div
              key={item.key || `header-${index}`}
              className="crm-action-menu-header crm-opp-card-menu-section-lbl"
            >
              {item.label}
            </div>
          )
        }

        if (item.type === 'divider') {
          return (
            <hr
              key={item.key || `divider-${index}`}
              className="crm-action-menu-divider crm-opp-menu-divider"
            />
          )
        }

        const currentInteractiveIndex = item.disabled ? -1 : interactiveCounter++
        const isFocused = focusedIndex === currentInteractiveIndex

        return (
          <button
            key={item.key || `item-${index}`}
            ref={(el) => {
              if (currentInteractiveIndex >= 0) {
                itemRefs.current[currentInteractiveIndex] = el
              }
            }}
            type="button"
            role="menuitem"
            tabIndex={isFocused ? 0 : -1}
            disabled={item.disabled}
            aria-disabled={item.disabled ? 'true' : undefined}
            className={`crm-action-menu-item crm-opp-dropdown-item ${
              item.danger ? 'crm-action-menu-item--danger crm-opp-dropdown-item--danger' : ''
            } ${item.className || ''}`}
            data-testid={item.testId || item['data-testid']}
            onClick={(e) => {
              e.stopPropagation()
              if (item.disabled) return
              closeMenu(true)
              item.onSelect?.()
            }}
          >
            {item.icon && <span className="crm-action-menu-item-icon">{item.icon}</span>}
            {item.label}
          </button>
        )
      })}
    </div>
  )

  return (
    <div className="crm-action-menu-wrap crm-opp-menu-wrap">
      <button
        ref={triggerRef}
        type="button"
        className={`crm-action-menu-trigger ${triggerClassName}`}
        onClick={toggleMenu}
        onKeyDown={handleTriggerKeyDown}
        aria-label={triggerLabel}
        aria-haspopup="menu"
        aria-expanded={isOpen}
        disabled={disabled}
        data-testid={triggerTestId}
      >
        {trigger}
      </button>

      {isOpen &&
        (portal && portalContainer
          ? createPortal(menuContent, portalContainer)
          : menuContent)}
    </div>
  )
}

export default ActionMenu
