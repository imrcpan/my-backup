import { useEffect, useState } from 'react'
import { useMutation, useQuery } from '@tanstack/react-query'
import {
  ArchiveRestoreIcon,
  ArrowLeftIcon,
  BroomIcon,
  CalendarClockIcon,
  FileIcon,
  FolderIcon,
  ShieldCheckIcon,
  Trash2Icon,
  XIcon,
} from 'lucide-react'
import { useNavigate, useParams, useSearchParams } from 'react-router-dom'
import { CheckDialog } from '@/components/check-dialog'
import { Page } from '@/components/page'
import { RestoreDialog } from '@/components/restore-dialog'
import { RetentionDialog } from '@/components/retention-dialog'
import { Button } from '@/components/ui/button'
import { Card, CardAction, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { Checkbox } from '@/components/ui/checkbox'
import { Field, FieldLabel } from '@/components/ui/field'
import { Skeleton } from '@/components/ui/skeleton'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'
import { api } from '@/lib/api'
import { describeRetention, formatBytes, formatTime } from '@/lib/format'
import { useI18n } from '@/lib/i18n'
import { useNotify } from '@/lib/notify'

/** The snapshots of one repository, and what can be done with them. Every change goes through a plan. */
export function RepositoryPage() {
  const { groupId = '', objectId = '' } = useParams()
  const { t, locale } = useI18n()
  const notify = useNotify()
  const navigate = useNavigate()
  const [params, setParams] = useSearchParams()
  const [selected, setSelected] = useState<string[]>([])
  const [pruneAfter, setPruneAfter] = useState(false)
  const [checking, setChecking] = useState(false)
  const [retention, setRetention] = useState(false)
  const [restorePaths, setRestorePaths] = useState<string[] | undefined>()
  const [picked, setPicked] = useState<string[]>([])

  const groups = useQuery({ queryKey: ['groups'], queryFn: api.groups.list })
  const devices = useQuery({ queryKey: ['devices'], queryFn: api.devices.list })
  const group = groups.data?.find((item) => item.id === groupId)
  const object = group?.objects.find((item) => item.id === objectId)
  const device = devices.data?.find((item) => item.id === object?.deviceId)

  const snapshots = useQuery({
    queryKey: ['snapshots', groupId, objectId],
    queryFn: () => api.groups.snapshots(groupId, objectId),
    enabled: object?.encrypted === true,
  })

  const snapshotId = params.get('snapshot') ?? ''
  const path = params.get('path') ?? '/'
  const snapshot = snapshots.data?.find((item) => item.id === snapshotId)
  const wantsLatest = params.get('latest') === '1'

  // "View files" lands here: open the newest snapshot so that the files are shown at once.
  useEffect(() => {
    if (!wantsLatest || !snapshots.data) return
    const newest = [...snapshots.data].sort((a, b) => b.time.localeCompare(a.time))[0]
    setParams(newest ? { snapshot: newest.id } : {}, { replace: true })
  }, [wantsLatest, snapshots.data, setParams])

  const files = useQuery({
    queryKey: ['files', groupId, objectId, snapshotId, path],
    queryFn: () => api.groups.files(groupId, objectId, snapshotId, path),
    enabled: snapshotId !== '',
  })

  const open = (next: { snapshot?: string; path?: string }) => {
    const query = new URLSearchParams()
    if (next.snapshot) query.set('snapshot', next.snapshot)
    if (next.path && next.path !== '/') query.set('path', next.path)
    setParams(query)
    setPicked([])
  }

  const planned = {
    onSuccess: (run: { id: string }) => void navigate(`/runs/${run.id}`),
    onError: notify.error,
  }
  const forgetSelected = useMutation({
    mutationFn: () => api.runs.planForget(groupId, objectId, selected, pruneAfter),
    ...planned,
  })
  const forgetByPolicy = useMutation({
    mutationFn: () => api.runs.planForget(groupId, objectId, undefined, pruneAfter),
    ...planned,
  })
  const prune = useMutation({ mutationFn: () => api.runs.planPrune(groupId, objectId), ...planned })

  const segments = path.split('/').filter(Boolean)
  const policy = describeRetention(object?.retention, t)
  const title = device && object ? `${device.name} · ${object.path}` : t('repo.title')

  return (
    <Page
      title={title}
      description={t('repo.description')}
      actions={
        <Button size="sm" variant="outline" onClick={() => void navigate('/groups')}>
          <ArrowLeftIcon />
          {t('repo.back')}
        </Button>
      }
    >
      {(groups.isPending || devices.isPending) && <Skeleton className="h-40 w-full" />}
      {object && (
        <div className="mx-auto flex max-w-5xl flex-col gap-4">
          <Card size="sm">
            <CardContent className="flex flex-col gap-3">
              <div className="flex flex-wrap items-center gap-2">
                <Button variant="outline" size="sm" onClick={() => setRetention(true)}>
                  <CalendarClockIcon />
                  {t('repo.retention')}
                </Button>
                <Button variant="outline" size="sm" onClick={() => setChecking(true)}>
                  <ShieldCheckIcon />
                  {t('repo.check')}
                </Button>
                <Button variant="outline" size="sm" disabled={prune.isPending} onClick={() => prune.mutate()}>
                  <BroomIcon />
                  {t('repo.prune')}
                </Button>
              </div>
              <div className="flex flex-wrap items-center gap-2">
                <Button
                  variant="destructive"
                  size="sm"
                  disabled={selected.length === 0 || forgetSelected.isPending}
                  onClick={() => forgetSelected.mutate()}
                >
                  <Trash2Icon />
                  {t('repo.remove.selected', { count: selected.length })}
                </Button>
                <Button
                  variant="destructive"
                  size="sm"
                  disabled={!object.retention || forgetByPolicy.isPending}
                  onClick={() => forgetByPolicy.mutate()}
                >
                  <Trash2Icon />
                  {t('repo.remove.policy')}
                  {policy && ` (${policy})`}
                </Button>
                <Field orientation="horizontal" className="w-auto items-center">
                  <Checkbox
                    id="prune-after"
                    checked={pruneAfter}
                    onCheckedChange={(checked) => setPruneAfter(checked === true)}
                  />
                  <FieldLabel htmlFor="prune-after" className="font-normal">
                    {t('repo.remove.prune')}
                  </FieldLabel>
                </Field>
              </div>
              {!object.retention && <p className="text-xs text-muted-foreground">{t('repo.policy.none')}</p>}
            </CardContent>
          </Card>

          <Card>
            <CardHeader>
              <CardTitle>{t('repo.title')}</CardTitle>
            </CardHeader>
            <CardContent className="px-0">
              {snapshots.isPending && <Skeleton className="mx-4 h-24" />}
              {snapshots.isError && <p className="px-4 text-sm text-destructive">{notify.describe(snapshots.error)}</p>}
              {snapshots.data?.length === 0 && <p className="px-4 text-sm text-muted-foreground">{t('repo.empty')}</p>}
              {snapshots.data && snapshots.data.length > 0 && (
                <Table>
                  <TableHeader>
                    <TableRow>
                      <TableHead className="w-10 pl-4">
                        <span className="sr-only">{t('repo.select')}</span>
                      </TableHead>
                      <TableHead>{t('repo.col.time')}</TableHead>
                      <TableHead className="hidden md:table-cell">{t('repo.col.paths')}</TableHead>
                      <TableHead className="hidden lg:table-cell">{t('repo.col.files')}</TableHead>
                      <TableHead className="hidden lg:table-cell">{t('repo.col.size')}</TableHead>
                      <TableHead className="pr-4 text-right">
                        <span className="sr-only">{t('common.actions')}</span>
                      </TableHead>
                    </TableRow>
                  </TableHeader>
                  <TableBody>
                    {snapshots.data.map((item) => (
                      <TableRow key={item.id} data-state={item.id === snapshotId ? 'selected' : undefined}>
                        <TableCell className="pl-4">
                          <Checkbox
                            aria-label={t('repo.select')}
                            checked={selected.includes(item.id)}
                            onCheckedChange={(checked) =>
                              setSelected(checked ? [...selected, item.id] : selected.filter((id) => id !== item.id))
                            }
                          />
                        </TableCell>
                        <TableCell>
                          <div className="flex flex-col">
                            <span>{formatTime(item.time, locale)}</span>
                            <span className="font-mono text-xs text-muted-foreground">{item.shortId}</span>
                          </div>
                        </TableCell>
                        <TableCell className="hidden font-mono text-xs break-all text-muted-foreground md:table-cell">
                          {item.paths.join(', ')}
                        </TableCell>
                        <TableCell className="hidden lg:table-cell">{item.files ?? '—'}</TableCell>
                        <TableCell className="hidden lg:table-cell">{formatBytes(item.bytes)}</TableCell>
                        <TableCell className="pr-4 text-right">
                          <Button size="sm" variant="outline" onClick={() => open({ snapshot: item.id })}>
                            <ArchiveRestoreIcon />
                            {t('repo.browse')}
                          </Button>
                        </TableCell>
                      </TableRow>
                    ))}
                  </TableBody>
                </Table>
              )}
            </CardContent>
          </Card>

          {snapshotId !== '' && (
            <Card>
              <CardHeader>
                <CardTitle>
                  {t('files.title')}{' '}
                  <span className="font-mono text-sm text-muted-foreground">{snapshot?.shortId}</span>
                </CardTitle>
                <CardDescription className="flex flex-wrap items-center gap-1 font-mono">
                  <Button variant="ghost" size="xs" onClick={() => open({ snapshot: snapshotId })}>
                    {t('files.root')}
                  </Button>
                  {segments.map((segment, index) => (
                    <span key={segment + index} className="flex items-center gap-1">
                      /
                      <Button
                        variant="ghost"
                        size="xs"
                        onClick={() =>
                          open({ snapshot: snapshotId, path: '/' + segments.slice(0, index + 1).join('/') })
                        }
                      >
                        {segment}
                      </Button>
                    </span>
                  ))}
                </CardDescription>
                <CardAction className="flex flex-wrap items-center gap-2">
                  <Button size="sm" disabled={picked.length === 0} onClick={() => setRestorePaths(picked)}>
                    <ArchiveRestoreIcon />
                    {t('files.restore.selected', { count: picked.length })}
                  </Button>
                  <Button size="sm" variant="outline" disabled={path === '/'} onClick={() => setRestorePaths([path])}>
                    {t('files.restore.folder')}
                  </Button>
                  <Button size="icon-sm" variant="ghost" aria-label={t('files.close')} onClick={() => open({})}>
                    <XIcon />
                  </Button>
                </CardAction>
              </CardHeader>
              <CardContent className="px-0">
                {files.isPending && <Skeleton className="mx-4 h-24" />}
                {files.isError && <p className="px-4 text-sm text-destructive">{notify.describe(files.error)}</p>}
                {files.data?.length === 0 && <p className="px-4 text-sm text-muted-foreground">{t('files.empty')}</p>}
                {files.data && files.data.length > 0 && (
                  <Table>
                    <TableHeader>
                      <TableRow>
                        <TableHead className="w-10 pl-4">
                          <span className="sr-only">{t('files.select')}</span>
                        </TableHead>
                        <TableHead>{t('files.col.name')}</TableHead>
                        <TableHead className="hidden sm:table-cell">{t('files.col.size')}</TableHead>
                        <TableHead className="hidden pr-4 md:table-cell">{t('files.col.modified')}</TableHead>
                      </TableRow>
                    </TableHeader>
                    <TableBody>
                      {files.data.map((node) => (
                        <TableRow
                          key={node.path}
                          className={node.type === 'dir' ? 'cursor-pointer' : undefined}
                          onClick={
                            node.type === 'dir' ? () => open({ snapshot: snapshotId, path: node.path }) : undefined
                          }
                        >
                          {/* Choosing a file is not opening a folder. */}
                          <TableCell className="pl-4" onClick={(event) => event.stopPropagation()}>
                            <Checkbox
                              aria-label={t('files.select')}
                              checked={picked.includes(node.path)}
                              onCheckedChange={(checked) =>
                                setPicked(
                                  checked ? [...picked, node.path] : picked.filter((item) => item !== node.path),
                                )
                              }
                            />
                          </TableCell>
                          <TableCell>
                            {node.type === 'dir' ? (
                              <Button variant="ghost" size="sm" className="cursor-pointer">
                                <FolderIcon />
                                {node.name}
                              </Button>
                            ) : (
                              <span className="flex items-center gap-1.5 px-2.5 text-sm">
                                <FileIcon className="size-3.5 text-muted-foreground" />
                                {node.name}
                              </span>
                            )}
                          </TableCell>
                          <TableCell className="hidden text-muted-foreground sm:table-cell">
                            {node.type === 'dir' ? '' : formatBytes(node.size)}
                          </TableCell>
                          <TableCell className="hidden pr-4 text-muted-foreground md:table-cell">
                            {formatTime(node.mtime, locale)}
                          </TableCell>
                        </TableRow>
                      ))}
                    </TableBody>
                  </Table>
                )}
              </CardContent>
            </Card>
          )}
        </div>
      )}

      <RestoreDialog
        open={restorePaths != null}
        onOpenChange={(next) => !next && setRestorePaths(undefined)}
        groupId={groupId}
        objectId={objectId}
        snapshotId={snapshotId}
        paths={restorePaths ?? []}
      />
      <CheckDialog open={checking} onOpenChange={setChecking} groupId={groupId} objectId={objectId} />
      <RetentionDialog open={retention} onOpenChange={setRetention} groupId={groupId} object={object} />
    </Page>
  )
}
