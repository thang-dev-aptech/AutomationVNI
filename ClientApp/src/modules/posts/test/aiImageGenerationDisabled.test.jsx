import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, describe, expect, it, vi } from 'vitest'
import {
  AI_IMAGE_DISABLED_HINT,
  FEATURES,
  isAiImageGenerationEnabled,
} from '@/shared/config/features'
import GenerationFlowPicker from '../components/GenerationFlowPicker'
import PostGenerationActions from '../components/PostGenerationActions'

const DEFAULTS = { ...FEATURES }

vi.mock('@/shared/hooks/usePermissions', () => ({
  usePermissions: () => ({ canEditPost: () => true }),
}))

vi.mock('../hooks/usePosts', () => ({
  useRegenerateText: () => ({ mutateAsync: vi.fn(), isPending: false }),
  useRegenerateImage: () => ({ mutateAsync: vi.fn(), isPending: false }),
}))

vi.mock('../constants/postStatus', () => ({
  getAvailableGenerationActions: () => ({ regenText: true, regenImage: true }),
}))

describe('aiImageGeneration kill switch (2026-10-07)', () => {
  afterEach(() => {
    Object.assign(FEATURES, DEFAULTS)
  })

  it('ships disabled with the warning copy', () => {
    expect(DEFAULTS.aiImageGeneration).toBe(false)
    expect(isAiImageGenerationEnabled()).toBe(false)
    expect(AI_IMAGE_DISABLED_HINT).toBe('Tính năng tạm thời tắt')
  })

  it('keeps Full AI visible but disabled with the warning', () => {
    render(<GenerationFlowPicker value="media" onChange={vi.fn()} />)
    const fullAi = screen.getByRole('button', { name: /Sinh toàn bộ bằng AI/ })
    expect(fullAi).toBeDisabled()
    expect(fullAi).toHaveTextContent(AI_IMAGE_DISABLED_HINT)
    expect(screen.getByRole('button', { name: /Dùng ảnh có sẵn trong Media/ })).not.toBeDisabled()
  })

  it('allows selecting Full AI again when the flag is on', async () => {
    FEATURES.aiImageGeneration = true
    const onChange = vi.fn()
    const user = userEvent.setup()
    render(<GenerationFlowPicker value="media" onChange={onChange} />)
    const fullAi = screen.getByRole('button', { name: /Sinh toàn bộ bằng AI/ })
    expect(fullAi).not.toBeDisabled()
    expect(fullAi).not.toHaveTextContent(AI_IMAGE_DISABLED_HINT)
    await user.click(fullAi)
    expect(onChange).toHaveBeenCalledWith('fullai')
  })

  it('disables Tạo lại ảnh with the same warning', () => {
    render(<PostGenerationActions post={{ id: 'p1', userId: 'u1', status: 3, content: 'x' }} />)
    const btn = screen.getByRole('button', { name: /Tạo lại ảnh/ })
    expect(btn).toBeDisabled()
    expect(btn).toHaveTextContent(AI_IMAGE_DISABLED_HINT)
  })
})
