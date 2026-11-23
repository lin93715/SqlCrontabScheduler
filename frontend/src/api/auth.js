import { http } from './http.js'

export const auth = {
  me: () => http.get('/api/auth/me').then(r => r.data),
  login: body => http.post('/api/auth/login', body).then(r => r.data),
  logout: () => http.post('/api/auth/logout'),
}
