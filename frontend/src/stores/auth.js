import { ref } from 'vue'
import { defineStore } from 'pinia'
import { auth } from '../api/auth.js'

export const useAuthStore = defineStore('auth', () => {
  const user = ref(null)
  let checked = false

  async function load() {
    if (checked) return
    try { user.value = await auth.me() }
    catch { user.value = null }
    checked = true
  }

  async function login(credentials) {
    user.value = await auth.login(credentials)
    checked = true
  }

  function clear() {
    user.value = null
    checked = true
  }

  async function logout() {
    try { await auth.logout() }
    catch (error) { if (error.response?.status !== 401) throw error }
    clear()
  }

  return { user, load, login, logout, clear }
})
