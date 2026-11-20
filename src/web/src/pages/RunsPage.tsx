import { useQuery } from '@tanstack/react-query'
import { HistoryIcon } from 'lucide-react'
import { useNavigate } from 'react-router-dom'
import { Page } from '@/components/page'
import { RunStateBadge } from '@/components/state-badge'
import { Card, CardContent } from '@/components/ui/card'
import { Empty, EmptyDescription, EmptyHeader, EmptyMedia, EmptyTitle } from '@/components/ui/empty'
import { Skeleton } from '@/components/ui/skeleton'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'
import { api } from '@/lib/api'
import { formatTime } from '@/lib/format'
import { useI18n, type MessageKey } from '@/lib/i18n'
import { useNotify } from '@/lib/notify'

/** Every plan and run, newest first. A row opens the run. */
export function RunsPage() {
  const { t, locale } = useI18n()
  const notify = useNotify()
  const navigate = useNavigate()
  const runs = useQuery({
    queryKey: ['runs'],
    queryFn: api.runs.list,
    refetchInterval: (query) => (query.state.data?.some((run) => run.state === 'Running') ? 2000 : false),
  })

  return (
    <Page title={t('page.runs.title')} description={t('page.runs.description')}>
      {runs.isPending && <Skeleton className="h-40 w-full" />}
      {runs.isError && <p className="text-sm text-destructive">{notify.describe(runs.error)}</p>}
      {runs.data?.length === 0 && (
        <Empty className="border">
          <EmptyHeader>
            <EmptyMedia variant="icon">
              <HistoryIcon />
            </EmptyMedia>
            <EmptyTitle>{t('runs.empty')}</EmptyTitle>
            <EmptyDescription>{t('page.runs.description')}</EmptyDescription>
          </EmptyHeader>
        </Empty>
      )}
      {runs.data && runs.data.length > 0 && (
        <Card>
          <CardContent className="px-0">
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead className="pl-4">{t('runs.col.time')}</TableHead>
                  <TableHead>{t('runs.col.kind')}</TableHead>
                  <TableHead>{t('runs.col.group')}</TableHead>
                  <TableHead>{t('runs.col.state')}</TableHead>
                  <TableHead className="hidden pr-4 text-right md:table-cell">{t('runs.col.steps')}</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {runs.data.map((run) => (
                  <TableRow key={run.id} className="cursor-pointer" onClick={() => void navigate(`/runs/${run.id}`)}>
                    <TableCell className="pl-4">{formatTime(run.createdAt, locale)}</TableCell>
                    <TableCell>{t(`run.kind.${run.kind}` as MessageKey)}</TableCell>
                    <TableCell>{run.groupName}</TableCell>
                    <TableCell>
                      <RunStateBadge state={run.state} />
                    </TableCell>
                    <TableCell className="hidden pr-4 text-right text-muted-foreground md:table-cell">
                      {run.steps.filter((step) => step.state === 'Succeeded' || step.state === 'UpToDate').length} /{' '}
                      {run.steps.length}
                    </TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </CardContent>
        </Card>
      )}
    </Page>
  )
}
