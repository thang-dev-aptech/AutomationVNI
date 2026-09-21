import { describe, expect, it } from 'vitest'
import { parseChungChiEligiblePages } from './useMediaFolders'

describe('parseChungChiEligiblePages', () => {
  it('returns an eligible Page array unchanged', () => {
    const pages = [{ id: 'page-a', pageName: 'Page A' }]
    expect(parseChungChiEligiblePages(pages)).toBe(pages)
  })

  it('rejects an HTML fallback response before it reaches ChannelMultiSelect', () => {
    expect(() => parseChungChiEligiblePages('<!doctype html>'))
      .toThrow('Dữ liệu Page chứng chỉ không hợp lệ')
  })
})
