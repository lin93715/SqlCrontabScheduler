import axios from 'axios'

export const http = axios.create({ baseURL: '/', timeout: 15000 })
let unauthorized

http.interceptors.request.use(config => {
  if (['post', 'put', 'patch', 'delete'].includes(config.method)) {
    config.headers.set('X-Scheduler-Request', '1')
  }
  return config
})

http.interceptors.response.use(response => response, error => {
  if (error.response?.status === 401 && !error.config?.url?.startsWith('/api/auth/')) unauthorized?.()
  return Promise.reject(error)
})

export function onUnauthorized(callback) {
  unauthorized = callback
}

export function fetchHealth() {
  return http.get('/health')
}

export function errorText(error, fallback = '無法完成操作，請稍後再試。') {
  const problem = error.response?.data
  const validation = Object.values(problem?.errors ?? {}).flat().filter(value => typeof value === 'string')
  return validation[0] || problem?.title || fallback
}
