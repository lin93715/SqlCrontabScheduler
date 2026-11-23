<script setup>
import { computed, onBeforeUnmount, ref } from 'vue'
import { RouterLink } from 'vue-router'
import { Calendar, CircleCheck, Clock, CircleClose, Plus, Refresh, VideoPlay } from '@element-plus/icons-vue'
import { executions } from '../api/executions.js'
import { errorText } from '../api/http.js'
import ExecutionTable from '../components/ExecutionTable.vue'

const summary = ref(null)
const loading = ref(false)
const error = ref('')
const metrics = [
  { key: 'totalJobs', label: '排程總數', icon: Calendar, tone: 'blue' },
  { key: 'enabledJobs', label: '已啟用排程', icon: CircleCheck, tone: 'green' },
  { key: 'runningJobs', label: '執行中', icon: VideoPlay, tone: 'blue' },
  { key: 'recentSuccess', label: '成功 · 近 24 小時', icon: Clock, tone: 'green' },
  { key: 'recentFailure', label: '失敗或逾時 · 近 24 小時', icon: CircleClose, tone: 'red' },
]
const recent = computed(() => summary.value?.recentExecutions ?? [])
let active = true

async function load(silent = false) {
  if (loading.value) return
  if (!silent) loading.value = true
  try {
    const result = await executions.summary()
    if (!active) return
    summary.value = result
    error.value = ''
  } catch (cause) {
    if (active) error.value = errorText(cause, '無法載入總覽。')
  } finally { if (active) loading.value = false }
}
load()
const timer = setInterval(() => load(true), 5000)
onBeforeUnmount(() => { active = false; clearInterval(timer) })
</script>

<template>
  <header class="page-heading">
    <div><h1>排程總覽</h1><p>掌握目前的 SQL 排程與最近執行情況。</p></div>
    <RouterLink :to="{ name: 'job-new' }"><el-button type="primary" :icon="Plus">新增排程</el-button></RouterLink>
  </header>

  <el-alert v-if="error" :title="error" type="error" show-icon :closable="false" class="dashboard-alert" />
  <el-alert v-if="summary?.pendingSync"
    :title="summary.pendingSync + ' 筆排程等待 Worker 同步'" type="info" show-icon :closable="false" class="dashboard-alert" />
  <div class="metrics" aria-label="排程統計">
    <article v-for="tile in metrics" :key="tile.key" class="metric-card">
      <span class="metric-icon" :class="tile.tone"><el-icon><component :is="tile.icon" /></el-icon></span>
      <span class="metric-label">{{ tile.label }}</span>
      <strong>{{ summary?.[tile.key] ?? '—' }}</strong>
    </article>
  </div>

  <section class="panel" aria-labelledby="recent-executions">
    <div class="panel-heading">
      <h2 id="recent-executions">最近執行紀錄</h2>
      <div class="panel-actions">
        <el-button text :icon="Refresh" :loading="loading" aria-label="更新總覽" @click="load()" />
        <RouterLink :to="{ name: 'executions' }" class="table-link">查看全部</RouterLink>
      </div>
    </div>
    <ExecutionTable :items="recent" :loading="loading" />
  </section>
</template>
