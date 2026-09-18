import { useEffect, useRef, useState } from 'react'
import './ContextMenu.css'

const MENU_WIDTH = 200
const MENU_ITEM_HEIGHT = 36
const PADDING = 8

/**
 * Shared context menu primitive for folder and file cards.
 *
 * Usage:
 *   <ContextMenu
 *     x={clientX}
 *     y={clientY}
 *     items={[
 *       { label: 'Rename', onSelect: () => handleRename() },
 *       { label: 'Delete', onSelect: () => handleDelete(), danger: true }
 *     ]}
 *     onDismiss={() => setState(null)}
 *   />
 */
export default function ContextMenu({ x, y, items, onDismiss }) {
  const [position, setPosition] = useState({ top: y, left: x })
  const menuRef = useRef(null)
  const focusedIndexRef = useRef(-1)
  const previousActiveRef = useRef(null)

  useEffect(() => {
    // Store the currently focused element to restore later
    previousActiveRef.current = document.activeElement

    // Calculate menu dimensions
    const menuHeight = items.length * MENU_ITEM_HEIGHT + PADDING * 2
    let top = y
    let left = x

    // Flip vertically if near bottom edge
    if (top + menuHeight > window.innerHeight - 16) {
      top = Math.max(16, y - menuHeight)
    }

    // Flip horizontally if near right edge
    if (left + MENU_WIDTH > window.innerWidth - 16) {
      left = Math.max(16, x - MENU_WIDTH)
    }

    setPosition({ top, left })

    // Focus first item
    focusedIndexRef.current = 0
    setTimeout(() => {
      const firstButton = menuRef.current?.querySelector('[data-menu-item="0"]')
      firstButton?.focus()
    }, 0)
  }, [x, y, items.length])

  useEffect(() => {
    const handleKeyDown = (e) => {
      if (e.key === 'Escape') {
        e.preventDefault()
        onDismiss()
        previousActiveRef.current?.focus()
        return
      }

      if (e.key === 'ArrowDown') {
        e.preventDefault()
        focusedIndexRef.current = (focusedIndexRef.current + 1) % items.length
        const button = menuRef.current?.querySelector(`[data-menu-item="${focusedIndexRef.current}"]`)
        button?.focus()
        return
      }

      if (e.key === 'ArrowUp') {
        e.preventDefault()
        focusedIndexRef.current = (focusedIndexRef.current - 1 + items.length) % items.length
        const button = menuRef.current?.querySelector(`[data-menu-item="${focusedIndexRef.current}"]`)
        button?.focus()
        return
      }

      if (e.key === 'Enter' || e.key === ' ') {
        e.preventDefault()
        const button = menuRef.current?.querySelector(`[data-menu-item="${focusedIndexRef.current}"]`)
        button?.click()
        return
      }
    }

    const handleClick = (e) => {
      if (!menuRef.current?.contains(e.target)) {
        onDismiss()
        previousActiveRef.current?.focus()
      }
    }

    const handleScroll = () => {
      onDismiss()
      previousActiveRef.current?.focus()
    }

    document.addEventListener('keydown', handleKeyDown)
    document.addEventListener('mousedown', handleClick)
    window.addEventListener('scroll', handleScroll, true)

    return () => {
      document.removeEventListener('keydown', handleKeyDown)
      document.removeEventListener('mousedown', handleClick)
      window.removeEventListener('scroll', handleScroll, true)
    }
  }, [items, onDismiss])

  const handleItemClick = (item) => {
    item.onSelect()
    onDismiss()
    previousActiveRef.current?.focus()
  }

  return (
    <div
      ref={menuRef}
      className="context-menu"
      style={{
        top: `${position.top}px`,
        left: `${position.left}px`,
      }}
      role="menu"
      aria-label="Context menu"
    >
      {items.map((item, index) => (
        <button
          key={index}
          type="button"
          data-menu-item={index}
          className={`context-menu-item${item.danger ? ' is-danger' : ''}`}
          onClick={() => handleItemClick(item)}
          onFocus={() => { focusedIndexRef.current = index }}
          role="menuitem"
        >
          {item.label}
        </button>
      ))}
    </div>
  )
}
