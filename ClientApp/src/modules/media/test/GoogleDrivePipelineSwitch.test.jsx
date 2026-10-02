import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'
import GoogleDrivePipelineSwitch from '../components/GoogleDrivePipelineSwitch'

/**
 * AC gdrive04-slider-test (c54441d6): slider gọi onChange khi bấm, disabled khi loading,
 * confirm/mutation vẫn ở parent handleToggleGoogleDrive (không đổi logic).
 */
describe('GoogleDrivePipelineSwitch (GDRIVE-04)', () => {
  it('calls onChange when toggled while enabled', async () => {
    const user = userEvent.setup()
    const onChange = vi.fn()
    render(
      <GoogleDrivePipelineSwitch checked={false} onChange={onChange} label="Google Drive" />,
    )

    await user.click(screen.getByRole('switch'))
    expect(onChange).toHaveBeenCalledTimes(1)
    expect(onChange).toHaveBeenCalledWith(true)
  })

  it('does not call onChange when disabled or loading', async () => {
    const user = userEvent.setup()
    const onChange = vi.fn()
    const { rerender } = render(
      <GoogleDrivePipelineSwitch checked disabled onChange={onChange} />,
    )
    await user.click(screen.getByRole('switch'))
    expect(onChange).not.toHaveBeenCalled()

    rerender(<GoogleDrivePipelineSwitch checked loading onChange={onChange} />)
    await user.click(screen.getByRole('switch'))
    expect(onChange).not.toHaveBeenCalled()
  })

  it('reflects checked state for parent-controlled confirm flow', () => {
    const { rerender } = render(
      <GoogleDrivePipelineSwitch checked={true} onChange={() => {}} />,
    )
    expect(screen.getByRole('switch')).toHaveAttribute('aria-checked', 'true')

    rerender(<GoogleDrivePipelineSwitch checked={false} onChange={() => {}} />)
    expect(screen.getByRole('switch')).toHaveAttribute('aria-checked', 'false')
  })
})
