import { describe, expect, it } from 'vitest'
import { formatDuration, formatTime, schedulePayload } from '../src/format.js'

describe('schedule and execution presentation', () => {
  it('sends structured schedule fields without a client-generated cron', () => {
    expect(schedulePayload({ type: 'weekly', dayOfWeek: 'monday', hour: 9, minute: 0, intervalMinutes: 5 }))
      .toEqual({ type: 'weekly', dayOfWeek: 'monday', hour: 9, minute: 0 })
  })

  it('formats UTC instants in the selected time zone', () => {
    const instant = '2026-11-23T18:30:00Z'
    expect(formatTime(instant, 'Asia/Taipei').replace(/\s+/gu, ' ')).toContain('2026/11/24 02:30')
    expect(formatTime(instant, 'UTC').replace(/\s+/gu, ' ')).toContain('2026/11/23 18:30')
  })

  it('preserves a zero duration and distinguishes missing durations', () => {
    expect(formatDuration(0)).toBe('0 ms')
    expect(formatDuration(1700)).toBe('1.7 s')
    expect(formatDuration(null)).toBe('—')
  })
})
