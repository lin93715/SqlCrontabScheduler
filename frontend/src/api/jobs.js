import { http } from './http.js'

const jobsPath = '/api/jobs'
const jobPath = id => jobsPath + '/' + encodeURIComponent(id)

export const jobs = {
  list: params => http.get(jobsPath, { params }).then(r => r.data),
  get: id => http.get(jobPath(id)).then(r => r.data),
  create: body => http.post(jobsPath, body).then(r => r.data),
  update: (id, body) => http.put(jobPath(id), body).then(r => r.data),
  remove: id => http.delete(jobPath(id)),
  action: (id, action) => http.post(jobPath(id) + '/' + action).then(r => r.data),
  connections: () => http.get('/api/connections').then(r => r.data),
  preview: (body, signal) => http.post('/api/schedules/preview', body, { signal }).then(r => r.data),
}
