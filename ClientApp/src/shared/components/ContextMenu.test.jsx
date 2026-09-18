import { render, screen, fireEvent } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import ContextMenu from './ContextMenu'

describe('ContextMenu', () => {
  const mockItems = [
    { label: 'Rename', onSelect: vi.fn() },
    { label: 'Delete', onSelect: vi.fn(), danger: true },
    { label: 'Copy', onSelect: vi.fn() }
  ]

  const mockOnDismiss = vi.fn()

  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('renders menu at specified position', () => {
    const { container } = render(
      <ContextMenu x={100} y={150} items={mockItems} onDismiss={mockOnDismiss} />
    )

    const menu = container.querySelector('.context-menu')
    expect(menu).toHaveStyle('top: 150px')
    expect(menu).toHaveStyle('left: 100px')
  })

  it('renders all menu items with correct labels', () => {
    render(
      <ContextMenu x={100} y={150} items={mockItems} onDismiss={mockOnDismiss} />
    )

    expect(screen.getByText('Rename')).toBeInTheDocument()
    expect(screen.getByText('Delete')).toBeInTheDocument()
    expect(screen.getByText('Copy')).toBeInTheDocument()
  })

  it('applies danger styling to danger items', () => {
    render(
      <ContextMenu x={100} y={150} items={mockItems} onDismiss={mockOnDismiss} />
    )

    const deleteButton = screen.getByText('Delete').closest('button')
    expect(deleteButton).toHaveClass('is-danger')

    const renameButton = screen.getByText('Rename').closest('button')
    expect(renameButton).not.toHaveClass('is-danger')
  })

  it('calls onSelect and onDismiss when item is clicked', async () => {
    const user = userEvent.setup()
    render(
      <ContextMenu x={100} y={150} items={mockItems} onDismiss={mockOnDismiss} />
    )

    await user.click(screen.getByText('Rename'))

    expect(mockItems[0].onSelect).toHaveBeenCalled()
    expect(mockOnDismiss).toHaveBeenCalled()
  })

  it('dismisses on click outside', async () => {
    const user = userEvent.setup()
    render(
      <>
        <button>Outside button</button>
        <ContextMenu x={100} y={150} items={mockItems} onDismiss={mockOnDismiss} />
      </>
    )

    await user.click(screen.getByText('Outside button'))

    expect(mockOnDismiss).toHaveBeenCalled()
  })

  it('dismisses on Escape key', async () => {
    const user = userEvent.setup()
    render(
      <ContextMenu x={100} y={150} items={mockItems} onDismiss={mockOnDismiss} />
    )

    const renameButton = screen.getByText('Rename').closest('button')
    renameButton.focus()

    await user.keyboard('{Escape}')

    expect(mockOnDismiss).toHaveBeenCalled()
  })

  it('selects item with Enter key', async () => {
    const user = userEvent.setup()
    render(
      <ContextMenu x={100} y={150} items={mockItems} onDismiss={mockOnDismiss} />
    )

    const renameButton = screen.getByText('Rename').closest('button')
    renameButton.focus()

    await user.keyboard('{Enter}')

    expect(mockItems[0].onSelect).toHaveBeenCalled()
    expect(mockOnDismiss).toHaveBeenCalled()
  })

  it('selects item with Space key', async () => {
    const user = userEvent.setup()
    render(
      <ContextMenu x={100} y={150} items={mockItems} onDismiss={mockOnDismiss} />
    )

    const renameButton = screen.getByText('Rename').closest('button')
    renameButton.focus()

    await user.keyboard(' ')

    expect(mockItems[0].onSelect).toHaveBeenCalled()
    expect(mockOnDismiss).toHaveBeenCalled()
  })

  it('dismisses on scroll', () => {
    render(
      <ContextMenu x={100} y={150} items={mockItems} onDismiss={mockOnDismiss} />
    )

    fireEvent.scroll(window, { top: 100 })

    expect(mockOnDismiss).toHaveBeenCalled()
  })

  it('flips position when near right edge', () => {
    global.innerWidth = 400

    const { container } = render(
      <ContextMenu x={350} y={150} items={mockItems} onDismiss={mockOnDismiss} />
    )

    const menu = container.querySelector('.context-menu')
    const style = menu.getAttribute('style')

    expect(style).toContain('left:')
    const leftValue = parseInt(style.match(/left:\s*(\d+)px/)?.[1] || '0')
    expect(leftValue).toBeLessThan(350)
  })

  it('flips position when near bottom edge', () => {
    global.innerHeight = 400

    const { container } = render(
      <ContextMenu x={100} y={350} items={mockItems} onDismiss={mockOnDismiss} />
    )

    const menu = container.querySelector('.context-menu')
    const style = menu.getAttribute('style')

    expect(style).toContain('top:')
    const topValue = parseInt(style.match(/top:\s*(\d+)px/)?.[1] || '0')
    expect(topValue).toBeLessThan(350)
  })

  it('has proper ARIA attributes', () => {
    const { container } = render(
      <ContextMenu x={100} y={150} items={mockItems} onDismiss={mockOnDismiss} />
    )

    const menu = container.querySelector('.context-menu')
    expect(menu).toHaveAttribute('role', 'menu')
    expect(menu).toHaveAttribute('aria-label', 'Context menu')

    const items = container.querySelectorAll('[role="menuitem"]')
    expect(items).toHaveLength(mockItems.length)
  })
})
