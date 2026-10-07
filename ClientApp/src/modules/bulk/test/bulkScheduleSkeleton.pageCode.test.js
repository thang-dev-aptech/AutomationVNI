import { describe, expect, it } from 'vitest'
import { buildPageCodeMap } from '../utils/bulkScheduleSkeleton'

// R-028 ngoại lệ: page_code đã xuất cho người dùng nên giữ nguyên A→Z thuần (KHÔNG đưa VNi lên đầu).
describe('buildPageCodeMap keeps plain A→Z numbering', () => {
  it('numbers pages alphabetically even when some names contain "vni"', () => {
    const channels = [
      { id: 'z', pageName: 'Zeta' },
      { id: 'v1', pageName: 'VNi Hà Nội' },
      { id: 'a', pageName: 'Alpha' },
      { id: 'v2', pageName: 'vni sài gòn' },
    ]

    const entries = buildPageCodeMap(channels)

    expect(entries.map((e) => [e.pageCode, e.pageName])).toEqual([
      ['1', 'Alpha'], ['2', 'VNi Hà Nội'], ['3', 'vni sài gòn'], ['4', 'Zeta'],
    ])
    expect(entries.map((e) => e.pageId)).toEqual(['a', 'v1', 'v2', 'z'])
  })

  it('is independent of the input order', () => {
    const channels = [{ id: 'b', pageName: 'VNi B' }, { id: 'a', pageName: 'Alpha' }, { id: 'c', pageName: 'Cat' }]

    expect(buildPageCodeMap([...channels].reverse())).toEqual(buildPageCodeMap(channels))
  })
})
