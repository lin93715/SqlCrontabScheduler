<script setup>
import { ref } from 'vue'
import CronEditor from './CronEditor.vue'
import SqlEditor from './SqlEditor.vue'

defineProps({ connections: { type: Array, required: true }, saving: Boolean })
const form = defineModel({ type: Object, required: true })
const emit = defineEmits(['save', 'cancel'])
const formRef = ref()
const rules = {
  name: [{ required: true, whitespace: true, message: '請輸入排程名稱。', trigger: 'blur' }],
  databaseConnectionId: [{ required: true, message: '請選擇目標資料庫。', trigger: 'change' }],
  sqlText: [{ required: true, whitespace: true, message: '請輸入 SQL 查詢。', trigger: 'submit' },
    { max: 100000, message: 'SQL 不可超過 100,000 字元。' }],
  timeoutSeconds: [{ type: 'number', required: true, min: 1, max: 300, message: '逾時須介於 1 至 300 秒。' }],
}

async function submit() {
  const valid = await formRef.value.validate().catch(() => false)
  if (valid) emit('save')
}
</script>

<template>
  <el-form ref="formRef" :model="form" :rules="rules" label-position="top" @submit.prevent="submit">
    <section class="panel editor-panel" aria-labelledby="basic-title">
      <h2 id="basic-title">基本資料</h2>
      <div class="form-grid">
        <el-form-item label="排程名稱" prop="name">
          <el-input v-model="form.name" maxlength="150" placeholder="例如 每日報表查詢" />
        </el-form-item>
        <el-form-item label="目標資料庫" prop="databaseConnectionId">
          <el-select v-model="form.databaseConnectionId" placeholder="選擇已核准的資料庫">
            <el-option v-for="item in connections" :key="item.id" :value="item.id" :label="item.name" />
          </el-select>
          <small v-if="!connections.length" class="field-note">目前沒有可用連線，請洽系統管理員。</small>
        </el-form-item>
      </div>
      <el-form-item label="說明" prop="description">
        <el-input v-model="form.description" maxlength="2000" type="textarea" :rows="2" placeholder="選填" />
      </el-form-item>
    </section>
    <section class="panel editor-panel" aria-labelledby="sql-title">
      <h2 id="sql-title">SQL 查詢</h2>
      <el-form-item prop="sqlText">
        <SqlEditor v-model="form.sqlText" />
      </el-form-item>
      <div class="form-grid compact-grid">
        <el-form-item label="逾時（秒）" prop="timeoutSeconds">
          <el-input-number v-model="form.timeoutSeconds" :min="1" :max="300" :precision="0" />
        </el-form-item>
        <el-form-item label="啟用排程">
          <el-switch v-model="form.enabled" aria-label="啟用排程" />
        </el-form-item>
      </div>
    </section>
    <section class="panel editor-panel" aria-labelledby="timing-title">
      <h2 id="timing-title">執行週期</h2>
      <CronEditor v-model:schedule="form.schedule" v-model:time-zone="form.timeZone" />
    </section>
    <div class="form-actions">
      <el-button :disabled="saving" @click="emit('cancel')">取消</el-button>
      <el-button type="primary" native-type="submit" :loading="saving">儲存排程</el-button>
    </div>
  </el-form>
</template>
