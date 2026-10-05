import { beforeEach, describe, expect, it, vi } from 'vitest'
import axiosInstance from '@/api/axiosInstance'
import { mediaAssetApi } from '../services/mediaAssetApi'

vi.mock('@/api/axiosInstance', () => ({ default: { post: vi.fn() } }))

describe('mediaAssetApi.generateCaption', () => {
  beforeEach(() => vi.clearAllMocks())

  it('sends socialChannelId as a query parameter when given', async () => {
    await mediaAssetApi.generateCaption('asset-1', 'page-1')

    expect(axiosInstance.post).toHaveBeenCalledWith(
      '/api/MediaAsset/asset-1/generate-caption', null, { params: { socialChannelId: 'page-1' } },
    )
  })

  it.each([undefined, null, ''])('sends no query string when the Page is %j', async (page) => {
    await mediaAssetApi.generateCaption('asset-1', page)

    expect(axiosInstance.post).toHaveBeenCalledWith('/api/MediaAsset/asset-1/generate-caption', null, undefined)
  })
})
