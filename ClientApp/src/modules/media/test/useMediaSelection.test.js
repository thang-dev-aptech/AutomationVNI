import { act, renderHook } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { useMediaSelection } from '../hooks/useMediaSelection'

vi.mock('@/shared/stores/toastStore', () => ({
  toast: { warning: vi.fn(), success: vi.fn(), error: vi.fn() },
}))

const image = (id) => ({ id, mimeType: 'image/jpeg', originalFileName: `${id}.jpg` })

describe('useMediaSelection', () => {
  beforeEach(() => vi.clearAllMocks())

  it('keeps selection order up to the limit and blocks the next image', async () => {
    const { toast } = await import('@/shared/stores/toastStore')
    const { result } = renderHook(() => useMediaSelection({ limit: 3 }))

    act(() => {
      result.current.toggle(image('a'))
      result.current.toggle(image('b'))
      result.current.toggle(image('c'))
    })
    expect(result.current.items.map((asset) => asset.id)).toEqual(['a', 'b', 'c'])

    act(() => result.current.toggle(image('d')))
    expect(result.current.items.map((asset) => asset.id)).toEqual(['a', 'b', 'c'])
    expect(toast.warning).toHaveBeenCalled()

    act(() => result.current.toggle({ id: 'pdf', mimeType: 'application/pdf' }))
    expect(result.current.count).toBe(3)
  })
})
