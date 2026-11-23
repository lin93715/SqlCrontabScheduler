<script setup>
import { computed, ref, watch } from 'vue'
import { RouterLink, useRoute, useRouter } from 'vue-router'
import { ArrowLeft } from '@element-plus/icons-vue'
import { ElMessage } from 'element-plus'
import { jobs } from '../api/jobs.js'
import { errorText } from '../api/http.js'
import JobForm from '../components/JobForm.vue'
import { defaultZone } from '../stores/preferences.js'
import { schedulePayload } from '../format.js'

const route = useRoute()
const router = useRouter()
const id = computed(() => route.params.id)
const connections = ref([])
const form = ref(null)
const loadError = ref('')
const saveError = ref('')
const loading = ref(false)
const saving = ref(false)
const initialSchedule = { type: 'everyMinutes', intervalMinutes: 5, hour: 9, minute: 0, dayOfWeek: 'monday', dayOfMonth: 1 }

let loadSequence = 0
async function load() {
  const sequence = ++loadSequence
  const knownId = id.value
  loading.value = true
  loadError.value = ''
  saveError.value = ''
  form.value = null
  try {
    const [available, job] = await Promise.all([jobs.connections(), knownId ? jobs.get(knownId) : null])
    if (sequence !== loadSequence) return
    connections.value = available
    form.value = {
      name: job?.name ?? '', description: job?.description ?? '',
      databaseConnectionId: job?.databaseConnectionId ?? (available.length === 1 ? available[0].id : ''),
      sqlText: job?.sqlText ?? '', timeoutSeconds: job?.timeoutSeconds ?? 30,
      enabled: job?.enabled ?? true, timeZone: job?.timeZone ?? defaultZone, revision: job?.revision ?? null,
      schedule: { ...initialSchedule, ...Object.fromEntries(Object.entries(job?.schedule || {}).filter(([, value]) => value != null)) },
    }
  } catch (error) {
    if (loadSequence === sequence) loadError.value = errorText(error, '無法載入排程編輯器。')
  } finally {
    if (loadSequence === sequence) loading.value = false
  }
}
watch(id, load, { immediate: true })

async function save() {
  if (saving.value) return
  saving.value = true
  saveError.value = ''
  try {
    const payload = { ...form.value, schedule: schedulePayload(form.value.schedule) }
    if (id.value) await jobs.update(id.value, payload)
    else await jobs.create(payload)
    ElMessage.success('已儲存排程')
    await router.push({ name: 'jobs' })
  } catch (error) {
    saveError.value = errorText(error, '儲存排程失敗。')
    window.scrollTo({ top: 0, behavior: 'smooth' })
  } finally { saving.value = false }
}
</script>

<template>
  <div class="editor-page">
    <RouterLink :to="{ name: 'jobs' }" class="back-link"><el-icon><ArrowLeft /></el-icon> 返回排程管理</RouterLink>
    <header class="page-heading"><div><h1>{{ id ? '編輯排程' : '新增排程' }}</h1><p>設定 SQL 查詢、目標資料庫與執行時間。</p></div></header>
    <div v-loading="loading" class="editor-content">
      <el-alert v-if="loadError" :title="loadError" type="error" :closable="false" show-icon />
      <el-button v-if="loadError" class="retry-button" @click="load">重新載入</el-button>
      <el-alert v-if="saveError" :title="saveError" type="error" :closable="false" show-icon />
      <JobForm v-if="form" v-model="form" :connections="connections" :saving="saving" @save="save"
        @cancel="$router.push({ name: 'jobs' })" />
    </div>
  </div>
</template>
