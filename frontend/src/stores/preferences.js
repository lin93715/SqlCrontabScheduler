import { ref } from 'vue'
import { defineStore } from 'pinia'

export const defaultZone = 'Asia/Taipei'
export const browserZone = Intl.DateTimeFormat().resolvedOptions().timeZone || defaultZone
export const zoneOptions = [...new Set([defaultZone, browserZone, 'UTC',
  ...(Intl.supportedValuesOf?.('timeZone') ?? [])])]

export const usePreferences = defineStore('preferences', () => {
  const saved = localStorage.getItem('scheduler.displayTimeZone')
  const timeZone = ref(zoneOptions.includes(saved) ? saved : defaultZone)

  function setTimeZone(value) {
    timeZone.value = value
    localStorage.setItem('scheduler.displayTimeZone', value)
  }

  return { timeZone, setTimeZone }
})
