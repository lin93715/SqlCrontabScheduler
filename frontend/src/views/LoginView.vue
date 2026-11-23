<script setup>
import { reactive, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { Coin, Lock, User } from '@element-plus/icons-vue'
import { useAuthStore } from '../stores/auth.js'
import { errorText } from '../api/http.js'

const auth = useAuthStore()
const route = useRoute()
const router = useRouter()
const form = reactive({ username: '', password: '' })
const loading = ref(false)
const error = ref('')

async function submit() {
  if (loading.value) return
  if (!form.username.trim() || !form.password) {
    error.value = '請輸入帳號及密碼。'
    return
  }
  error.value = ''
  loading.value = true
  try {
    await auth.login(form)
    const next = route.query.next
    await router.replace(typeof next === 'string' && next.startsWith('/') && !next.startsWith('//') &&
      router.resolve(next).name !== 'login' ? next : '/')
  } catch (cause) { error.value = errorText(cause, '無法登入，請確認 API 已啟動。') }
  finally {
    form.password = ''
    loading.value = false
  }
}
</script>

<template>
  <main class="login-screen">
    <section class="login-card" aria-labelledby="login-title">
      <span class="brand-mark"><el-icon><Coin /></el-icon></span>
      <h1 id="login-title">SQL Scheduler</h1>
      <p class="muted">登入排程管理中心</p>
      <el-alert v-if="error" :title="error" type="error" show-icon :closable="false" />
      <el-form label-position="top" @submit.prevent="submit">
        <el-form-item label="帳號" required>
          <el-input v-model="form.username" :prefix-icon="User" autocomplete="username" maxlength="100" />
        </el-form-item>
        <el-form-item label="密碼" required>
          <el-input v-model="form.password" :prefix-icon="Lock" type="password" autocomplete="current-password" show-password />
        </el-form-item>
        <el-button type="primary" native-type="submit" :loading="loading" class="login-submit">登入</el-button>
      </el-form>
    </section>
  </main>
</template>
