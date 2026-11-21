import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import {
  ArchiveRestoreIcon,
  CalendarClockIcon,
  FolderOpenIcon,
  KeyRoundIcon,
  LayersIcon,
  LockIcon,
  LockOpenIcon,
  MoreHorizontalIcon,
  PencilIcon,
  PlayIcon,
  PlusIcon,
  Trash2Icon,
} from 'lucide-react'
import { useNavigate } from 'react-router-dom'
import { DeviceStatusBadge } from '@/components/device-status'
import { NameDialog } from '@/components/name-dialog'
import { NewRunDialog } from '@/components/new-run-dialog'
import { ObjectDialog } from '@/components/object-dialog'
import { Page } from '@/components/page'
import { RetentionDialog } from '@/components/retention-dialog'
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
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardAction, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from '@/components/ui/dropdown-menu'
import { Empty, EmptyContent, EmptyDescription, EmptyHeader, EmptyMedia, EmptyTitle } from '@/components/ui/empty'
import { Skeleton } from '@/components/ui/skeleton'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'
import { api } from '@/lib/api'
import { describeRetention } from '@/lib/format'
import { useI18n } from '@/lib/i18n'
import { useNotify } from '@/lib/notify'
import type { Device, Group, StorageObject } from '@/lib/types'

type ObjectTarget = { group: Group; object?: StorageObject }

