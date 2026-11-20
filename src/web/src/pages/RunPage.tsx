import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { AlertTriangleIcon, ArrowLeftIcon, PlayIcon, RefreshCwIcon, SquareIcon, Trash2Icon } from 'lucide-react'
import { useNavigate, useParams } from 'react-router-dom'
import { Page } from '@/components/page'
import { RunPlan } from '@/components/run-plan'
import { RunStateBadge } from '@/components/state-badge'
import { Alert, AlertDescription } from '@/components/ui/alert'
import {
  AlertDialog,
  AlertDialogAction,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogTitle,
} from '@/components/ui/alert-dialog'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Checkbox } from '@/components/ui/checkbox'
import { Field, FieldLabel } from '@/components/ui/field'
import { ScrollArea } from '@/components/ui/scroll-area'
import { Skeleton } from '@/components/ui/skeleton'
import { api } from '@/lib/api'
import { formatTime } from '@/lib/format'
import { useI18n, type MessageKey } from '@/lib/i18n'
import { useObjectLabels } from '@/lib/labels'
import { useNotify } from '@/lib/notify'

/**
 * One run, at every stage: the plan that waits for a confirmation, the progress of the run
 * with a way to cancel it, and the result with its log.
 */
export function RunPage() {
  const { id = '' } = useParams()
  const { t, locale } = useI18n()
  const notify = useNotify()
  const navigate = useNavigate()
  const queryClient = useQueryClient()
  const labelOf = useObjectLabels()
  const [confirming, setConfirming] = useState(false)
  const [acknowledged, setAcknowledged] = useState(false)
  const [cancelling, setCancelling] = useState(false)

  const run = useQuery({
    queryKey: ['run', id],
    queryFn: () => api.runs.get(id),
    refetchInterval: (query) => (query.state.data?.state === 'Running' ? 1000 : false),
  })
  const log = useQuery({
    queryKey: ['run-log', id],
    queryFn: () => api.runs.log(id),
    refetchInterval: run.data?.state === 'Running' ? 2000 : false,
    enabled: run.data != null && run.data.state !== 'Planned' && run.data.state !== 'Discarded',
  })

  const refresh = async () => {
    await queryClient.invalidateQueries({ queryKey: ['run', id] })
    await queryClient.invalidateQueries({ queryKey: ['run-log', id] })
    await queryClient.invalidateQueries({ queryKey: ['runs'] })
  }

  const start = useMutation({
    mutationFn: () => api.runs.start(id, acknowledged),
    onSuccess: async () => {
      notify.success('run.started')
      await refresh()
    },
    onError: notify.error,
    onSettled: () => setConfirming(false),
  })

  const discard = useMutation({
    mutationFn: () => api.runs.discard(id),
    onSuccess: async () => {
      notify.success('run.discarded')
      await queryClient.invalidateQueries({ queryKey: ['runs'] })
      void navigate('/groups')
    },
    onError: notify.error,
  })

  const cancel = useMutation({
    mutationFn: () => api.runs.cancel(id),
    onSuccess: refresh,
    onError: notify.error,
    onSettled: () => setCancelling(false),
  })

  // A sync can be planned again with the same choice, once what blocked it is fixed.
  const planAgain = useMutation({
    mutationFn: async () => {
      const old = run.data!
      const fresh = await api.runs.planSync(
        old.groupId,
        old.sourceObjectId,
        old.steps.map((step) => step.targetObjectId),
      )
      await api.runs.discard(old.id).catch(() => undefined)
      return fresh
    },
    onSuccess: async (fresh) => {
      await queryClient.invalidateQueries({ queryKey: ['runs'] })
      void navigate(`/runs/${fresh.id}`, { replace: true })
    },
    onError: notify.error,
  })

  const data = run.data
  const blocked = data?.steps.some((step) => step.state === 'Blocked') ?? false
  const label = (objectId: string) => labelOf(data?.groupId ?? '', objectId)

  return (
    <Page
      title={data ? `${t(`run.kind.${data.kind}` as MessageKey)} · ${data.groupName}` : t('nav.runs')}
      description={t('page.runs.description')}
      actions={
        <Button size="sm" variant="outline" onClick={() => void navigate('/runs')}>
          <ArrowLeftIcon />
          {t('run.back')}
        </Button>
      }
    >
      {run.isPending && <Skeleton className="h-40 w-full" />}
      {run.isError && <p className="text-sm text-destructive">{notify.describe(run.error)}</p>}
      {data && (
        <div className="mx-auto flex max-w-4xl flex-col gap-4">
          <Card size="sm">
            <CardContent className="flex flex-wrap items-center gap-x-6 gap-y-2 text-sm">
              <RunStateBadge state={data.state} />
              <span className="text-muted-foreground">
                {t('run.created')}: {formatTime(data.createdAt, locale)}
              </span>
              {data.finishedAt && (
                <span className="text-muted-foreground">
                  {t('run.finished')}: {formatTime(data.finishedAt, locale)}
                </span>
              )}
              {data.sourceObjectId && (
                <span className="min-w-0 break-all text-muted-foreground">
                  {t('run.source')}: <span className="font-mono">{label(data.sourceObjectId)}</span>
                </span>
              )}
            </CardContent>
          </Card>

          {data.errorMessage && (
            <Alert variant="destructive">
              <AlertTriangleIcon />
              <AlertDescription>{data.errorMessage}</AlertDescription>
            </Alert>
          )}

          {data.state === 'Planned' && (
            <>
              <Alert variant={blocked || data.destructive ? 'destructive' : 'default'}>
                {data.destructive && <AlertTriangleIcon />}
                <AlertDescription>
                  {blocked
                    ? t('run.plan.blocked')
                    : data.destructive
                      ? t('run.start.destructive')
                      : t('run.plan.expires')}
                </AlertDescription>
              </Alert>
              <div className="flex flex-wrap gap-2">
                <Button
                  disabled={blocked || start.isPending}
                  onClick={() => {
                    setAcknowledged(false)
                    setConfirming(true)
                  }}
                >
                  <PlayIcon />
                  {t('run.start')}
                </Button>
                {data.kind === 'Sync' && (
                  <Button variant="outline" disabled={planAgain.isPending} onClick={() => planAgain.mutate()}>
                    <RefreshCwIcon />
                    {t('run.plan.again')}
                  </Button>
                )}
                <Button variant="ghost" disabled={discard.isPending} onClick={() => discard.mutate()}>
                  <Trash2Icon />
                  {t('run.discard')}
                </Button>
              </div>
            </>
          )}

          {data.state === 'Running' && (
            <div>
              <Button variant="outline" onClick={() => setCancelling(true)}>
                <SquareIcon />
                {t('run.cancel')}
              </Button>
            </div>
          )}

          <RunPlan run={data} label={label} />

          {data.state !== 'Planned' && data.state !== 'Discarded' && (
            <Card size="sm">
              <CardHeader>
                <CardTitle>{t('run.log')}</CardTitle>
              </CardHeader>
              <CardContent>
                <ScrollArea className="max-h-80 rounded-md border bg-muted/30">
                  <div className="max-h-80 p-3 font-mono text-xs leading-relaxed break-all whitespace-pre-wrap">
                    {log.data?.lines.length ? log.data.lines.join('\n') : t('run.log.empty')}
                  </div>
                </ScrollArea>
              </CardContent>
            </Card>
          )}
        </div>
      )}

      <AlertDialog open={confirming} onOpenChange={setConfirming}>
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>{t('run.start.title')}</AlertDialogTitle>
            <AlertDialogDescription>{t('run.start.body')}</AlertDialogDescription>
          </AlertDialogHeader>
          {data?.destructive && (
            <div className="flex flex-col gap-3">
              <Alert variant="destructive">
                <AlertTriangleIcon />
                <AlertDescription>{t('run.start.destructive')}</AlertDescription>
              </Alert>
              <Field orientation="horizontal" className="items-center">
                <Checkbox
                  id="acknowledge"
                  checked={acknowledged}
                  onCheckedChange={(checked) => setAcknowledged(checked === true)}
                />
                <FieldLabel htmlFor="acknowledge" className="font-normal">
                  {t('run.start.acknowledge')}
                </FieldLabel>
              </Field>
            </div>
          )}
          <AlertDialogFooter>
            <AlertDialogCancel>{t('common.cancel')}</AlertDialogCancel>
            <AlertDialogAction
              variant={data?.destructive ? 'destructive' : 'default'}
              disabled={data?.destructive === true && !acknowledged}
              onClick={() => start.mutate()}
            >
              {t('run.start.confirm')}
            </AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>

      <AlertDialog open={cancelling} onOpenChange={setCancelling}>
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>{t('run.cancel.title')}</AlertDialogTitle>
            <AlertDialogDescription>{t('run.cancel.body')}</AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel>{t('run.cancel.keep')}</AlertDialogCancel>
            <AlertDialogAction variant="destructive" onClick={() => cancel.mutate()}>
              {t('run.cancel.confirm')}
            </AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>
    </Page>
  )
}
