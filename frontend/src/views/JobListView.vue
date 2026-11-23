<script setup>
import { computed, onBeforeUnmount, ref, watch } from 'vue'
import { RouterLink } from 'vue-router'
import { MoreFilled, Plus, Refresh, Search } from '@element-plus/icons-vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import { jobs } from '../api/jobs.js'
import { errorText } from '../api/http.js'
import { useJobsStore } from '../stores/jobs.js'
import { usePreferences } from '../stores/preferences.js'
import { formatTime, scheduleTypes } from '../format.js'

const store = useJobsStore()
const preferences = usePreferences()
const search = ref('')
const filter = ref('')
const page = ref(1)
const size = ref(20)
const busy = ref('')
const params = computed(() => ({ search: filter.value || undefined, page: page.value, pageSize: size.value }))
const load = () => store.load(params.value)
watch(params, load, { immediate: true })
const timer = setInterval(() => { if (!store.loading && !busy.value) store.load(params.value, true) }, 5000)
onBeforeUnmount(() => clearInterval(timer))

function applySearch() {
  page.value = 1
  filter.value = search.value.trim()
}

async function act(job, action) {
  if (busy.value) return
  if (action === 'delete') {
    try {
      await ElMessageBox.confirm('確定刪除「' + job.name + '」？既有執行紀錄會依保留期限留存。',
        '刪除排程', { type: 'warning', confirmButtonText: '刪除', cancelButtonText: '取消' })
    } catch { return }
  }
  busy.value = job.id
  try {
    if (action === 'delete') {
      await jobs.remove(job.id)
      if (page.value > 1 && store.items.length === 1) page.value--
    } else await jobs.action(job.id, action)
    ElMessage.success(action === 'execute' ? '已加入手動執行佇列' : '已更新排程')
    await load()
  } catch (error) { ElMessage.error(errorText(error)) }
  finally { busy.value = '' }
}
</script>

<template>
  <header class="page-heading">
    <div><h1>排程管理</h1><p>管理 SQL 查詢、執行週期與排程狀態。</p></div>
    <RouterLink :to="{ name: 'job-new' }"><el-button type="primary" :icon="Plus">新增排程</el-button></RouterLink>
  </header>
  <section class="panel">
    <div class="toolbar">
      <el-input v-model="search" :prefix-icon="Search" clearable maxlength="150" placeholder="搜尋排程名稱"
        aria-label="搜尋排程名稱" @keyup.enter="applySearch" @clear="applySearch" />
      <el-button @click="applySearch">搜尋</el-button>
      <el-button class="toolbar-end" :icon="Refresh" @click="load">重新整理</el-button>
    </div>
    <el-alert v-if="store.error" :title="store.error" type="error" show-icon :closable="false" />
    <el-table v-loading="store.loading" :data="store.items" row-key="id" class="data-table" empty-text="沒有符合條件的排程">
      <el-table-column label="排程名稱" min-width="195">
        <template #default="{ row }">
          <RouterLink :to="{ name: 'job-edit', params: { id: row.id } }" class="table-link">{{ row.name }}</RouterLink>
          <div v-if="row.description" class="table-subtext">{{ row.description }}</div>
        </template>
      </el-table-column>
      <el-table-column prop="connectionName" label="目標資料庫" min-width="125" show-overflow-tooltip />
      <el-table-column label="週期" min-width="136">
        <template #default="{ row }">{{ scheduleTypes[row.scheduleType] }}<div class="table-subtext"><code>{{ row.cronExpression }}</code></div></template>
      </el-table-column>
      <el-table-column label="狀態" min-width="100">
        <template #default="{ row }">
          <el-tag effect="light" :type="!row.enabled ? 'info' : row.paused ? 'warning' : 'success'">
            {{ !row.enabled ? '已停用' : row.paused ? '已暫停' : '已啟用' }}
          </el-tag>
          <div v-if="row.syncPending" class="table-subtext">等待同步</div>
        </template>
      </el-table-column>
      <el-table-column label="下次執行" min-width="150">
        <template #default="{ row }">{{ formatTime(row.nextRunUtc, preferences.timeZone) }}</template>
      </el-table-column>
      <el-table-column label="操作" min-width="170">
        <template #default="{ row }">
          <div class="row-actions">
            <RouterLink :to="{ name: 'executions', query: { jobId: row.id, jobName: row.name } }" class="table-link">紀錄</RouterLink>
            <el-button link type="primary" :disabled="!row.enabled || !!busy" :loading="busy === row.id" @click="act(row, 'execute')">立即執行</el-button>
            <el-dropdown trigger="click" @command="action => act(row, action)">
              <el-button text :icon="MoreFilled" :disabled="!!busy" :aria-label="'管理 ' + row.name" />
              <template #dropdown>
                <el-dropdown-menu>
                  <el-dropdown-item :command="row.enabled ? 'disable' : 'enable'">{{ row.enabled ? '停用' : '啟用' }}</el-dropdown-item>
                  <el-dropdown-item :disabled="!row.enabled" :command="row.paused ? 'resume' : 'pause'">{{ row.paused ? '恢復' : '暫停' }}</el-dropdown-item>
                  <el-dropdown-item command="delete" divided>刪除</el-dropdown-item>
                </el-dropdown-menu>
              </template>
            </el-dropdown>
          </div>
        </template>
      </el-table-column>
    </el-table>
    <div class="pagination">
      <el-pagination v-model:current-page="page" v-model:page-size="size" :page-sizes="[10, 20, 50]"
        :total="store.total" layout="total, prev, pager, next, sizes" />
    </div>
  </section>
</template>