export function GroupsPage() {
  const { t } = useI18n()
  const notify = useNotify()
  const queryClient = useQueryClient()
  const navigate = useNavigate()
  const groups = useQuery({ queryKey: ['groups'], queryFn: api.groups.list })
  const devices = useQuery({ queryKey: ['devices'], queryFn: api.devices.list })

  const [creating, setCreating] = useState(false)
  const [renaming, setRenaming] = useState<Group | undefined>()
  const [removingGroup, setRemovingGroup] = useState<Group | undefined>()
  const [objectTarget, setObjectTarget] = useState<ObjectTarget | undefined>()
  const [removingObject, setRemovingObject] = useState<ObjectTarget | undefined>()
  const [retentionTarget, setRetentionTarget] = useState<ObjectTarget | undefined>()
  const [runGroup, setRunGroup] = useState<Group | undefined>()

  const refresh = () => queryClient.invalidateQueries({ queryKey: ['groups'] })

  const create = useMutation({
    mutationFn: (name: string) => api.groups.create(name),
    onSuccess: async () => {
      await refresh()
      notify.success('groups.saved')
      setCreating(false)
    },
    onError: notify.error,
  })

  const rename = useMutation({
    mutationFn: ({ group, name }: { group: Group; name: string }) => api.groups.rename(group.id, name),
    onSuccess: async () => {
      await refresh()
      notify.success('groups.saved')
      setRenaming(undefined)
    },
    onError: notify.error,
  })

  const removeGroup = useMutation({
    mutationFn: (group: Group) => api.groups.remove(group.id),
    onSuccess: async () => {
      await refresh()
      notify.success('groups.removed')
    },
    onError: notify.error,
    onSettled: () => setRemovingGroup(undefined),
  })

  const removeObject = useMutation({
    mutationFn: ({ group, object }: { group: Group; object: StorageObject }) =>
      api.groups.removeObject(group.id, object.id),
    onSuccess: async () => {
      await refresh()
      notify.success('object.removed')
    },
    onError: notify.error,
    onSettled: () => setRemovingObject(undefined),
  })

  const deviceList: Device[] = devices.data ?? []

  return (
    <Page
      title={t('page.groups.title')}
      description={t('page.groups.description')}
      actions={
        <Button size="sm" onClick={() => setCreating(true)}>
          <PlusIcon />
          {t('groups.new')}
        </Button>
      }
    >
      <div className="flex flex-col gap-4">
        {groups.isPending && <Skeleton className="h-40 w-full" />}
        {groups.isError && <p className="text-sm text-destructive">{notify.describe(groups.error)}</p>}
        {groups.data?.length === 0 && (
          <Empty className="border">
            <EmptyHeader>
              <EmptyMedia variant="icon">
                <LayersIcon />
              </EmptyMedia>
              <EmptyTitle>{t('groups.empty.title')}</EmptyTitle>
              <EmptyDescription>{t('groups.empty.body')}</EmptyDescription>
            </EmptyHeader>
            <EmptyContent>
              <Button onClick={() => setCreating(true)}>
                <PlusIcon />
                {t('groups.new')}
              </Button>
            </EmptyContent>
          </Empty>
        )}
        {groups.data?.map((group) => (
          <Card key={group.id}>
            <CardHeader>
              <CardTitle>{group.name}</CardTitle>
              <CardDescription>{t('groups.objects', { count: group.objects.length })}</CardDescription>
              <CardAction className="flex items-center gap-2">
                <Button
                  size="sm"
                  disabled={group.objects.length < 2}
                  title={group.objects.length < 2 ? t('groups.newrun.needTwo') : undefined}
                  onClick={() => setRunGroup(group)}
                >
                  <PlayIcon />
                  {t('groups.newrun')}
                </Button>
                <Button size="sm" variant="outline" onClick={() => setObjectTarget({ group })}>
                  <PlusIcon />
                  {t('groups.object.add')}
                </Button>
                <DropdownMenu>
                  <DropdownMenuTrigger
                    render={<Button variant="ghost" size="icon-sm" aria-label={t('common.actions')} />}
                  >
                    <MoreHorizontalIcon />
                  </DropdownMenuTrigger>
                  <DropdownMenuContent align="end" className="w-auto min-w-40">
                    <DropdownMenuItem onClick={() => setRenaming(group)}>
                      <PencilIcon />
                      {t('groups.rename')}
                    </DropdownMenuItem>
                    <DropdownMenuSeparator />
                    <DropdownMenuItem variant="destructive" onClick={() => setRemovingGroup(group)}>
                      <Trash2Icon />
                      {t('common.delete')}
                    </DropdownMenuItem>
                  </DropdownMenuContent>
                </DropdownMenu>
              </CardAction>
            </CardHeader>
            <CardContent className="px-0">
              {group.objects.length === 0 ? (
                <p className="px-4 pb-2 text-sm text-muted-foreground">{t('groups.object.none')}</p>
              ) : (
                <Table>
                  <TableHeader>
                    <TableRow>
                      <TableHead className="pl-4">{t('object.col.location')}</TableHead>
                      <TableHead>{t('object.col.encryption')}</TableHead>
                      <TableHead className="hidden md:table-cell">{t('object.col.password')}</TableHead>
                      <TableHead className="hidden lg:table-cell">{t('object.col.retention')}</TableHead>
                      <TableHead className="w-12 pr-4 text-right">
                        <span className="sr-only">{t('common.actions')}</span>
                      </TableHead>
                    </TableRow>
                  </TableHeader>
                  <TableBody>
                    {group.objects.map((object) => {
                      const device = deviceList.find((item) => item.id === object.deviceId)
                      const retention = describeRetention(object.retention, t)
                      return (
                        <TableRow key={object.id}>
                          <TableCell className="pl-4">
                            <div className="flex flex-col gap-0.5">
                              <span className="flex items-center gap-2 font-medium">
                                {device?.name ?? object.deviceId}
                                {device?.kind === 'ExternalDisk' && <DeviceStatusBadge device={device} />}
                              </span>
                              <span className="font-mono text-xs break-all text-muted-foreground">{object.path}</span>
                            </div>
                          </TableCell>
                          <TableCell>
                            {object.encrypted ? (
                              <Badge variant="secondary">
                                <LockIcon />
                                {t('object.encrypted')}
                              </Badge>
                            ) : (
                              <Badge variant="outline">
                                <LockOpenIcon />
                                {t('object.plain')}
                              </Badge>
                            )}
                          </TableCell>
                          <TableCell className="hidden md:table-cell">
                            {object.hasPassword ? (
                              <span className="inline-flex items-center gap-1 text-sm text-muted-foreground">
                                <KeyRoundIcon className="size-3.5" />
                                {t('object.password.saved')}
                              </span>
                            ) : (
                              <span className="text-muted-foreground">{t('object.password.none')}</span>
                            )}
                          </TableCell>
                          <TableCell className="hidden text-sm text-muted-foreground lg:table-cell">
                            {object.encrypted ? retention || t('retention.none') : t('object.password.none')}
                          </TableCell>
                          <TableCell className="pr-4 text-right">
                            <DropdownMenu>
                              <DropdownMenuTrigger
                                render={<Button variant="ghost" size="icon-sm" aria-label={t('common.actions')} />}
                              >
                                <MoreHorizontalIcon />
                              </DropdownMenuTrigger>
                              <DropdownMenuContent align="end" className="w-auto min-w-44">
                                <DropdownMenuItem onClick={() => setObjectTarget({ group, object })}>
                                  <PencilIcon />
                                  {t('common.edit')}
                                </DropdownMenuItem>
                                <DropdownMenuItem
                                  onClick={() =>
                                    void navigate(
                                      object.encrypted
                                        ? `/groups/${group.id}/objects/${object.id}?latest=1`
                                        : `/groups/${group.id}/objects/${object.id}/files`,
                                    )
                                  }
                                >
                                  <FolderOpenIcon />
                                  {t('files.view')}
                                </DropdownMenuItem>
                                {object.encrypted && (
                                  <DropdownMenuItem
                                    onClick={() => void navigate(`/groups/${group.id}/objects/${object.id}`)}
                                  >
                                    <ArchiveRestoreIcon />
                                    {t('groups.menu.snapshots')}
                                  </DropdownMenuItem>
                                )}
                                {object.encrypted && (
                                  <DropdownMenuItem onClick={() => setRetentionTarget({ group, object })}>
                                    <CalendarClockIcon />
                                    {t('object.menu.retention')}
                                  </DropdownMenuItem>
                                )}
                                <DropdownMenuSeparator />
                                <DropdownMenuItem
                                  variant="destructive"
                                  onClick={() => setRemovingObject({ group, object })}
                                >
                                  <Trash2Icon />
                                  {t('common.delete')}
                                </DropdownMenuItem>
                              </DropdownMenuContent>
                            </DropdownMenu>
                          </TableCell>
                        </TableRow>
                      )
                    })}
                  </TableBody>
                </Table>
              )}
            </CardContent>
          </Card>
        ))}
      </div>

      <NameDialog
        open={creating}
        onOpenChange={setCreating}
        title={t('groups.new.title')}
        description={t('groups.new.description')}
        pending={create.isPending}
        onSubmit={(name) => create.mutate(name)}
      />
      <NameDialog
        open={renaming != null}
        onOpenChange={(next) => !next && setRenaming(undefined)}
        title={t('groups.rename.title')}
        initial={renaming?.name}
        pending={rename.isPending}
        onSubmit={(name) => renaming && rename.mutate({ group: renaming, name })}
      />
      <ObjectDialog
        open={objectTarget != null}
        onOpenChange={(next) => !next && setObjectTarget(undefined)}
        groupId={objectTarget?.group.id ?? ''}
        devices={deviceList}
        object={objectTarget?.object}
      />
      <NewRunDialog
        open={runGroup != null}
        onOpenChange={(next) => !next && setRunGroup(undefined)}
        group={runGroup}
        devices={deviceList}
      />
      <RetentionDialog
        open={retentionTarget != null}
        onOpenChange={(next) => !next && setRetentionTarget(undefined)}
        groupId={retentionTarget?.group.id ?? ''}
        object={retentionTarget?.object}
      />

      <AlertDialog open={removingGroup != null} onOpenChange={(next) => !next && setRemovingGroup(undefined)}>
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>{t('groups.delete.title')}</AlertDialogTitle>
            <AlertDialogDescription>{t('groups.delete.body')}</AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel>{t('common.cancel')}</AlertDialogCancel>
            <AlertDialogAction variant="destructive" onClick={() => removingGroup && removeGroup.mutate(removingGroup)}>
              {t('groups.delete.confirm')}
            </AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>

      <AlertDialog open={removingObject != null} onOpenChange={(next) => !next && setRemovingObject(undefined)}>
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>{t('object.delete.title')}</AlertDialogTitle>
            <AlertDialogDescription>{t('object.delete.body')}</AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel>{t('common.cancel')}</AlertDialogCancel>
            <AlertDialogAction
              variant="destructive"
              onClick={() =>
                removingObject?.object &&
                removeObject.mutate({ group: removingObject.group, object: removingObject.object })
              }
            >
              {t('object.delete.confirm')}
            </AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>
    </Page>
  )
}
