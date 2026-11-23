export const scheduleTypes = {
  everyMinutes: '分鐘間隔', hourly: '每小時', daily: '每天', weekly: '每週', monthly: '每月',
}

export const statuses = {
  running: ['執行中', 'primary'],
  success: ['成功', 'success'],
  failed: ['失敗', 'danger'],
  timeout: ['逾時', 'warning'],
  cancelled: ['已取消', 'info'],
  skipped: ['已略過', 'info'],
}

export function formatTime(value, timeZone, seconds = false) {
  if (!value) return '—'
  return new Intl.DateTimeFormat('zh-TW', {
    timeZone, year: 'numeric', month: '2-digit', day: '2-digit', hour: '2-digit', minute: '2-digit',
    ...(seconds ? { second: '2-digit' } : {}), hourCycle: 'h23',
  }).format(new Date(value))
}

export function formatDuration(value) {
  if (value == null) return '—'
  if (value < 1000) return value + ' ms'
  return (value / 1000).toLocaleString('zh-TW', { maximumFractionDigits: 2 }) + ' s'
}

export function schedulePayload(schedule) {
  const { type, intervalMinutes, minute, hour, dayOfWeek, dayOfMonth } = schedule
  const fields = {
    everyMinutes: { intervalMinutes },
    hourly: { minute },
    daily: { hour, minute },
    weekly: { dayOfWeek, hour, minute },
    monthly: { dayOfMonth, hour, minute },
  }
  return { type, ...fields[type] }
}
