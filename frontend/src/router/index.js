import { createRouter, createWebHistory } from 'vue-router'
import { useAuthStore } from '../stores/auth.js'

export const router = createRouter({
  history: createWebHistory(),
  routes: [
    { path: '/login', name: 'login', component: () => import('../views/LoginView.vue'), meta: { title: '登入' } },
    {
      path: '/', component: () => import('../layouts/MainLayout.vue'), children: [
        { path: '', name: 'dashboard', component: () => import('../views/DashboardView.vue'), meta: { title: '總覽' } },
        { path: 'jobs', name: 'jobs', component: () => import('../views/JobListView.vue'), meta: { title: '排程管理' } },
        { path: 'jobs/new', name: 'job-new', component: () => import('../views/JobEditorView.vue'), meta: { title: '新增排程' } },
        { path: 'jobs/:id/edit', name: 'job-edit', component: () => import('../views/JobEditorView.vue'), meta: { title: '編輯排程' } },
        { path: 'executions', name: 'executions', component: () => import('../views/ExecutionHistoryView.vue'), meta: { title: '執行紀錄' } },
      ],
    },
    { path: '/:pathMatch(.*)*', redirect: '/' },
  ],
  scrollBehavior: () => ({ top: 0 }),
})

router.beforeEach(async to => {
  const auth = useAuthStore()
  await auth.load()
  if (!auth.user && to.name !== 'login') return { name: 'login', query: { next: to.fullPath } }
  if (auth.user && to.name === 'login') return { name: 'dashboard' }
})
