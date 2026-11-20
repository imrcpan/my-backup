import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import {
  CloudIcon,
  HardDriveIcon,
  LaptopIcon,
  LogInIcon,
  MoreHorizontalIcon,
  PencilIcon,
  PlusIcon,
  Trash2Icon,
} from 'lucide-react'
import { toast } from 'sonner'
import { DeviceDialog } from '@/components/device-dialog'
import { DeviceStatusBadge } from '@/components/device-status'
import { Page } from '@/components/page'
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
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent } from '@/components/ui/card'
import { DropdownMenu, DropdownMenuContent, DropdownMenuItem, DropdownMenuTrigger } from '@/components/ui/dropdown-menu'
import { Skeleton } from '@/components/ui/skeleton'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'
import { api } from '@/lib/api'
import { useI18n } from '@/lib/i18n'
import { useNotify } from '@/lib/notify'
import type { Device, DeviceKind } from '@/lib/types'

const kindIcon: Record<DeviceKind, typeof LaptopIcon> = {
  Local: LaptopIcon,
  ExternalDisk: HardDriveIcon,
  Rclone: CloudIcon,
}

export function DevicesPage() {
  const { t } = useI18n()
  const notify = useNotify()
  const queryClient = useQueryClient()
  const devices = useQuery({ queryKey: ['devices'], queryFn: api.devices.list })
  const system = useQuery({ queryKey: ['system'], queryFn: api.system })
  const [editing, setEditing] = useState<Device | undefined>()
  const [dialogOpen, setDialogOpen] = useState(false)
  const [removing, setRemoving] = useState<Device | undefined>()
  const [signingIn, setSigningIn] = useState<string | undefined>()

  const remove = useMutation({
    mutationFn: (device: Device) => api.devices.remove(device.id),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ['devices'] })
      notify.success('devices.removed')
    },
    onError: notify.error,
    onSettled: () => setRemoving(undefined),
  })

  const signIn = async (device: Device) => {
    setSigningIn(device.id)
    const waiting = toast.loading(t('devices.signin.running'))
    try {
      await api.devices.authorize(device.id)
      await queryClient.invalidateQueries({ queryKey: ['device-status', device.id] })
      toast.success(t('devices.signin.done'))
    } catch (error) {
      notify.error(error)
    } finally {
      toast.dismiss(waiting)
      setSigningIn(undefined)
    }
  }

  const open = (device?: Device) => {
    setEditing(device)
    setDialogOpen(true)
  }

  const missing = [
    system.data && !system.data.restic.available ? 'restic' : null,
    system.data && !system.data.rclone.available ? 'rclone' : null,
  ].filter((tool): tool is string => tool != null)

  return (
    <Page
      title={t('page.devices.title')}
      description={t('page.devices.description')}
      actions={
        <Button size="sm" onClick={() => open()}>
          <PlusIcon />
          {t('devices.add')}
        </Button>
      }
    >
      <div className="flex flex-col gap-4">
        {missing.map((tool) => (
          <Alert key={tool} variant="destructive">
            <AlertDescription>{t('devices.tool.missing', { tool })}</AlertDescription>
          </Alert>
        ))}
        <Card>
          <CardContent className="px-0">
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead className="pl-4">{t('devices.col.device')}</TableHead>
                  <TableHead>{t('devices.col.kind')}</TableHead>
                  <TableHead className="hidden md:table-cell">{t('devices.col.details')}</TableHead>
                  <TableHead>{t('devices.col.status')}</TableHead>
                  <TableHead className="w-12 pr-4 text-right">
                    <span className="sr-only">{t('common.actions')}</span>
                  </TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {devices.isPending &&
                  [0, 1].map((row) => (
                    <TableRow key={row}>
                      <TableCell colSpan={5} className="px-4">
                        <Skeleton className="h-6 w-full" />
                      </TableCell>
                    </TableRow>
                  ))}
                {devices.isError && (
                  <TableRow>
                    <TableCell colSpan={5} className="px-4 text-destructive">
                      {notify.describe(devices.error)}
                    </TableCell>
                  </TableRow>
                )}
                {devices.data?.map((device) => {
                  const Icon = kindIcon[device.kind]
                  return (
                    <TableRow key={device.id}>
                      <TableCell className="pl-4">
                        <div className="flex items-center gap-3">
                          <Icon className="size-4 shrink-0 text-muted-foreground" />
                          <div className="flex min-w-0 flex-col">
                            <span className="truncate font-medium">{device.name}</span>
                            <span className="truncate font-mono text-xs text-muted-foreground">{device.id}</span>
                          </div>
                          {device.isBuiltIn && <Badge variant="secondary">{t('devices.builtin')}</Badge>}
                        </div>
                      </TableCell>
                      <TableCell>{t(`devices.kind.${device.kind}`)}</TableCell>
                      <TableCell className="hidden font-mono text-xs text-muted-foreground md:table-cell">
                        {device.uuid ?? (device.rcloneRemote ? `${device.rcloneRemote}:` : '')}
                      </TableCell>
                      <TableCell>
                        {device.kind === 'Local' ? (
                          <DeviceStatusBadge device={device} />
                        ) : (
                          <DeviceStatusBadge device={device} withRefresh />
                        )}
                      </TableCell>
                      <TableCell className="pr-4 text-right">
                        <DropdownMenu>
                          <DropdownMenuTrigger
                            render={<Button variant="ghost" size="icon-sm" aria-label={t('common.actions')} />}
                          >
                            <MoreHorizontalIcon />
                          </DropdownMenuTrigger>
                          <DropdownMenuContent align="end" className="w-auto min-w-40">
                            <DropdownMenuItem onClick={() => open(device)}>
                              <PencilIcon />
                              {t('common.edit')}
                            </DropdownMenuItem>
                            {device.kind === 'Rclone' && (
                              <DropdownMenuItem disabled={signingIn != null} onClick={() => void signIn(device)}>
                                <LogInIcon />
                                {t('devices.signin')}
                              </DropdownMenuItem>
                            )}
                            <DropdownMenuItem
                              variant="destructive"
                              disabled={device.isBuiltIn}
                              onClick={() => setRemoving(device)}
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
          </CardContent>
        </Card>
      </div>

      <DeviceDialog open={dialogOpen} onOpenChange={setDialogOpen} device={editing} />

      <AlertDialog open={removing != null} onOpenChange={(next) => !next && setRemoving(undefined)}>
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>{t('devices.delete.title')}</AlertDialogTitle>
            <AlertDialogDescription>{t('devices.delete.body')}</AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel>{t('common.cancel')}</AlertDialogCancel>
            <AlertDialogAction variant="destructive" onClick={() => removing && remove.mutate(removing)}>
              {t('devices.delete.confirm')}
            </AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>
    </Page>
  )
}
