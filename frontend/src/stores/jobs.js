import { ref } from 'vue'
import { defineStore } from 'pinia'
import { jobs } from '../api/jobs.js'
import { errorText } from '../api/http.js'

export const useJobsStore = defineStore('jobs', () => {
  const items = ref([])
  const total = ref(0)
  const loading = ref(false)
  const error = ref('')
  let request = 0

  async function load(params, silent = false) {
    const current = ++request
    if (!silent) loading.value = true
    try {
      const page = await jobs.list(params)
      if (request !== current) return
      items.value = page.items
      total.value = page.total
      error.value = ''
    } catch (cause) {
      if (request === current) error.value = errorText(cause, '無法載入排程。')
    } finally {
      if (request === current) loading.value = false
    }
  }

  function clear() {
    ++request
    items.value = []
    total.value = 0
    loading.value = false
    error.value = ''
  }

  return { items, total, loading, error, load, clear }
})
