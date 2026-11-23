<script setup>
import { onBeforeUnmount, onMounted, ref, watch } from 'vue'

const value = defineModel({ type: String, default: '' })
const container = ref()
const failed = ref(false)
const starting = ref(true)
let editor
let active = true

onMounted(async () => {
  try {
    const { mountSqlEditor } = await import('./monaco.js')
    if (active) editor = mountSqlEditor(container.value, value.value, text => { value.value = text })
  } catch {
    if (active) failed.value = true
  } finally {
    if (active) starting.value = false
  }
})
watch(value, text => editor?.setValue(text))
onBeforeUnmount(() => { active = false; editor?.dispose() })
</script>

<template>
  <div class="sql-editor">
    <div class="sql-editor-bar"><span>PostgreSQL</span><span>唯讀查詢</span></div>
    <div v-if="!failed" ref="container" v-loading="starting" class="sql-monaco" />
    <template v-else>
      <el-alert title="Monaco Editor 載入失敗，仍可在文字欄位編輯 SQL。" type="warning" :closable="false" />
      <el-input v-model="value" type="textarea" :rows="10" aria-label="SQL 查詢" />
    </template>
  </div>
</template>
