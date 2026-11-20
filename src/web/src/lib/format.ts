import type { MessageKey } from '@/lib/i18n'
import type { RetentionPolicy } from '@/lib/types'

const units = ['B', 'KB', 'MB', 'GB', 'TB']

export function formatBytes(bytes: number | undefined): string {
  if (bytes == null) return '—'
  let value = bytes
  let unit = 0
  while (value >= 1024 && unit < units.length - 1) {
    value /= 1024
    unit++
  }
  return `${unit === 0 ? value : value.toFixed(value >= 100 ? 0 : 1)} ${units[unit]}`
}

export function formatTime(iso: string | undefined, locale: string): string {
  if (!iso) return '—'
  return new Date(iso).toLocaleString(locale === 'zh' ? 'zh-CN' : 'en-GB', {
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
    hour: '2-digit',
    minute: '2-digit',
  })
}

const retentionKeys: [keyof RetentionPolicy, MessageKey][] = [
  ['keepLast', 'retention.summary.last'],
  ['keepDaily', 'retention.summary.daily'],
  ['keepWeekly', 'retention.summary.weekly'],
  ['keepMonthly', 'retention.summary.monthly'],
  ['keepYearly', 'retention.summary.yearly'],
]

/** "last 3, monthly 12" for a policy; empty if it keeps nothing. */
export function describeRetention(
  policy: RetentionPolicy | undefined,
  t: (key: MessageKey, params?: Record<string, string | number>) => string,
): string {
  if (!policy) return ''
  return retentionKeys
    .filter(([field]) => (policy[field] ?? 0) > 0)
    .map(([field, key]) => t(key, { n: policy[field]! }))
    .join(', ')
}
