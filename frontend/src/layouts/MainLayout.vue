<script setup>
import { computed, ref } from 'vue'
import { RouterLink, RouterView, useRoute, useRouter } from 'vue-router'
import { Calendar, Clock, Coin, DataAnalysis, Expand, Fold } from '@element-plus/icons-vue'
import { ElMessage } from 'element-plus'
import { useAuthStore } from '../stores/auth.js'
import { useJobsStore } from '../stores/jobs.js'
import { usePreferences, zoneOptions } from '../stores/preferences.js'
import { errorText } from '../api/http.js'

const auth = useAuthStore()
const preferences = usePreferences()
const router = useRouter()
const route = useRoute()
const menuOpen = ref(false)
const signingOut = ref(false)
const jobPage = computed(() => ['jobs', 'job-new', 'job-edit'].includes(route.name))

async function signOut() {
  signingOut.value = true
  try {
    await auth.logout()
    useJobsStore().clear()
    await router.replace({ name: 'login' })
  } catch (error) { ElMessage.error(errorText(error, '登出失敗。')) }
  finally { signingOut.value = false }
}
</script>

<template>
  <div class="app-shell">
    <aside class="sidebar" :class="{ 'menu-open': menuOpen }">
      <RouterLink :to="{ name: 'dashboard' }" class="brand" @click="menuOpen = false">
        <span class="brand-mark"><el-icon><Coin /></el-icon></span>
        <span>SQL Scheduler<small>排程管理中心</small></span>
      </RouterLink>
      <nav aria-label="主要導覽" class="navigation">
        <RouterLink :to="{ name: 'dashboard' }" exact-active-class="nav-active" @click="menuOpen = false">
          <el-icon><DataAnalysis /></el-icon><span>總覽</span>
        </RouterLink>
        <RouterLink :to="{ name: 'jobs' }" :class="{ 'nav-active': jobPage }" @click="menuOpen = false">
          <el-icon><Calendar /></el-icon><span>排程管理</span>
        </RouterLink>
        <RouterLink :to="{ name: 'executions' }" exact-active-class="nav-active" @click="menuOpen = false">
          <el-icon><Clock /></el-icon><span>執行紀錄</span>
        </RouterLink>
      </nav>
    </aside>
    <div class="workspace">
      <header class="topbar">
        <div class="header-location">
          <el-button class="mobile-menu" text :icon="menuOpen ? Fold : Expand"
            :aria-expanded="menuOpen" aria-label="切換導覽" @click="menuOpen = !menuOpen" />
          <span>{{ route.meta.title }}</span>
        </div>
        <div class="header-controls">
          <el-select class="header-zone" :model-value="preferences.timeZone" filterable aria-label="顯示時區"
            @update:model-value="preferences.setTimeZone">
            <el-option v-for="zone in zoneOptions" :key="zone" :label="zone" :value="zone" />
          </el-select>
          <el-dropdown trigger="click" @command="signOut">
            <button class="profile" type="button" :disabled="signingOut">
              <span class="avatar">{{ auth.user?.username?.slice(0, 1).toUpperCase() }}</span>
              <span class="profile-label">{{ auth.user?.username }}<small>{{ auth.user?.role === 'Admin' ? '管理員' : '一般使用者' }}</small></span>
            </button>
            <template #dropdown><el-dropdown-menu><el-dropdown-item command="logout">登出</el-dropdown-item></el-dropdown-menu></template>
          </el-dropdown>
        </div>
      </header>
      <main class="page-content"><RouterView /></main>
    </div>
  </div>
</template>
