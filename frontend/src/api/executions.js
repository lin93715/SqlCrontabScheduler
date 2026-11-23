import { http } from './http.js'

export const executions = {
  list(params, jobId) {
    const path = jobId ? '/api/jobs/' + encodeURIComponent(jobId) + '/executions' : '/api/executions'
    return http.get(path, { params }).then(r => r.data)
  },
  get: id => http.get('/api/executions/' + encodeURIComponent(id)).then(r => r.data),
  summary: () => http.get('/api/dashboard/summary').then(r => r.data),
}
