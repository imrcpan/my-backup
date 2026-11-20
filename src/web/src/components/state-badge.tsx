import { Badge } from '@/components/ui/badge'
import { useI18n, type MessageKey } from '@/lib/i18n'
import type { RunState, StepState } from '@/lib/types'
import { cn } from '@/lib/utils'

const tone: Record<RunState | StepState, string> = {
  Succeeded: 'border-emerald-500/40 text-emerald-600 dark:text-emerald-400',
  UpToDate: 'border-emerald-500/40 text-emerald-600 dark:text-emerald-400',
  Running: 'border-sky-500/40 text-sky-600 dark:text-sky-400',
  Planned: 'border-amber-500/40 text-amber-600 dark:text-amber-400',
  Pending: 'text-muted-foreground',
  Failed: 'border-red-500/40 text-red-600 dark:text-red-400',
  Blocked: 'border-red-500/40 text-red-600 dark:text-red-400',
  Interrupted: 'border-red-500/40 text-red-600 dark:text-red-400',
  Cancelled: 'text-muted-foreground',
  Skipped: 'text-muted-foreground',
  Discarded: 'text-muted-foreground',
}

export function RunStateBadge({ state }: { state: RunState }) {
  const { t } = useI18n()
  return (
    <Badge variant="outline" className={cn(tone[state])}>
      {t(`run.state.${state}` as MessageKey)}
    </Badge>
  )
}

export function StepStateBadge({ state }: { state: StepState }) {
  const { t } = useI18n()
  return (
    <Badge variant="outline" className={cn(tone[state])}>
      {t(`step.state.${state}` as MessageKey)}
    </Badge>
  )
}
