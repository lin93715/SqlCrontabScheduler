import { afterEach, describe, expect, it, vi } from 'vitest'
import { fetchHealth, http } from '../src/api/http.js'

const defaultAdapter = http.defaults.adapter

afterEach(() => { http.defaults.adapter = defaultAdapter })

describe('API health client', () => {
  it('calls the proxied API health endpoint', async () => {
    http.defaults.adapter = vi.fn(async config => ({ data: 'Healthy', status: 200, config }))
    await expect(fetchHealth()).resolves.toMatchObject({ status: 200 })
    expect(http.defaults.adapter).toHaveBeenCalledWith(expect.objectContaining({ url: '/health', method: 'get' }))
  })
})
