import { AlertTriangleIcon, DownloadIcon, InfoIcon } from 'lucide-react'
import { StepStateBadge } from '@/components/state-badge'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { Card, CardAction, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { Progress } from '@/components/ui/progress'
import { ScrollArea } from '@/components/ui/scroll-area'
import { formatBytes } from '@/lib/format'
import { useI18n, type MessageKey } from '@/lib/i18n'
import { useNotify } from '@/lib/notify'
import type { PlanLine, Run, RunStep } from '@/lib/types'
import { cn } from '@/lib/utils'

/** The note keys that are warnings about data being lost or a lot being read. */
const dangerous = new Set(['deletes_in_target', 'irreversible', 'partial_output_left'])

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

function Stat({ children, danger = false }: { children: React.ReactNode; danger?: boolean }) {
  return (
    <span
      className={cn(
        'rounded-md px-2 py-0.5 text-xs',
        danger ? 'bg-red-500/15 text-red-600 dark:text-red-400' : 'bg-muted',
      )}
    >
      {children}
    </span>
  )
}

type Group = 'delete' | 'remove' | 'changed' | 'add'

function groupOf(line: PlanLine): Group {
  if (line.action === 'delete') return 'delete'
  if (line.action === 'remove') return 'remove'
  if (line.action === 'modified') return 'changed'
  return 'add'
}

/** The lines in the order they come (the server puts what is deleted first), with a heading where the kind changes. */
function Lines({ lines }: { lines: PlanLine[] }) {
  const { t } = useI18n()
  const kinds = new Set(lines.map(groupOf))
  const headings = kinds.size > 1 || kinds.has('delete') || kinds.has('remove')
  let previous: Group | undefined
  return (
    <>
      {lines.map((line, index) => {
        const group = groupOf(line)
        const heading = headings && group !== previous
        previous = group
        return (
          <div key={`${index}-${line.path}`}>
            {heading && (
              <p
                className={cn(
                  'px-3 pt-2 pb-1 text-xs font-medium',
                  group === 'delete' || group === 'remove' ? 'text-red-600 dark:text-red-400' : 'text-muted-foreground',
                )}
              >
                {t(`step.group.${group}` as MessageKey)}
              </p>
            )}
            <Line line={line} />
          </div>
        )
      })}
    </>
  )
}

function StepCard({ runId, step, target }: { runId: string; step: RunStep; target: string }) {
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
        {step.hasDiff && step.lines.length > 0 && (
          <CardAction>
            <Button
              variant="outline"
              size="sm"
              render={
                <a href={`/api/runs/${encodeURIComponent(runId)}/steps/${encodeURIComponent(step.id)}/diff`} download />
              }
            >
              <DownloadIcon />
              {t('step.diff.download')}
            </Button>
          </CardAction>
        )}
      </CardHeader>
      <CardContent className="flex flex-col gap-3">
        {(step.toAdd > 0 || step.toChange > 0 || step.toDelete > 0 || step.bytes > 0) && (
          <div className="flex flex-wrap gap-2">
            {step.toDelete > 0 && (
              <Stat danger>
                {removes
                  ? t('step.remove', { count: step.toDelete })
                  : t('step.delete.size', { count: step.toDelete, size: formatBytes(step.bytesToDelete) })}
              </Stat>
            )}
            {step.toAdd > 0 && (
              <Stat>
                {t(step.kind === 'FileSync' ? 'step.copy.size' : 'step.new.size', {
                  count: step.toAdd,
                  size: formatBytes(step.bytes),
                })}
              </Stat>
            )}
            {step.toChange > 0 && <Stat>{t('step.changed', { count: step.toChange })}</Stat>}
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
          <div className="flex flex-col gap-2">
            <ScrollArea className="max-h-72 rounded-md border bg-muted/30">
              <div className="max-h-72 py-1">
                <Lines lines={step.lines} />
                {step.linesTruncated && (
                  <p className="px-3 py-1 text-xs text-muted-foreground">
                    {t('step.diff.hint', { count: step.lines.length })}
                  </p>
                )}
              </div>
            </ScrollArea>
          </div>
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
        <StepCard key={step.id} runId={run.id} step={step} target={label(step.targetObjectId)} />
      ))}
    </div>
  )
}
