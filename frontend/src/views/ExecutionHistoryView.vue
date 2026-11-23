<script setup>
import { computed, onBeforeUnmount, ref, watch } from 'vue'
import { RouterLink, useRoute } from 'vue-router'
import { Refresh, Search } from '@element-plus/icons-vue'
import { executions } from '../api/executions.js'
import { errorText } from '../api/http.js'
import { browserZone } from '../stores/preferences.js'
import { statuses } from '../format.js'
import ExecutionTable from '../components/ExecutionTable.vue'

const route = useRoute()
const edits = ref({ jobName: '', status: '', range: null })
const applied = ref({})
const page = ref(1)
const size = ref(20)
const items = ref([])
const total = ref(0)
const loading = ref(false)
const error = ref('')
const jobId = computed(() => typeof route.query.jobId === 'string' ? route.query.jobId : undefined)
const query = computed(() => ({ ...applied.value, page: page.value, pageSize: size.value }))
let sequence = 0

function apply() {
  page.value = 1
  applied.value = {
    jobName: edits.value.jobName.trim() || undefined,
    status: edits.value.status || undefined,
    fromUtc: edits.value.range?.[0]?.toISOString(),
    toUtc: edits.value.range?.[1]?.toISOString(),
  }
}

async function load(silent = false) {
  const current = ++sequence
  if (!silent) loading.value = true
  try {
    const result = await executions.list(query.value, jobId.value)
    if (sequence !== current) return
    items.value = result.items
    total.value = result.total
    error.value = ''
  } catch (cause) {
    if (sequence === current) error.value = errorText(cause, '無法載入執行紀錄。')
  } finally { if (sequence === current) loading.value = false }
}
watch([query, jobId], () => load(), { immediate: true })
const timer = setInterval(() => { if (!loading.value) load(true) }, 5000)
onBeforeUnmount(() => { clearInterval(timer); ++sequence })
</script>

<template>
  <header class="page-heading">
    <div><h1>執行紀錄</h1><p>依排程、狀態與時間查詢 SQL 執行結果。</p></div>
    <el-button :icon="Refresh" @click="load()">重新整理</el-button>
  </header>
  <section class="panel">
    <div v-if="jobId" class="active-filter">
      <el-tag>{{ route.query.jobName || jobId }}</el-tag>
      <RouterLink :to="{ name: 'executions' }" class="table-link">清除排程篩選</RouterLink>
    </div>
    <el-form class="history-filters" label-position="top" @submit.prevent="apply">
      <el-form-item label="排程名稱">
        <el-input v-model="edits.jobName" maxlength="150" clearable :prefix-icon="Search" placeholder="輸入排程名稱" />
      </el-form-item>
      <el-form-item label="執行狀態">
        <el-select v-model="edits.status" clearable placeholder="全部狀態">
          <el-option v-for="([label], value) in statuses" :key="value" :label="label" :value="value" />
        </el-select>
      </el-form-item>
      <el-form-item :label="'執行時間（' + browserZone + '）'" class="history-range">
        <el-date-picker v-model="edits.range" type="datetimerange" start-placeholder="開始時間" end-placeholder="結束時間"
          range-separator="至" format="YYYY-MM-DD HH:mm:ss" />
      </el-form-item>
      <el-form-item><el-button type="primary" native-type="submit">查詢</el-button></el-form-item>
    </el-form>
    <el-alert v-if="error" :title="error" type="error" show-icon :closable="false" />
    <ExecutionTable :items="items" :loading="loading" />
    <div class="pagination">
      <el-pagination v-model:current-page="page" v-model:page-size="size" :page-sizes="[10, 20, 50]"
        :total="total" layout="total, prev, pager, next, sizes" />
    </div>
  </section>
</template>
