<script setup>
import { computed, onBeforeUnmount, ref, watch } from 'vue'
import { jobs } from '../api/jobs.js'
import { errorText } from '../api/http.js'
import { formatTime, schedulePayload, scheduleTypes } from '../format.js'
import { zoneOptions } from '../stores/preferences.js'

const schedule = defineModel('schedule', { type: Object, required: true })
const timeZone = defineModel('timeZone', { type: String, required: true })
const preview = ref(null)
const error = ref('')
const loading = ref(false)
const week = [
  ['monday', '星期一'], ['tuesday', '星期二'], ['wednesday', '星期三'], ['thursday', '星期四'],
  ['friday', '星期五'], ['saturday', '星期六'], ['sunday', '星期日'],
]
const payload = computed(() => ({ schedule: schedulePayload(schedule.value), timeZone: timeZone.value }))
let timer
let request

watch(payload, (value) => {
  clearTimeout(timer)
  request?.abort()
  preview.value = null
  error.value = ''
  loading.value = true
  const controller = new AbortController()
  request = controller
  timer = setTimeout(async () => {
    try {
      const result = await jobs.preview(value, controller.signal)
      if (!controller.signal.aborted) preview.value = result
    }
    catch (cause) { if (!controller.signal.aborted) error.value = errorText(cause, '無法預覽排程。') }
    finally { if (!controller.signal.aborted) loading.value = false }
  }, 220)
}, { immediate: true })

onBeforeUnmount(() => { clearTimeout(timer); request?.abort() })
</script>

<template>
  <div class="schedule-grid">
    <el-form-item label="週期">
      <el-select v-model="schedule.type" aria-label="週期">
        <el-option v-for="(label, type) in scheduleTypes" :key="type" :label="label" :value="type" />
      </el-select>
    </el-form-item>
    <el-form-item label="排程時區">
      <el-select v-model="timeZone" filterable allow-create default-first-option aria-label="排程時區">
        <el-option v-for="zone in zoneOptions" :key="zone" :label="zone" :value="zone" />
      </el-select>
    </el-form-item>
    <el-form-item v-if="schedule.type === 'everyMinutes'" label="每隔幾分鐘">
      <el-input-number v-model="schedule.intervalMinutes" :min="1" :max="59" :precision="0" aria-label="每隔幾分鐘" />
      <small class="field-note">每小時從第 00 分鐘重新計算間隔。</small>
    </el-form-item>
    <template v-else>
      <el-form-item v-if="schedule.type === 'weekly'" label="星期">
        <el-select v-model="schedule.dayOfWeek" aria-label="星期">
          <el-option v-for="[value, label] in week" :key="value" :value="value" :label="label" />
        </el-select>
      </el-form-item>
      <el-form-item v-if="schedule.type === 'monthly'" label="每月日期">
        <el-input-number v-model="schedule.dayOfMonth" :min="1" :max="31" :precision="0" aria-label="每月日期" />
        <small class="field-note">缺少指定日期的月份會跳過。</small>
      </el-form-item>
      <el-form-item v-if="schedule.type !== 'hourly'" label="小時（24 小時制）">
        <el-input-number v-model="schedule.hour" :min="0" :max="23" :precision="0" aria-label="小時" />
      </el-form-item>
      <el-form-item label="分鐘">
        <el-input-number v-model="schedule.minute" :min="0" :max="59" :precision="0" aria-label="分鐘" />
      </el-form-item>
    </template>
  </div>
  <div class="cron-preview" aria-live="polite">
    <div v-if="loading" class="muted">正在計算執行時間…</div>
    <el-alert v-else-if="error" type="warning" :title="error" show-icon :closable="false" />
    <template v-else-if="preview">
      <div class="preview-meta">
        <div><span>Cron Expression</span><code>{{ preview.cronExpression }}</code></div>
        <div><span>下次預計執行</span><strong>{{ formatTime(preview.nextRunUtc, preview.timeZone, true) }}</strong></div>
      </div>
      <details v-if="preview.upcomingUtc.length" open>
        <summary>接下來 {{ preview.upcomingUtc.length }} 次（{{ preview.timeZone }}）</summary>
        <ol><li v-for="instant in preview.upcomingUtc" :key="instant">{{ formatTime(instant, preview.timeZone, true) }}</li></ol>
      </details>
    </template>
  </div>
</template>
