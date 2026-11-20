import { RefreshCwIcon } from 'lucide-react'
import { useQuery } from '@tanstack/react-query'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { useNotify } from '@/lib/notify'
import { api } from '@/lib/api'
import { useI18n, type MessageKey } from '@/lib/i18n'
import type { Device, DeviceStatus } from '@/lib/types'
import { cn } from '@/lib/utils'

const tone: Record<DeviceStatus['state'], string> = {
  ok: 'border-emerald-500/40 text-emerald-600 dark:text-emerald-400',
  mounted: 'border-emerald-500/40 text-emerald-600 dark:text-emerald-400',
  not_mounted: 'border-amber-500/40 text-amber-600 dark:text-amber-400',
  not_configured: 'border-amber-500/40 text-amber-600 dark:text-amber-400',
  not_connected: 'border-red-500/40 text-red-600 dark:text-red-400',
  unreachable: 'border-red-500/40 text-red-600 dark:text-red-400',
}

/** Whether a device can be used right now. A disk is looked at again every few seconds; a cloud drive only when asked. */
export function useDeviceStatus(device: Device) {
  return useQuery({
    queryKey: ['device-status', device.id, device.uuid, device.rcloneRemote],
    queryFn: () => api.devices.status(device.id),
    refetchInterval: device.kind === 'ExternalDisk' ? 5000 : false,
    staleTime: 0,
  })
}

export function DeviceStatusBadge({ device, withRefresh = false }: { device: Device; withRefresh?: boolean }) {
  const { t } = useI18n()
  const notify = useNotify()
  const status = useDeviceStatus(device)

  let content: React.ReactNode
  if (status.isPending) {
    content = <Badge variant="outline">{t('devices.status.checking')}</Badge>
  } else if (status.isError) {
    content = (
      <Badge variant="outline" className={tone.unreachable} title={notify.describe(status.error)}>
        {t('devices.status.unreachable')}
      </Badge>
    )
  } else {
    const state = status.data.state
    content = (
      <Badge variant="outline" className={cn(tone[state])} title={status.data.message ?? status.data.mountPoint}>
        {t(`devices.status.${state}` as MessageKey)}
        {status.data.readOnly && ` · ${t('devices.status.readonly')}`}
      </Badge>
    )
  }

  return (
    <span className="inline-flex items-center gap-1">
      {content}
      {withRefresh && (
        <Button
          variant="ghost"
          size="icon-xs"
          aria-label={t('common.refresh')}
          disabled={status.isFetching}
          onClick={() => void status.refetch()}
        >
          <RefreshCwIcon className={cn(status.isFetching && 'animate-spin')} />
        </Button>
      )}
    </span>
  )
}
