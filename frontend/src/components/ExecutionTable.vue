<script setup>
import { ref } from 'vue'
import { executions } from '../api/executions.js'
import { errorText } from '../api/http.js'
import { formatDuration, formatTime, statuses } from '../format.js'
import { usePreferences } from '../stores/preferences.js'

defineProps({ items: { type: Array, required: true }, loading: Boolean })
const preferences = usePreferences()
const visible = ref(false)
const detailLoading = ref(false)
const detail = ref(null)
const detailError = ref('')
let sequence = 0

async function show(id) {
  const current = ++sequence
  detail.value = null
  detailError.value = ''
  visible.value = true
  detailLoading.value = true
  try {
    const result = await executions.get(id)
    if (current === sequence) detail.value = result
  } catch (cause) {
    if (current === sequence) detailError.value = errorText(cause, '無法載入執行詳細資料。')
  } finally { if (current === sequence) detailLoading.value = false }
}

function status(value) { return statuses[value] ?? [value, 'info'] }
function time(value) { return formatTime(value, preferences.timeZone, true) }
</script>

<template>
  <el-table v-loading="loading" :data="items" row-key="id" class="data-table" empty-text="沒有符合條件的執行紀錄">
    <el-table-column prop="jobName" label="排程名稱" min-width="165" show-overflow-tooltip />
    <el-table-column label="觸發方式" min-width="95">
      <template #default="{ row }">{{ row.triggerType === 'manual' ? '手動' : '排程' }}</template>
    </el-table-column>
    <el-table-column label="執行時間" min-width="170">
      <template #default="{ row }">{{ time(row.startUtc || row.scheduledUtc) }}</template>
    </el-table-column>
    <el-table-column label="耗時" min-width="85">
      <template #default="{ row }">{{ formatDuration(row.durationMs) }}</template>
    </el-table-column>
    <el-table-column label="狀態" min-width="100">
      <template #default="{ row }"><el-tag :type="status(row.status)[1]" effect="light">{{ status(row.status)[0] }}</el-tag></template>
    </el-table-column>
    <el-table-column prop="errorMessage" label="錯誤摘要" min-width="170" show-overflow-tooltip>
      <template #default="{ row }">{{ row.errorMessage || '—' }}</template>
    </el-table-column>
    <el-table-column label="詳細" width="75">
      <template #default="{ row }"><el-button link type="primary" @click="show(row.id)">查看</el-button></template>
    </el-table-column>
  </el-table>

  <el-dialog v-model="visible" title="執行詳細紀錄" width="min(620px, 92vw)" destroy-on-close>
    <div v-loading="detailLoading" class="execution-detail">
      <el-alert v-if="detailError" :title="detailError" type="error" show-icon :closable="false" />
      <el-descriptions v-else-if="detail" :column="1" border>
        <el-descriptions-item label="執行 ID"><code>{{ detail.id }}</code></el-descriptions-item>
        <el-descriptions-item label="排程名稱">{{ detail.jobName }}</el-descriptions-item>
        <el-descriptions-item label="觸發方式">{{ detail.triggerType === 'manual' ? '手動' : '排程' }}</el-descriptions-item>
        <el-descriptions-item label="狀態"><el-tag :type="status(detail.status)[1]">{{ status(detail.status)[0] }}</el-tag></el-descriptions-item>
        <el-descriptions-item label="預計時間">{{ time(detail.scheduledUtc) }}</el-descriptions-item>
        <el-descriptions-item label="開始時間">{{ time(detail.startUtc) }}</el-descriptions-item>
        <el-descriptions-item label="結束時間">{{ time(detail.endUtc) }}</el-descriptions-item>
        <el-descriptions-item label="耗時">{{ formatDuration(detail.durationMs) }}</el-descriptions-item>
        <el-descriptions-item label="讀取筆數">{{ detail.rowsRead ?? '—' }}<span v-if="detail.rowLimitReached">（已達上限）</span></el-descriptions-item>
        <el-descriptions-item label="影響筆數">{{ detail.rowsAffected ?? '—' }}</el-descriptions-item>
        <el-descriptions-item v-if="detail.errorMessage" label="錯誤摘要">{{ detail.errorMessage }}</el-descriptions-item>
        <el-descriptions-item v-if="detail.errorDetail" label="診斷代碼"><code>{{ detail.errorDetail }}</code></el-descriptions-item>
      </el-descriptions>
    </div>
  </el-dialog>
</template>
