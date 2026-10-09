// Tài khoản điền sẵn ở màn đăng nhập. Mật khẩu CHỈ điền khi chạy dev (vite dev / vitest).
// Mặc định trùng DevSeedOptions của backend (backend/Shared/DevSeedOptions.cs);
// đổi qua VITE_DEV_LOGIN_EMAIL / VITE_DEV_LOGIN_PASSWORD trong .env.local.
// Production giữ hành vi cũ: chỉ điền email admin@vni.local, không điền mật khẩu.
// So sánh trực tiếp import.meta.env.DEV để Vite thay bằng false khi build production và
// loại hẳn chuỗi mật khẩu khỏi bundle (truyền env qua tham số thì không loại được).
const DEV_SEED_ACCOUNT = import.meta.env.DEV
  ? { email: 'admin@vni.local', password: 'Admin@123' }
  : null

export function getDevLoginDefaults(env = import.meta.env, devSeed = DEV_SEED_ACCOUNT) {
  if (!env.DEV || !devSeed) return { email: 'admin@vni.local', password: '' }
  return {
    email: env.VITE_DEV_LOGIN_EMAIL || devSeed.email,
    password: env.VITE_DEV_LOGIN_PASSWORD || devSeed.password,
  }
}
