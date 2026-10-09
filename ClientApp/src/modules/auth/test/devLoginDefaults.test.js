import { describe, it, expect } from 'vitest'
import { getDevLoginDefaults } from '../devLoginDefaults'

describe('getDevLoginDefaults', () => {
  it('dev: điền sẵn tài khoản dev seed mặc định', () => {
    expect(getDevLoginDefaults({ DEV: true })).toEqual({ email: 'admin@vni.local', password: 'Admin@123' })
  })

  it('dev: VITE_DEV_LOGIN_* ghi đè mặc định', () => {
    expect(
      getDevLoginDefaults({ DEV: true, VITE_DEV_LOGIN_EMAIL: 'qa@vni.local', VITE_DEV_LOGIN_PASSWORD: 'Qa@123' }),
    ).toEqual({ email: 'qa@vni.local', password: 'Qa@123' })
  })

  it('production: không điền mật khẩu', () => {
    expect(getDevLoginDefaults({ DEV: false, VITE_DEV_LOGIN_PASSWORD: 'x' })).toEqual({ email: 'admin@vni.local', password: '' })
  })
})
