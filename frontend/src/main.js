import { createApp } from 'vue'
import {
  ElAlert, ElAside, ElButton, ElConfigProvider, ElDatePicker, ElDescriptions, ElDescriptionsItem, ElDialog,
  ElDropdown, ElDropdownItem, ElDropdownMenu, ElEmpty, ElForm, ElFormItem, ElIcon, ElInput,
  ElInputNumber, ElLoading, ElOption, ElPagination, ElSelect, ElSwitch, ElTable, ElTableColumn, ElTag,
} from 'element-plus'
import 'element-plus/dist/index.css'
import { createPinia } from 'pinia'
import App from './App.vue'
import { router } from './router/index.js'
import { onUnauthorized } from './api/http.js'
import { useAuthStore } from './stores/auth.js'
import { useJobsStore } from './stores/jobs.js'
import './styles/main.css'

const pinia = createPinia()
const app = createApp(App).use(pinia)
for (const component of [
  ElAlert, ElAside, ElButton, ElConfigProvider, ElDatePicker, ElDescriptions, ElDescriptionsItem, ElDialog,
  ElDropdown, ElDropdownItem, ElDropdownMenu, ElEmpty, ElForm, ElFormItem, ElIcon, ElInput,
  ElInputNumber, ElOption, ElPagination, ElSelect, ElSwitch, ElTable, ElTableColumn, ElTag,
]) app.component(component.name, component)
app.use(ElLoading)

onUnauthorized(() => {
  useAuthStore(pinia).clear()
  useJobsStore(pinia).clear()
  const route = router.currentRoute.value
  if (route.name && route.name !== 'login') router.replace({ name: 'login', query: { next: route.fullPath } })
})
app.use(router).mount('#app')
