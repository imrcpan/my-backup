import { AlertTriangleIcon, InfoIcon } from 'lucide-react'
import { StepStateBadge } from '@/components/state-badge'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { Progress } from '@/components/ui/progress'
import { ScrollArea } from '@/components/ui/scroll-area'
import { formatBytes } from '@/lib/format'
import { useI18n, type MessageKey } from '@/lib/i18n'
import { useNotify } from '@/lib/notify'
import type { PlanLine, Run, RunStep } from '@/lib/types'
import { cn } from '@/lib/utils'

/** The note keys that are warnings about data being lost or a lot being read. */
const dangerous = new Set(['deletes_in_target', 'irreversible'])

const symbol: Record<string, { mark: string; tone: string }> = {
  new: { mark: '+', tone: 'text-emerald-600 dark:text-emerald-400' },
  copy: { mark: '+', tone: 'text-emerald-600 dark:text-emerald-400' },
  restore: { mark: '+', tone: 'text-emerald-600 dark:text-emerald-400' },
  modified: { mark: '~', tone: 'text-amber-600 dark:text-amber-400' },
  delete: { mark: '−', tone: 'text-red-600 dark:text-red-400' },
  remove: { mark: '−', tone: 'text-red-600 dark:text-red-400' },
  keep: { mark: '=', tone: 'text-muted-foreground' },
}

function Line({ line }: { line: PlanLine }) {
  const style = symbol[line.action] ?? { mark: '·', tone: 'text-muted-foreground' }
  return (
    <div className="flex items-baseline gap-2 px-3 py-0.5 font-mono text-xs">
      <span className={cn('w-3 shrink-0 text-center font-bold', style.tone)}>{style.mark}</span>
      <span className="min-w-0 flex-1 break-all">{line.path}</span>
      {line.size > 0 && <span className="shrink-0 text-muted-foreground">{formatBytes(line.size)}</span>}
    </div>
  )
}

function Stat({ children }: { children: React.ReactNode }) {
  return <span className="rounded-md bg-muted px-2 py-0.5 text-xs">{children}</span>
}

function StepCard({ step, target }: { step: RunStep; target: string }) {
  const { t } = useI18n()
  const notify = useNotify()
  const removes = step.kind === 'Forget'

  return (
    <Card size="sm">
      <CardHeader>
        <CardTitle className="flex items-center gap-2">
          {t(`step.kind.${step.kind}` as MessageKey)}
          <StepStateBadge state={step.state} />
        </CardTitle>
        <CardDescription className="font-mono break-all">{target}</CardDescription>
      </CardHeader>
      <CardContent className="flex flex-col gap-3">
        {(step.toAdd > 0 || step.toChange > 0 || step.toDelete > 0 || step.bytes > 0) && (
          <div className="flex flex-wrap gap-2">
            {step.toAdd > 0 && (
              <Stat>{t(step.kind === 'FileSync' ? 'step.copy' : 'step.new', { count: step.toAdd })}</Stat>
            )}
            {step.toChange > 0 && <Stat>{t('step.changed', { count: step.toChange })}</Stat>}
            {step.toDelete > 0 && <Stat>{t(removes ? 'step.remove' : 'step.delete', { count: step.toDelete })}</Stat>}
            {step.bytes > 0 && <Stat>{formatBytes(step.bytes)}</Stat>}
          </div>
        )}

        {step.notes.map((note) => (
          <Alert key={note} variant={dangerous.has(note) ? 'destructive' : 'default'}>
            {dangerous.has(note) ? <AlertTriangleIcon /> : <InfoIcon />}
            <AlertDescription>{t(`step.note.${note}` as MessageKey)}</AlertDescription>
          </Alert>
        ))}

        {step.errorCode && (
          <Alert variant="destructive">
            <AlertTriangleIcon />
            <AlertDescription>{notify.describeCode(step.errorCode, step.errorMessage)}</AlertDescription>
          </Alert>
        )}

        {step.state === 'Running' && (
          <div className="flex flex-col gap-1">
            <Progress value={Math.round(step.progress * 100)} />
            <span className="text-xs text-muted-foreground">
              {Math.round(step.progress * 100)}%{step.bytesDone > 0 && ` · ${formatBytes(step.bytesDone)}`}
            </span>
          </div>
        )}

        {step.lines.length > 0 && (
          <ScrollArea className="max-h-64 rounded-md border bg-muted/30">
            <div className="max-h-64 py-1">
              {step.lines.map((line, index) => (
                <Line key={`${index}-${line.path}`} line={line} />
              ))}
              {step.linesTruncated && <p className="px-3 py-1 text-xs text-muted-foreground">{t('step.lines.more')}</p>}
            </div>
          </ScrollArea>
        )}
        {step.lines.length === 0 && step.state === 'UpToDate' && (
          <p className="text-sm text-muted-foreground">{t('step.lines.none')}</p>
        )}
      </CardContent>
    </Card>
  )
}

/** The steps of a run: for a plan the diff that the user is asked to confirm, for a run its progress and result. */
export function RunPlan({ run, label }: { run: Run; label: (objectId: string) => string }) {
  return (
    <div className="flex flex-col gap-3">
      {run.steps.map((step) => (
        <StepCard key={step.id} step={step} target={label(step.targetObjectId)} />
      ))}
    </div>
  )
}
