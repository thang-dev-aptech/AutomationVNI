/**
 * App config — đọc từ import.meta.env, không hard-code URL trong component.
 */
const apiBaseUrl = import.meta.env.VITE_API_BASE_URL ?? ''

export const appConfig = {
  apiBaseUrl,
  isDev: import.meta.env.DEV,
  appName: 'VNI Automation',
}
