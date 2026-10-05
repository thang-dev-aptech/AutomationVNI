import { describe, expect, it } from 'vitest'
import { isVniChannelName, sortChannelsVniFirst } from './channelSort'

const ch = (id, pageName) => ({ id, pageName })
const names = (list) => list.map((c) => c.pageName)

describe('sortChannelsVniFirst', () => {
  it('puts names containing "vni" first (any case, anywhere), then A→Z inside each group', () => {
    const input = [
      ch('1', 'Zeta'), ch('2', 'VNi Hà Nội'), ch('3', 'Alpha'), ch('4', 'vni sài gòn'), ch('5', 'Beta VNI'),
    ]

    expect(names(sortChannelsVniFirst(input))).toEqual([
      'Beta VNI', 'VNi Hà Nội', 'vni sài gòn', 'Alpha', 'Zeta',
    ])
  })

  it('orders Vietnamese names with Vietnamese collation (diacritics, Đ after D)', () => {
    const input = [ch('1', 'Đông'), ch('2', 'Bắc'), ch('3', 'Ân'), ch('4', 'An'), ch('5', 'Đà Nẵng'), ch('6', 'Dũng')]

    expect(names(sortChannelsVniFirst(input))).toEqual(['An', 'Ân', 'Bắc', 'Dũng', 'Đà Nẵng', 'Đông'])
  })

  // Cùng bộ tên + kỳ vọng với tests/Backend.Tests/Modules/SocialChannel/PageOrderingTests.cs Expected.
  it('matches the backend pinned Vietnamese order for the shared name set', () => {
    const input = [
      ch('1', 'Zeta'), ch('2', 'VNi Hà Nội'), ch('3', 'Alpha'), ch('4', 'vni sài gòn'),
      ch('5', 'Beta VNI'), ch('6', 'Đà Nẵng'), ch('7', 'Ân Thi'), ch('8', 'Ba Vì'),
      ch('9', 'VNi Đông Anh'), ch('10', 'VNi Bắc Ninh'),
    ]

    expect(names(sortChannelsVniFirst(input))).toEqual([
      'Beta VNI', 'VNi Bắc Ninh', 'VNi Đông Anh', 'VNi Hà Nội', 'vni sài gòn',
      'Alpha', 'Ân Thi', 'Ba Vì', 'Đà Nẵng', 'Zeta',
    ])
  })

  it('breaks ties by id and is deterministic for equal names', () => {
    const input = [ch('b', 'Same'), ch('a', 'Same'), ch('c', 'Same')]

    expect(sortChannelsVniFirst(input).map((c) => c.id)).toEqual(['a', 'b', 'c'])
    expect(sortChannelsVniFirst([...input].reverse()).map((c) => c.id)).toEqual(['a', 'b', 'c'])
  })

  it('does not mutate the input array or its items and returns a new array', () => {
    const input = Object.freeze([ch('1', 'Zeta'), ch('2', 'VNi'), ch('3', 'Alpha')])
    const before = input.map((c) => c.id)

    const result = sortChannelsVniFirst(input)

    expect(input.map((c) => c.id)).toEqual(before)
    expect(result).not.toBe(input)
    expect(result[0]).toBe(input[1])
  })

  it('tolerates empty names, name fallback, missing ids and nullish input', () => {
    expect(sortChannelsVniFirst(undefined)).toEqual([])
    expect(sortChannelsVniFirst(null)).toEqual([])
    expect(sortChannelsVniFirst([])).toEqual([])

    const input = [{ id: '1', name: 'Beta' }, { id: '2' }, { id: '3', pageName: null, name: 'vni x' }, { pageName: 'Alpha' }]
    const result = sortChannelsVniFirst(input)
    expect(result).toHaveLength(4)
    expect(result[0].id).toBe('3')
    expect(result.at(-1).id).toBe('1')
  })

  it('supports a custom name accessor for items shown by another label', () => {
    const labels = { p1: 'Zeta', p2: 'VNi Huế', p3: 'Alpha' }
    const contexts = [{ id: 'c1', socialChannelId: 'p1' }, { id: 'c2', socialChannelId: 'p2' }, { id: 'c3', socialChannelId: 'p3' }]

    const result = sortChannelsVniFirst(contexts, (c) => labels[c.socialChannelId])

    expect(result.map((c) => c.id)).toEqual(['c2', 'c3', 'c1'])
  })
})

describe('isVniChannelName', () => {
  it('matches "vni" case-insensitively and rejects other names', () => {
    expect(isVniChannelName('VNi Hà Nội')).toBe(true)
    expect(isVniChannelName('Beta VNI')).toBe(true)
    expect(isVniChannelName('Alpha')).toBe(false)
    expect(isVniChannelName('')).toBe(false)
    expect(isVniChannelName(undefined)).toBe(false)
  })
})
