import { useQuery } from '@tanstack/react-query'
import { RefreshCwIcon } from 'lucide-react'
import { Button } from '@/components/ui/button'
import { Field, FieldDescription, FieldLabel } from '@/components/ui/field'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { api } from '@/lib/api'
import { formatBytes } from '@/lib/format'
import { useI18n } from '@/lib/i18n'
import { useNotify } from '@/lib/notify'
import type { Disk } from '@/lib/types'
import { cn } from '@/lib/utils'

/** What a person would call the disk: its label, else its model, else its device. */
export function diskName(disk: Disk, fallback: string): string {
  return disk.label ?? disk.model ?? disk.device ?? fallback
}

/**
 * Lists the disks that are attached now, to choose the one to add. The list is read again when
 * the dialog opens, every few seconds, and when asked, because a disk may be plugged in at any time.
 */
export function DiskPicker({
  uuid,
  onPick,
  /** The device being edited, whose own disk is not "already added". */
  currentDeviceId,
  devices,
}: {
  uuid: string
  onPick: (disk: Disk) => void
  currentDeviceId?: string
  devices: { id: string; name: string }[]
}) {
  const { t } = useI18n()
  const notify = useNotify()
  const disks = useQuery({ queryKey: ['disks'], queryFn: api.disks, refetchInterval: 5000, staleTime: 0 })

  const describe = (disk: Disk): string => {
    const parts = [
      diskName(disk, t('devices.disks.unknown')),
      disk.sizeBytes != null ? formatBytes(disk.sizeBytes) : null,
      disk.fileSystem,
      disk.removable ? null : t('devices.disks.internal'),
      disk.mountPoint ? t('devices.disks.mounted') : t('devices.disks.unmounted'),
    ]
    const owner =
      disk.deviceId && disk.deviceId !== currentDeviceId ? devices.find((item) => item.id === disk.deviceId) : undefined
    if (owner) parts.push(t('devices.disks.added', { name: owner.name }))
    return parts.filter(Boolean).join(' · ')
  }

  const list = disks.data ?? []
  const items = list.map((disk) => ({ value: disk.uuid, label: describe(disk) }))
  const selected = list.find((disk) => disk.uuid.toLowerCase() === uuid.trim().toLowerCase())

  return (
    <Field>
      <FieldLabel>{t('devices.disks.title')}</FieldLabel>
      <div className="flex gap-2">
        <Select
          items={items}
          value={selected?.uuid ?? null}
          onValueChange={(value) => {
            const disk = list.find((item) => item.uuid === value)
            if (disk) onPick(disk)
          }}
        >
          <SelectTrigger className="w-full min-w-0">
            <SelectValue placeholder={t('devices.disks.placeholder')} />
          </SelectTrigger>
          <SelectContent className="w-auto max-w-[32rem]">
            {list.map((disk, index) => (
              <SelectItem
                key={disk.uuid}
                value={disk.uuid}
                disabled={disk.deviceId != null && disk.deviceId !== currentDeviceId}
              >
                <span className="flex flex-col">
                  <span>{items[index].label}</span>
                  <span className="font-mono text-xs text-muted-foreground">
                    {disk.device} · {disk.uuid}
                  </span>
                </span>
              </SelectItem>
            ))}
          </SelectContent>
        </Select>
        <Button
          type="button"
          variant="outline"
          size="icon"
          aria-label={t('devices.disks.refresh')}
          title={t('devices.disks.refresh')}
          disabled={disks.isFetching}
          onClick={() => void disks.refetch()}
        >
          <RefreshCwIcon className={cn(disks.isFetching && 'animate-spin')} />
        </Button>
      </div>
      {disks.isError && <p className="text-sm text-destructive">{notify.describe(disks.error)}</p>}
      {disks.isSuccess && list.length === 0 && <FieldDescription>{t('devices.disks.none')}</FieldDescription>}
      <FieldDescription>{t('devices.disks.hint')}</FieldDescription>
    </Field>
  )
}
