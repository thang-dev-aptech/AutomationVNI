import { describe, expect, it } from 'vitest'
import { getErrorMessage, unwrapApiData } from './apiHelpers'

describe('unwrapApiData', () => {
  it('returns payload.data for a normal ApiResponse envelope', () => {
    const response = { data: { success: true, data: { id: 1 } } }
    expect(unwrapApiData(response)).toEqual({ id: 1 })
  })

  it('throws payload.message when success is false', () => {
    const response = { data: { success: false, message: 'Thất bại', errorCode: 'X' } }
    expect(() => unwrapApiData(response)).toThrow('Thất bại')
  })

  it('returns raw payload when it is not an ApiResponse envelope', () => {
    const response = { data: [1, 2, 3] }
    expect(unwrapApiData(response)).toEqual([1, 2, 3])
  })

  // Backend cũ/thiếu route rơi vào SPA fallback (app.MapFallbackToFile trong Program.cs)
  // và trả 200 OK kèm index.html thay vì JSON — phải chặn sớm, không để chuỗi HTML
  // trôi xuống làm "data" tới khi một nơi xa hơn crash không rõ nguyên nhân.
  it('throws a clear error when the backend returns the SPA index.html instead of JSON', () => {
    const response = { data: '<!DOCTYPE html><html><head></head><body></body></html>' }
    expect(() => unwrapApiData(response)).toThrow(/backend cũ|HTML/i)
  })

  it('throws a clear error for a bare <html> response without a doctype', () => {
    const response = { data: '<html><body>fallback</body></html>' }
    expect(() => unwrapApiData(response)).toThrow(/backend cũ|HTML/i)
  })

  it('does not misfire on a normal string payload that merely starts with "<"', () => {
    const response = { data: '<3 not html' }
    expect(unwrapApiData(response)).toBe('<3 not html')
  })
})

describe('getErrorMessage', () => {
  it('prefers the backend error message when present', () => {
    const error = { response: { data: { message: 'Lỗi server' } } }
    expect(getErrorMessage(error)).toBe('Lỗi server')
  })

  it('falls back to error.message, then the provided fallback', () => {
    expect(getErrorMessage(new Error('boom'))).toBe('boom')
    expect(getErrorMessage({}, 'Mặc định')).toBe('Mặc định')
  })
})
